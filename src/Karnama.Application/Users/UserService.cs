using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Auth;
using Karnama.Application.Common;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Users;

public sealed record UserRoleRef(Guid Id, string Name, string? SystemKey);

public sealed record UserListItemDto(Guid Id, string DisplayName, string Phone, UserKind Kind, bool IsActive, bool IsDemo, bool TwoFactorEnabled,
    IReadOnlyList<UserRoleRef> Roles, DateTimeOffset CreatedAt, bool IsDeleted);

public sealed record UpsertUserRequest(string DisplayName, string Phone, List<Guid> RoleIds, bool IsActive);

public sealed class UpsertUserValidator : AbstractValidator<UpsertUserRequest>
{
    public UpsertUserValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100).WithMessage("نام باید بین ۱ تا ۱۰۰ کاراکتر باشد.");
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalize(p, out _)).WithMessage("شماره‌ی موبایل معتبر نیست (مثل ۰۹۱۲۱۲۳۴۵۶۷).");
        RuleFor(x => x.RoleIds).NotNull().WithMessage("نقش‌ها را انتخاب کنید.");
    }
}

/// <summary>
/// Staff accounts and their roles. Rules: only an Owner can grant or remove the Owner role; the last active real Owner
/// can't lose it or be deactivated; demo sessions can only touch demo accounts.
/// </summary>
public sealed class UserService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock, IRealtimeNotifier realtime, IExcelExporter excel, SessionStamps stamps)
{
    public async Task<KeysetPage<UserListItemDto>> ListAsync(KeysetQuery q, UserKind? kind, CancellationToken ct)
    {
        var query = q.IncludeDeleted ? db.Users.IgnoreQueryFilters() : db.Users;
        // Staff only: client logins (and their numbers) belong to the clients page and clients.contact.
        _ = kind;
        query = query.Where(u => u.Kind == UserKind.Staff);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = Keyset.Like(q.Search);
            var digits = new string(q.Search.Where(char.IsAsciiDigit).ToArray());
            query = query.Where(u => EF.Functions.ILike(u.DisplayName, like) || (digits.Length >= 4 && u.Phone.Contains(digits)));
        }
        var page = await Keyset.PageAsync(query.AsNoTracking().Include(u => u.Roles), q, x => x, u => u.Id, ct);
        var roles = await RoleNamesAsync(ct);
        return new KeysetPage<UserListItemDto>(page.Items.Select(u => ToDto(u, roles)).ToList(), page.NextCursor);
    }

    public async Task<Result<UserListItemDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.IgnoreQueryFilters().AsNoTracking().Include(x => x.Roles).FirstOrDefaultAsync(x => x.Id == id && x.Kind == UserKind.Staff, ct);
        return u is null ? Error.NotFound("users.notFound", "کاربر پیدا نشد.") : ToDto(u, await RoleNamesAsync(ct));
    }

    public async Task<Result<UserListItemDto>> CreateAsync(UpsertUserRequest r, CancellationToken ct)
    {
        PhoneNumber.TryNormalize(r.Phone, out var phone);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone && !u.IsDeleted, ct))
            return Error.Conflict("users.phoneTaken", "این شماره برای کاربر دیگری ثبت شده است.");
        var created = User.Create(phone, r.DisplayName, UserKind.Staff, isDemo: me.IsDemo);
        if (created.IsFailure) return created.Error!;
        var user = created.Value;
        var roles = await ApplyRolesAsync(user, r.RoleIds, ct);
        if (roles.IsFailure) return roles.Error!;
        if (!r.IsActive) user.Deactivate();
        db.Users.Add(user);
        audit.Add("users.created", nameof(User), user.Id.ToString(), new { r.DisplayName, phone = PhoneNumber.Mask(phone), r.RoleIds });
        await db.SaveChangesAsync(ct);
        return ToDto(user, await RoleNamesAsync(ct));
    }

    public async Task<Result<UserListItemDto>> UpdateAsync(Guid id, UpsertUserRequest r, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id && u.Kind == UserKind.Staff, ct);
        if (user is null) return Error.NotFound("users.notFound", "کاربر پیدا نشد.");
        var guard = await GuardAsync(user, ct);
        if (guard.IsFailure) return guard.Error!;

        PhoneNumber.TryNormalize(r.Phone, out var phone);
        if (phone != user.Phone)
            return Error.Validation("users.phoneLocked", "شماره‌ی ورود قابل تغییر نیست؛ برای شماره‌ی جدید کاربر تازه بسازید و این را غیرفعال کنید.");

        var beforeRoles = user.Roles.Select(x => x.RoleId).ToList();
        var renamed = user.Rename(r.DisplayName);
        if (renamed.IsFailure) return renamed.Error!;
        var roles = await ApplyRolesAsync(user, r.RoleIds, ct);
        if (roles.IsFailure) return roles.Error!;
        if (!r.IsActive && user.IsActive)
        {
            var last = await LastOwnerCheckAsync(user, removingOwner: true, ct);
            if (last.IsFailure) return last.Error!;
            user.Deactivate();
        }
        else if (r.IsActive && !user.IsActive) user.Activate();

        db.MarkModified(user);
        audit.Add("users.updated", nameof(User), id.ToString(), new { r.DisplayName, beforeRoles, afterRoles = r.RoleIds, r.IsActive });
        await db.SaveChangesAsync(ct);
        await stamps.ForgetAsync([user.Id], ct);
        await realtime.PermissionsChangedAsync([user.Id], ct);
        return ToDto(user, await RoleNamesAsync(ct));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id && u.Kind == UserKind.Staff, ct);
        if (user is null) return Error.NotFound("users.notFound", "کاربر پیدا نشد.");
        if (user.Id == me.UserId) return Error.Conflict("users.self", "نمی‌توانید حساب خودتان را حذف کنید.");
        var guard = await GuardAsync(user, ct);
        if (guard.IsFailure) return guard;
        var last = await LastOwnerCheckAsync(user, removingOwner: true, ct);
        if (last.IsFailure) return last;
        user.Deactivate();
        user.SoftDelete(me.UserId, clock.GetUtcNow());
        // End every session of the removed user.
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
        audit.Add("users.deleted", nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);
        await stamps.ForgetAsync([user.Id], ct);
        return Result.Success();
    }

    public async Task<Result<UserListItemDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id && u.Kind == UserKind.Staff, ct);
        if (user is null) return Error.NotFound("users.notFound", "کاربر پیدا نشد.");
        var guard = await GuardAsync(user, ct);
        if (guard.IsFailure) return guard.Error!;
        if (await db.Users.AnyAsync(u => u.Phone == user.Phone, ct)) return Error.Conflict("users.phoneTaken", "این شماره برای کاربر دیگری ثبت شده است.");
        user.SoftRestore();
        user.Activate();
        audit.Add("users.restored", nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(user, await RoleNamesAsync(ct));
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Include(u => u.Roles).Where(u => u.Kind == UserKind.Staff).OrderBy(u => u.DisplayName).ToListAsync(ct);
        var roles = await RoleNamesAsync(ct);
        return excel.Export("Users", ["نام", "موبایل", "نقش‌ها", "فعال", "ورود دومرحله‌ای"],
            users.Select(u => (IReadOnlyList<object?>)[u.DisplayName, u.Phone, string.Join("، ", u.Roles.Select(r => roles.GetValueOrDefault(r.RoleId)?.Name)), u.IsActive ? "بله" : "نه", u.TwoFactorEnabled ? "بله" : "نه"]));
    }

    /// <summary>Demo sessions may only change demo accounts; nobody but an Owner touches an Owner.</summary>
    private async Task<Result> GuardAsync(User target, CancellationToken ct)
    {
        if (me.IsDemo && !target.IsDemo) return Error.Forbidden("users.demoGuard", "در حالت نمایشی فقط حساب‌های نمونه قابل تغییرند.");
        if (await PermissionLoader.IsOwnerAsync(db, target.Id, ct) && !await CallerIsOwnerAsync(ct))
            return Error.Forbidden("users.ownerOnly", "فقط مالک می‌تواند حساب یک مالک را تغییر دهد.");
        return Result.Success();
    }

    private Task<bool> CallerIsOwnerAsync(CancellationToken ct) => PermissionLoader.IsOwnerAsync(db, me.UserId ?? Guid.Empty, ct);

    private async Task<Result> ApplyRolesAsync(User user, List<Guid> roleIds, CancellationToken ct)
    {
        var wanted = roleIds.Distinct().ToList();
        var roles = await db.Roles.Where(r => wanted.Contains(r.Id)).Select(r => new { r.Id, r.SystemKey }).ToListAsync(ct);
        if (roles.Count != wanted.Count) return Error.Validation("users.unknownRole", "یکی از نقش‌ها وجود ندارد.");
        if (roles.Any(r => r.SystemKey == SystemRoles.Client)) return Error.Validation("users.clientRole", "نقش «مشتری» از صفحه‌ی مشتری‌ها داده می‌شود.");

        var ownerRoleId = await db.Roles.Where(r => r.SystemKey == SystemRoles.Owner).Select(r => r.Id).FirstAsync(ct);
        var callerIsOwner = await CallerIsOwnerAsync(ct);
        var current = user.Roles.Select(r => r.RoleId).ToHashSet();
        var changed = wanted.Where(id => !current.Contains(id)).Concat(current.Where(id => !wanted.Contains(id))).ToList();
        if (!callerIsOwner && changed.Count > 0)
        {
            // No self-promotion: a non-owner can't change their own roles, and can only grant or take away
            // roles whose every permission they hold themselves at the same scope or wider.
            if (user.Id == me.UserId) return Error.Forbidden("users.selfRoles", "نقش‌های خودتان را فقط مالک می‌تواند تغییر دهد.");
            var grants = await db.Roles.Where(r => changed.Contains(r.Id)).SelectMany(r => r.Permissions.Select(p => new { p.Code, p.Scope })).ToListAsync(ct);
            if (grants.Any(g => !me.Has(g.Code, g.Scope)))
                return Error.Forbidden("users.roleTooStrong", "این نقش دسترسی‌هایی دارد که خودتان ندارید؛ فقط مالک می‌تواند آن را بدهد یا بگیرد.");
        }
        var hadOwner = user.Roles.Any(r => r.RoleId == ownerRoleId);
        var wantsOwner = wanted.Contains(ownerRoleId);
        if (hadOwner != wantsOwner)
        {
            if (!callerIsOwner)
                return Error.Forbidden("users.ownerOnly", "فقط مالک می‌تواند نقش «مالک» را بدهد یا بگیرد.");
            if (hadOwner && !wantsOwner)
            {
                var last = await LastOwnerCheckAsync(user, removingOwner: true, ct);
                if (last.IsFailure) return last;
            }
        }

        foreach (var r in user.Roles.Select(r => r.RoleId).Where(id => !wanted.Contains(id)).ToList()) user.RemoveRole(r);
        foreach (var id in wanted) user.AssignRole(id);
        if (changed.Count > 0) user.BumpSecurityStamp(); // tokens issued with the old roles stop working
        return Result.Success();
    }

    private async Task<Result> LastOwnerCheckAsync(User user, bool removingOwner, CancellationToken ct)
    {
        if (!removingOwner || user.IsDemo) return Result.Success();
        var ownerRoleId = await db.Roles.Where(r => r.SystemKey == SystemRoles.Owner).Select(r => r.Id).FirstAsync(ct);
        if (user.Roles.All(r => r.RoleId != ownerRoleId) && !await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == ownerRoleId, ct))
            return Result.Success();
        var others = await (from u in db.Users
                            join ur in db.UserRoles on u.Id equals ur.UserId
                            where ur.RoleId == ownerRoleId && u.Id != user.Id && u.IsActive && !u.IsDemo
                            select u.Id).AnyAsync(ct);
        return others ? Result.Success() : Error.Conflict("users.lastOwner", "حداقل یک مالک فعال لازم است؛ اول مالک دیگری تعیین کنید.");
    }

    private async Task<Dictionary<Guid, UserRoleRef>> RoleNamesAsync(CancellationToken ct)
    {
        var rows = await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Name, r.SystemKey }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new UserRoleRef(r.Id, r.Name.Get(me.LanguageCode), r.SystemKey));
    }

    private static UserListItemDto ToDto(User u, Dictionary<Guid, UserRoleRef> roles) =>
        new(u.Id, u.DisplayName, u.Phone, u.Kind, u.IsActive, u.IsDemo, u.TwoFactorEnabled,
            u.Roles.Select(r => roles.GetValueOrDefault(r.RoleId)).Where(r => r is not null).Select(r => r!).ToList(), u.CreatedAt, u.IsDeleted);
}
