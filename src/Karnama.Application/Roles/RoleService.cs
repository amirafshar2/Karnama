using Karnama.Application.Abstractions;
using Karnama.Application.Auth;
using Karnama.Application.Common;
using Karnama.Domain.Auditing;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Roles;

public sealed class RoleService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, IRealtimeNotifier realtime, TimeProvider clock, SessionStamps stamps)
{
    public static IReadOnlyList<PermissionDefinitionDto> Catalog() =>
        Permissions.Catalog.Select(p => new PermissionDefinitionDto(p.Code, p.Group, p.AllowedScopes)).ToList();

    public async Task<KeysetPage<RoleListItemDto>> ListAsync(KeysetQuery query, CancellationToken ct)
    {
        var source = query.IncludeDeleted ? db.Roles.IgnoreQueryFilters() : db.Roles;

        // Roles are a small table; names are translatable JSON, so search and order happen in memory.
        var rows = await source.AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.SystemKey, r.IsDeleted, r.Version,
                PermissionCount = r.Permissions.Count,
                UserCount = db.UserRoles.Count(ur => ur.RoleId == r.Id),
            })
            .ToListAsync(ct);

        var lang = me.LanguageCode;
        IEnumerable<RoleListItemDto> items = rows
            .Select(r => new RoleListItemDto(r.Id, r.Name.Get(lang), r.Name.Values, r.Description, r.SystemKey is not null,
                r.SystemKey, r.PermissionCount, r.UserCount, r.IsDeleted, r.Version))
            .OrderByDescending(r => r.IsSystem).ThenByDescending(r => r.Id); // system roles, then newest first

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var q = query.Search.Trim();
            items = items.Where(r => r.NameValues.Values.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase))
                                     || (r.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var ordered = items.ToList();
        var start = 0;
        if (DecodeCursor(query.Cursor) is { } after)
        {
            var idx = ordered.FindIndex(r => r.Id == after);
            start = idx < 0 ? ordered.Count : idx + 1;
        }
        var page = ordered.Skip(start).Take(query.SafeLimit + 1).ToList();
        var hasMore = page.Count > query.SafeLimit;
        if (hasMore) page.RemoveAt(page.Count - 1);
        return new KeysetPage<RoleListItemDto>(page, hasMore ? EncodeCursor(page[^1].Id) : null);
    }

    public async Task<Result<RoleDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        return role is null ? Error.NotFound("roles.notFound", "نقش پیدا نشد.") : ToDetail(role);
    }

    public async Task<Result<RoleDetailDto>> CreateAsync(UpsertRoleRequest request, CancellationToken ct)
    {
        var created = Role.Create(new LocalizedText(request.Name), request.Description, ToGrants(request));
        if (created.IsFailure) return created.Error!;
        var guard = await GuardAsync(null, ToGrants(request), ct);
        if (guard.IsFailure) return guard.Error!;

        var role = created.Value;
        db.Roles.Add(role);
        audit.Add(AuditActions.RoleCreated, nameof(Role), role.Id.ToString(), new { name = request.Name, permissions = request.Permissions });
        await db.SaveChangesAsync(ct);
        return ToDetail(role);
    }

    public async Task<Result<RoleDetailDto>> UpdateAsync(Guid id, UpsertRoleRequest request, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Error.NotFound("roles.notFound", "نقش پیدا نشد.");
        if (request.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
        db.SetOriginalVersion(role, v);

        var before = role.Permissions.Select(p => new GrantDto(p.Code, p.Scope)).ToList();
        var guard = await GuardAsync(role.Id, before.Select(g => new PermissionGrant(g.Code, g.Scope)).Concat(ToGrants(request)), ct);
        if (guard.IsFailure) return guard.Error!;
        var updated = role.Update(new LocalizedText(request.Name), request.Description, ToGrants(request));
        if (updated.IsFailure) return updated.Error!;

        // Grants live in a child table: touch the role row so its version (xmin) moves and stale editors get 409.
        db.MarkModified(role);
        audit.Add(AuditActions.RoleUpdated, nameof(Role), role.Id.ToString(), new { name = request.Name, before, after = request.Permissions });
        await db.SaveChangesAsync(ct);
        var after = role.Permissions.Select(p => new GrantDto(p.Code, p.Scope)).ToHashSet();
        await NotifyMembersAsync(role.Id, grantsChanged: !after.SetEquals(before), ct);
        return ToDetail(role);
    }

    public async Task<Result> DeleteAsync(Guid id, uint? version, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Error.NotFound("roles.notFound", "نقش پیدا نشد.");
        if (version is { } v) db.SetOriginalVersion(role, v);
        var guard = await GuardAsync(role.Id, role.Permissions.Select(p => new PermissionGrant(p.Code, p.Scope)), ct);
        if (guard.IsFailure) return guard;

        var deleted = role.Delete(me.UserId, clock.GetUtcNow());
        if (deleted.IsFailure) return deleted;

        audit.Add(AuditActions.RoleDeleted, nameof(Role), role.Id.ToString());
        await db.SaveChangesAsync(ct);
        await NotifyMembersAsync(role.Id, grantsChanged: true, ct);
        return Result.Success();
    }

    public async Task<Result<RoleDetailDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Error.NotFound("roles.notFound", "نقش پیدا نشد.");
        var guard = await GuardAsync(role.Id, role.Permissions.Select(p => new PermissionGrant(p.Code, p.Scope)), ct);
        if (guard.IsFailure) return guard.Error!;
        role.Restore();
        audit.Add(AuditActions.RoleRestored, nameof(Role), role.Id.ToString());
        await db.SaveChangesAsync(ct);
        await NotifyMembersAsync(role.Id, grantsChanged: true, ct);
        return ToDetail(role);
    }

    /// <summary>
    /// No privilege escalation through roles (same rule as <c>users.roleTooStrong</c>): a non-owner can only create,
    /// edit, delete or restore a role whose every grant (before and after) they hold themselves at that scope or wider,
    /// and can't edit a role they hold. Owners are unrestricted. Checked against the database, not the token.
    /// </summary>
    private async Task<Result> GuardAsync(Guid? roleId, IEnumerable<PermissionGrant> grants, CancellationToken ct)
    {
        var callerId = me.UserId ?? Guid.Empty;
        if (await PermissionLoader.IsOwnerAsync(db, callerId, ct)) return Result.Success();
        if (roleId is { } rid && await db.UserRoles.AnyAsync(ur => ur.UserId == callerId && ur.RoleId == rid, ct))
            return Error.Forbidden("roles.ownRole", "نقشی را که خودتان دارید فقط مالک می‌تواند تغییر دهد.");
        var mine = await PermissionLoader.LoadAsync(db, callerId, ct);
        if (grants.Any(g => !mine.TryGetValue(g.Code, out var scope) || scope < g.Scope))
            return Error.Forbidden("roles.tooStrong", "این نقش دسترسی‌هایی دارد که خودتان ندارید؛ فقط مالک می‌تواند آن را بسازد یا تغییر دهد.");
        return Result.Success();
    }

    private async Task NotifyMembersAsync(Guid roleId, bool grantsChanged, CancellationToken ct)
    {
        if (grantsChanged) await stamps.BumpRoleMembersAsync(roleId, ct);
        var userIds = await db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
        if (userIds.Count > 0) await realtime.PermissionsChangedAsync(userIds, ct);
    }

    private static IEnumerable<PermissionGrant> ToGrants(UpsertRoleRequest r) =>
        (r.Permissions ?? []).Select(p => new PermissionGrant(p.Code, p.Scope));

    private static RoleDetailDto ToDetail(Role r) => new(
        r.Id, r.Name.Values, r.Description, r.IsSystem, r.SystemKey,
        r.Permissions.OrderBy(p => p.Code, StringComparer.Ordinal).Select(p => new GrantDto(p.Code, p.Scope)).ToList(),
        r.IsDeleted, r.Version, r.CreatedAt, r.UpdatedAt);

    private static string EncodeCursor(Guid id) => Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Guid? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(s);
            return bytes.Length == 16 ? new Guid(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
