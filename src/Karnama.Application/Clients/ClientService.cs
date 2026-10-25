using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Catalog;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Clients;

public sealed record ClientAccountDto(Guid PlatformId, string Handle, PageManagement Management);

public sealed record ActivePackageDto(Guid Id, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int DaysLeft, int TotalDays, long Price, IReadOnlyList<QuotaDto> Quotas);

public sealed record ClientListItemDto(
    Guid Id, string BusinessName, string? Industry, string? City, string? ContactName, string? Mobile, ClientStatus Status,
    IReadOnlyList<ClientAccountDto> Accounts, string? ActivePackage, int? DaysLeft, bool HasPortal, bool IsDeleted, uint Version);

public sealed record ClientDto(
    Guid Id, string BusinessName, string? Industry, string? ContactName, string? Mobile, string? City, string? InternalNote,
    ClientStatus Status, IReadOnlyList<ClientAccountDto> Accounts, bool HasPortal, ActivePackageDto? ActivePackage,
    IReadOnlyList<ActivePackageDto> PackageHistory, bool IsDeleted, uint Version);

public sealed record UpsertClientRequest(
    string BusinessName, string? Industry, string? ContactName, string Mobile, string? City, string? InternalNote,
    ClientStatus Status, List<ClientAccountDto>? Accounts, uint? Version);

public sealed record AssignPackageRequest(Guid PackageId, DateTimeOffset? StartsAt, string? Note);

public sealed class UpsertClientValidator : AbstractValidator<UpsertClientRequest>
{
    public UpsertClientValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(120).WithMessage("نام کسب‌وکار الزامی است (حداکثر ۱۲۰ کاراکتر).");
        RuleFor(x => x.Mobile).Must(m => PhoneNumber.TryNormalize(m, out _)).WithMessage("شماره‌ی موبایل معتبر نیست (مثل ۰۹۱۲۱۲۳۴۵۶۷).");
        RuleFor(x => x.InternalNote).MaximumLength(2000).WithMessage("یادداشت حداکثر ۲۰۰۰ کاراکتر است.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

/// <summary>
/// Clients. Staff never see a client's mobile number unless they hold clients.contact (only admins talk to clients).
/// Scope: clients.view:All sees everyone; :Assigned sees clients of work assigned to them (from M3).
/// </summary>
public sealed class ClientService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock, IExcelExporter excel)
{
    private bool CanSeeContact => me.Has(Permissions.ClientsContact, PermissionScope.All);

    private IQueryable<Client> Visible(bool includeDeleted)
    {
        var q = includeDeleted ? db.Clients.IgnoreQueryFilters() : db.Clients;
        // Assigned scope (work items) arrives with M3; until then only :All sees clients.
        return me.Has(Permissions.ClientsView, PermissionScope.All) ? q : q.Where(_ => false);
    }

    public async Task<KeysetPage<ClientListItemDto>> ListAsync(KeysetQuery q, ClientStatus? status, CancellationToken ct)
    {
        var query = Visible(q.IncludeDeleted);
        if (status is { } st) query = query.Where(c => c.Status == st);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = Keyset.Like(q.Search);
            var digits = new string(q.Search.Where(char.IsAsciiDigit).ToArray());
            query = query.Where(c => EF.Functions.ILike(c.BusinessName, like) || EF.Functions.ILike(c.City ?? "", like)
                                     || EF.Functions.ILike(c.Industry ?? "", like) || EF.Functions.ILike(c.ContactName ?? "", like)
                                     || (CanSeeContact && digits.Length >= 4 && c.Mobile.Contains(digits)));
        }
        var page = await Keyset.PageAsync(query.AsNoTracking(), q, x => x, c => c.Id, ct);
        var now = clock.GetUtcNow();
        var ids = page.Items.Select(c => c.Id).ToList();
        var active = await db.ClientPackages.AsNoTracking()
            .Where(cp => ids.Contains(cp.ClientId) && cp.Status == ClientPackageStatus.Active)
            .OrderByDescending(cp => cp.EndsAt).ToListAsync(ct);
        return new KeysetPage<ClientListItemDto>(page.Items.Select(c =>
        {
            var pkg = active.FirstOrDefault(a => a.ClientId == c.Id);
            return new ClientListItemDto(c.Id, c.BusinessName, c.Industry, c.City, c.ContactName, CanSeeContact ? c.Mobile : null, c.Status,
                c.Accounts.Select(a => new ClientAccountDto(a.PlatformId, a.Handle, a.Management)).ToList(),
                pkg?.PackageName.Get(me.LanguageCode), pkg?.DaysLeft(now), c.PortalUserId is not null, c.IsDeleted, c.Version);
        }).ToList(), page.NextCursor);
    }

    public async Task<Result<ClientDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await Visible(true).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? Error.NotFound("clients.notFound", "مشتری پیدا نشد.") : await ToDtoAsync(c, ct);
    }

    public async Task<Result<ClientDto>> CreateAsync(UpsertClientRequest r, CancellationToken ct)
    {
        var check = await CheckPlatformsAsync(r, ct);
        if (check.IsFailure) return check.Error!;
        var created = Client.Create(ToSpec(r));
        if (created.IsFailure) return created.Error!;
        db.Clients.Add(created.Value);
        audit.Add("clients.created", nameof(Client), created.Value.Id.ToString(), new { r.BusinessName });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(created.Value, ct);
    }

    public async Task<Result<ClientDto>> UpdateAsync(Guid id, UpsertClientRequest r, CancellationToken ct)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound("clients.notFound", "مشتری پیدا نشد.");
        if (r.Version is { } v) db.SetOriginalVersion(c, v);
        var check = await CheckPlatformsAsync(r, ct);
        if (check.IsFailure) return check.Error!;
        // Staff without clients.contact never see the number, so they can't overwrite it either.
        var spec = CanSeeContact ? ToSpec(r) : ToSpec(r) with { Mobile = c.Mobile };
        var u = c.Update(spec);
        if (u.IsFailure) return u.Error!;
        db.MarkModified(c);
        audit.Add("clients.updated", nameof(Client), id.ToString(), new { r.BusinessName, r.Status });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(c, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound("clients.notFound", "مشتری پیدا نشد.");
        c.Delete(me.UserId, clock.GetUtcNow());
        audit.Add("clients.deleted", nameof(Client), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ClientDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Clients.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound("clients.notFound", "مشتری پیدا نشد.");
        c.Restore();
        audit.Add("clients.restored", nameof(Client), id.ToString());
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(c, ct);
    }

    /// <summary>Gives the client a portal login (OTP with the client's mobile). Idempotent.</summary>
    public async Task<Result<ClientDto>> EnablePortalAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound("clients.notFound", "مشتری پیدا نشد.");
        if (c.PortalUserId is null)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Phone == c.Mobile, ct);
            if (existing is not null && existing.Kind != UserKind.Client)
                return Error.Conflict("clients.phoneIsStaff", "این شماره برای یکی از همکاران ثبت شده؛ برای مشتری شماره‌ی دیگری بگذارید.");
            var clientRole = await db.Roles.FirstAsync(r => r.SystemKey == SystemRoles.Client, ct);
            var user = existing;
            if (user is null)
            {
                var created = User.Create(c.Mobile, c.ContactName ?? c.BusinessName, UserKind.Client, isDemo: me.IsDemo);
                if (created.IsFailure) return created.Error!;
                user = created.Value;
                db.Users.Add(user);
            }
            user.AssignRole(clientRole.Id);
            c.LinkPortalUser(user.Id);
            audit.Add("clients.portalEnabled", nameof(Client), id.ToString());
            await db.SaveChangesAsync(ct);
        }
        return await ToDtoAsync(c, ct);
    }

    public async Task<Result<ClientDto>> AssignPackageAsync(Guid clientId, AssignPackageRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.PackagesAssign, PermissionScope.All)) return Error.Forbidden();
        var now = clock.GetUtcNow();

        // Capacity is a hard limit: count + insert in one serializable transaction (two admins can't both take the last seat).
        var result = await db.InTransactionAsync<Result>(async token =>
        {
            var client = await Visible(false).FirstOrDefaultAsync(x => x.Id == clientId, token);
            if (client is null) return Error.NotFound("clients.notFound", "مشتری پیدا نشد.");
            var package = await db.Packages.Include(p => p.Quotas).FirstOrDefaultAsync(p => p.Id == r.PackageId, token);
            if (package is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
            if (package.Capacity is { } cap &&
                await db.ClientPackages.CountAsync(cp => cp.PackageId == package.Id && cp.Status == ClientPackageStatus.Active, token) >= cap)
                return Error.Conflict("clientPackages.full", "ظرفیت این پکیج پر است.");

            var names = (await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(token))
                .ToDictionary(x => x.Id, x => x.Name.Get(me.LanguageCode));
            var assigned = ClientPackage.Assign(client.Id, package, r.StartsAt ?? now, PackageService.SnapshotQuotas(package, names), now, r.Note);
            if (assigned.IsFailure) return assigned.Error!;
            db.ClientPackages.Add(assigned.Value);
            audit.Add("clientPackages.assigned", nameof(ClientPackage), assigned.Value.Id.ToString(),
                new { clientId, package = package.Sku, assigned.Value.Price, assigned.Value.StartsAt, assigned.Value.EndsAt });
            await db.SaveChangesAsync(token);
            return Result.Success();
        }, ct);
        return result.IsFailure ? result.Error! : await GetAsync(clientId, ct);
    }

    public async Task<Result<ClientDto>> CancelPackageAsync(Guid clientId, Guid clientPackageId, CancellationToken ct)
    {
        if (!me.Has(Permissions.PackagesAssign, PermissionScope.All)) return Error.Forbidden();
        var cp = await db.ClientPackages.FirstOrDefaultAsync(x => x.Id == clientPackageId && x.ClientId == clientId, ct);
        if (cp is null) return Error.NotFound("clientPackages.notFound", "پکیج مشتری پیدا نشد.");
        var r = cp.Cancel();
        if (r.IsFailure) return r.Error!;
        audit.Add("clientPackages.cancelled", nameof(ClientPackage), clientPackageId.ToString(), new { clientId });
        await db.SaveChangesAsync(ct);
        return await GetAsync(clientId, ct);
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var rows = await Visible(false).AsNoTracking().OrderBy(c => c.BusinessName).ToListAsync(ct);
        var headers = new List<string> { "کسب‌وکار", "صنف", "شخص رابط", "شهر", "وضعیت", "حساب‌ها" };
        if (CanSeeContact) headers.Insert(3, "موبایل");
        return excel.Export("Clients", headers, rows.Select(c =>
        {
            var row = new List<object?> { c.BusinessName, c.Industry, c.ContactName, c.City, c.Status.ToString(), string.Join("، ", c.Accounts.Select(a => "@" + a.Handle)) };
            if (CanSeeContact) row.Insert(3, c.Mobile);
            return (IReadOnlyList<object?>)row;
        }));
    }

    private async Task<Result> CheckPlatformsAsync(UpsertClientRequest r, CancellationToken ct)
    {
        var ids = (r.Accounts ?? []).Select(a => a.PlatformId).Distinct().ToList();
        if (ids.Count > 0 && await db.Platforms.CountAsync(p => ids.Contains(p.Id), ct) != ids.Count)
            return Error.Validation("clients.unknownPlatform", "یکی از شبکه‌ها وجود ندارد.");
        return Result.Success();
    }

    private static ClientSpec ToSpec(UpsertClientRequest r) => new(r.BusinessName, r.Industry, r.ContactName, r.Mobile, r.City, r.InternalNote, r.Status,
        (r.Accounts ?? []).Select(a => new ClientAccountSpec(a.PlatformId, a.Handle, a.Management)).ToList());

    private async Task<ClientDto> ToDtoAsync(Client c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var packages = await db.ClientPackages.AsNoTracking().Where(cp => cp.ClientId == c.Id).OrderByDescending(cp => cp.StartsAt).ToListAsync(ct);
        ActivePackageDto Map(ClientPackage cp) => new(cp.Id, cp.PackageName.Get(me.LanguageCode), cp.StartsAt, cp.EndsAt, cp.DaysLeft(now),
            (int)Math.Round((cp.EndsAt - cp.StartsAt).TotalDays), cp.Price,
            System.Text.Json.JsonSerializer.Deserialize<List<QuotaDto>>(cp.QuotasJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? []);
        var active = packages.FirstOrDefault(p => p.Status == ClientPackageStatus.Active && p.EndsAt > now);
        return new ClientDto(c.Id, c.BusinessName, c.Industry, c.ContactName, CanSeeContact ? c.Mobile : null, c.City, c.InternalNote, c.Status,
            c.Accounts.Select(a => new ClientAccountDto(a.PlatformId, a.Handle, a.Management)).ToList(), c.PortalUserId is not null,
            active is null ? null : Map(active), packages.Where(p => p != active).Select(Map).ToList(), c.IsDeleted, c.Version);
    }
}
