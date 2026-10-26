using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Catalog;

public sealed record QuotaDto(Guid ServiceTypeId, int Count, string? ServiceName = null);

public sealed record PackageListItemDto(
    Guid Id, string Name, string Sku, string? Category, long Price, long? SpecialPrice, long EffectivePrice, PackagePeriod Period,
    int ValidityDays, PackageStatus Status, PageAccess Access, PackageBadge Badge, bool ShowOnSite, int ActiveClients, int? Capacity,
    IReadOnlyList<QuotaDto> Quotas, string? CoverUrl, bool IsDeleted, uint Version);

public sealed record PackageDto(
    Guid Id, IReadOnlyDictionary<string, string> NameValues, string Slug, string Sku, string? Category, IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> ShortDescription, string? DescriptionMarkdown, string? DescriptionHtml,
    long Price, long? CompareAtPrice, long? SpecialPrice, DateTimeOffset? SpecialFrom, DateTimeOffset? SpecialTo,
    PackagePeriod Period, int ValidityDays, DateTimeOffset? SaleFrom, DateTimeOffset? SaleTo, int? Capacity,
    IReadOnlyList<Guid> PlatformIds, PageManagement DefaultManagement, PageAccess Access,
    int ConsultSessions, int RevisionsPerItem, int DeliveryHours,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Features, IReadOnlyList<PackageFaq> Faq,
    PackageStatus Status, bool ShowOnSite, int SortOrder, PackageBadge Badge, string? SeoTitle, string? SeoDescription,
    IReadOnlyList<QuotaDto> Quotas, string? CoverUrl, int ActiveClients, bool IsDeleted, uint Version);

public sealed record UpsertPackageRequest(
    Dictionary<string, string> Name, string Slug, string Sku, string? Category, List<string>? Tags,
    Dictionary<string, string>? ShortDescription, string? DescriptionMarkdown,
    long Price, long? CompareAtPrice, long? SpecialPrice, DateTimeOffset? SpecialFrom, DateTimeOffset? SpecialTo,
    PackagePeriod Period, int ValidityDays, DateTimeOffset? SaleFrom, DateTimeOffset? SaleTo, int? Capacity,
    List<Guid>? PlatformIds, PageManagement DefaultManagement, PageAccess Access,
    int ConsultSessions, int RevisionsPerItem, int DeliveryHours,
    List<Dictionary<string, string>>? Features, List<PackageFaq>? Faq,
    PackageStatus Status, bool ShowOnSite, int SortOrder, PackageBadge Badge, string? SeoTitle, string? SeoDescription,
    List<QuotaDto> Quotas, uint? Version);

public sealed class UpsertPackageValidator : AbstractValidator<UpsertPackageRequest>
{
    public UpsertPackageValidator()
    {
        RuleFor(x => x.Name).Must(n => n is not null && n.Values.Any(v => !string.IsNullOrWhiteSpace(v))).WithMessage("نام پکیج الزامی است.");
        RuleFor(x => x.Slug).MaximumLength(80).WithMessage("نامک حداکثر ۸۰ کاراکتر است (خالی = خودکار از نام).");
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(30).WithMessage("کد پکیج الزامی است.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("قیمت نمی‌تواند منفی باشد.");
        RuleFor(x => x.ValidityDays).InclusiveBetween(1, 3650).WithMessage("مدت اعتبار باید بین ۱ تا ۳۶۵۰ روز باشد.");
        RuleFor(x => x.DescriptionMarkdown).MaximumLength(20_000).WithMessage("توضیحات حداکثر ۲۰ هزار کاراکتر است.");
        RuleFor(x => x.SeoTitle).MaximumLength(70).WithMessage("عنوان سئو حداکثر ۷۰ کاراکتر است.");
        RuleFor(x => x.SeoDescription).MaximumLength(170).WithMessage("توضیح سئو حداکثر ۱۷۰ کاراکتر است.");
        RuleFor(x => x.Quotas).NotNull().WithMessage("سهمیه‌ها را مشخص کنید (می‌تواند خالی باشد).");
        RuleFor(x => x.Period).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Access).IsInEnum();
        RuleFor(x => x.DefaultManagement).IsInEnum();
        RuleFor(x => x.Badge).IsInEnum();
    }
}

public sealed class PackageService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock, IContentRenderer renderer, IMediaStore media, IExcelExporter excel,
    IPublicCache cache)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<KeysetPage<PackageListItemDto>> ListAsync(KeysetQuery q, PackageStatus? status, CancellationToken ct)
    {
        var query = q.IncludeDeleted ? db.Packages.IgnoreQueryFilters() : db.Packages;
        if (status is { } st) query = query.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = Keyset.Like(q.Search);
            query = query.Where(p => EF.Functions.ILike(EF.Property<string>(p, "NameSearch"), like) || EF.Functions.ILike(p.Sku, like) || EF.Functions.ILike(p.Category ?? "", like));
        }
        var page = await Keyset.PageAsync(query.AsNoTracking().Include(p => p.Quotas), q, x => x, p => p.Id, ct);
        var ids = page.Items.Select(p => p.Id).ToList();
        var active = await ActiveCountsAsync(ids, ct);
        var names = await ServiceNamesAsync(ct);
        var now = clock.GetUtcNow();
        return new KeysetPage<PackageListItemDto>(page.Items.Select(p => new PackageListItemDto(
            p.Id, p.Name.Get(me.LanguageCode), p.Sku, p.Category, p.Price, p.SpecialPrice, p.PriceAt(now), p.Period, p.ValidityDays, p.Status, p.Access,
            p.Badge, p.ShowOnSite, active.GetValueOrDefault(p.Id), p.Capacity,
            p.Quotas.Select(x => new QuotaDto(x.ServiceTypeId, x.Count, names.GetValueOrDefault(x.ServiceTypeId))).ToList(),
            p.CoverImage is null ? null : media.PublicUrl(p.CoverImage), p.IsDeleted, p.Version)).ToList(), page.NextCursor);
    }

    public async Task<Result<PackageDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Packages.IgnoreQueryFilters().AsNoTracking().Include(x => x.Quotas).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
        return await ToDtoAsync(p, ct);
    }

    public async Task<Result<PackageDto>> CreateAsync(UpsertPackageRequest r, CancellationToken ct)
    {
        var check = await CheckReferencesAsync(r, null, ct);
        if (check.IsFailure) return check.Error!;
        var created = Package.Create(ToSpec(r), ToQuotas(r), Render(r.DescriptionMarkdown));
        if (created.IsFailure) return created.Error!;
        db.Packages.Add(created.Value);
        audit.Add("packages.created", nameof(Package), created.Value.Id.ToString(), new { r.Name, r.Sku, r.Price, r.Status });
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);   // the public site shows packages (prices, pages)
        return await ToDtoAsync(created.Value, ct);
    }

    public async Task<Result<PackageDto>> UpdateAsync(Guid id, UpsertPackageRequest r, CancellationToken ct)
    {
        var p = await db.Packages.Include(x => x.Quotas).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
        if (r.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
        db.SetOriginalVersion(p, v);
        var check = await CheckReferencesAsync(r, id, ct);
        if (check.IsFailure) return check.Error!;
        var before = new { p.Price, p.SpecialPrice, p.Status };
        var u = p.Update(ToSpec(r), ToQuotas(r), Render(r.DescriptionMarkdown));
        if (u.IsFailure) return u.Error!;
        db.MarkModified(p);
        // Price changes are always audited (docs/PROMPT.md §5.10).
        audit.Add("packages.updated", nameof(Package), id.ToString(), new { before, after = new { r.Price, r.SpecialPrice, r.Status } });
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);   // the public site shows packages (prices, pages)
        return await ToDtoAsync(p, ct);
    }

    public async Task<Result<PackageDto>> SetCoverAsync(Guid id, Stream content, long length, CancellationToken ct)
    {
        var p = await db.Packages.Include(x => x.Quotas).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
        var saved = await media.SaveImageAsync(content, length, "packages", ct);
        if (saved.IsFailure) return saved.Error!;
        p.SetCover(saved.Value);
        audit.Add("packages.cover", nameof(Package), id.ToString(), new { cover = saved.Value });
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);   // the public site shows packages (prices, pages)
        return await ToDtoAsync(p, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Packages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
        p.Delete(me.UserId, clock.GetUtcNow());
        audit.Add("packages.deleted", nameof(Package), id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);   // the public site shows packages (prices, pages)
        return Result.Success();
    }

    public async Task<Result<PackageDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Packages.IgnoreQueryFilters().Include(x => x.Quotas).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("packages.notFound", "پکیج پیدا نشد.");
        if (await db.Packages.AnyAsync(x => x.Slug == p.Slug || x.Sku == p.Sku, ct))
            return Error.Conflict("packages.duplicate", "پکیج دیگری با همین نامک یا کد ساخته شده است.");
        p.Restore();
        audit.Add("packages.restored", nameof(Package), id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);   // the public site shows packages (prices, pages)
        return await ToDtoAsync(p, ct);
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var rows = await db.Packages.AsNoTracking().Include(p => p.Quotas).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var names = await ServiceNamesAsync(ct);
        var active = await ActiveCountsAsync(rows.Select(r => r.Id).ToList(), ct);
        return excel.Export("Packages", ["نام", "کد", "دسته", "قیمت (ریال)", "قیمت ویژه (ریال)", "دوره", "اعتبار (روز)", "سهمیه‌ها", "وضعیت", "مشتری فعال", "ظرفیت"],
            rows.Select(p => (IReadOnlyList<object?>)[p.Name.Get(me.LanguageCode), p.Sku, p.Category, p.Price, p.SpecialPrice, p.Period.ToString(), p.ValidityDays,
                string.Join("، ", p.Quotas.Select(q => $"{names.GetValueOrDefault(q.ServiceTypeId)} ×{q.Count}")), p.Status.ToString(), active.GetValueOrDefault(p.Id), p.Capacity]));
    }

    private async Task<Result> CheckReferencesAsync(UpsertPackageRequest r, Guid? id, CancellationToken ct)
    {
        var slug = Slug.From(string.IsNullOrWhiteSpace(r.Slug) ? new LocalizedText(r.Name).Get("fa") : r.Slug);
        var sku = r.Sku.Trim().ToUpperInvariant();
        if (await db.Packages.AnyAsync(p => p.Slug == slug && p.Id != id, ct)) return Error.Conflict("packages.slugTaken", "این نامک برای پکیج دیگری است.");
        if (await db.Packages.AnyAsync(p => p.Sku == sku && p.Id != id, ct)) return Error.Conflict("packages.skuTaken", "این کد برای پکیج دیگری است.");
        var serviceIds = r.Quotas.Select(q => q.ServiceTypeId).Distinct().ToList();
        if (serviceIds.Count > 0 && await db.ServiceTypes.CountAsync(s => serviceIds.Contains(s.Id), ct) != serviceIds.Count)
            return Error.Validation("packages.unknownService", "یکی از خدمت‌های سهمیه وجود ندارد.");
        var platformIds = (r.PlatformIds ?? []).Distinct().ToList();
        if (platformIds.Count > 0 && await db.Platforms.CountAsync(p => platformIds.Contains(p.Id), ct) != platformIds.Count)
            return Error.Validation("packages.unknownPlatform", "یکی از شبکه‌ها وجود ندارد.");
        return Result.Success();
    }

    private string? Render(string? markdown) => string.IsNullOrWhiteSpace(markdown) ? null : renderer.MarkdownToSafeHtml(markdown);

    private static PackageSpec ToSpec(UpsertPackageRequest r) => new(
        new LocalizedText(r.Name), r.Slug ?? string.Empty, r.Sku.Trim().ToUpperInvariant(), r.Category, r.Tags ?? [],
        new LocalizedText(r.ShortDescription), r.DescriptionMarkdown,
        r.Price, r.CompareAtPrice, r.SpecialPrice, r.SpecialFrom, r.SpecialTo,
        r.Period, r.ValidityDays, r.SaleFrom, r.SaleTo, r.Capacity,
        r.PlatformIds ?? [], r.DefaultManagement, r.Access,
        r.ConsultSessions, r.RevisionsPerItem, r.DeliveryHours,
        (r.Features ?? []).Select(f => new LocalizedText(f)).ToList(), r.Faq ?? [],
        r.Status, r.ShowOnSite, r.SortOrder, r.Badge, r.SeoTitle, r.SeoDescription);

    private static List<QuotaSpec> ToQuotas(UpsertPackageRequest r) => r.Quotas.Select(q => new QuotaSpec(q.ServiceTypeId, q.Count)).ToList();

    private async Task<Dictionary<Guid, int>> ActiveCountsAsync(List<Guid> ids, CancellationToken ct) =>
        await db.ClientPackages.AsNoTracking().Where(c => ids.Contains(c.PackageId) && c.Status == ClientPackageStatus.Active)
            .GroupBy(c => c.PackageId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private async Task<Dictionary<Guid, string>> ServiceNamesAsync(CancellationToken ct)
    {
        var rows = await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Name.Get(me.LanguageCode));
    }

    private async Task<PackageDto> ToDtoAsync(Package p, CancellationToken ct)
    {
        var names = await ServiceNamesAsync(ct);
        var active = await ActiveCountsAsync([p.Id], ct);
        return new PackageDto(
            p.Id, p.Name.Values, p.Slug, p.Sku, p.Category, p.Tags, p.ShortDescription.Values, p.DescriptionMarkdown, p.DescriptionHtml,
            p.Price, p.CompareAtPrice, p.SpecialPrice, p.SpecialFrom, p.SpecialTo, p.Period, p.ValidityDays, p.SaleFrom, p.SaleTo, p.Capacity,
            p.PlatformIds, p.DefaultManagement, p.Access, p.ConsultSessions, p.RevisionsPerItem, p.DeliveryHours,
            p.Features.Select(f => f.Values).ToList(), p.Faq, p.Status, p.ShowOnSite, p.SortOrder, p.Badge, p.SeoTitle, p.SeoDescription,
            p.Quotas.Select(q => new QuotaDto(q.ServiceTypeId, q.Count, names.GetValueOrDefault(q.ServiceTypeId))).ToList(),
            p.CoverImage is null ? null : media.PublicUrl(p.CoverImage), active.GetValueOrDefault(p.Id), p.IsDeleted, p.Version);
    }

    /// <summary>Frozen copy of quotas for a client contract.</summary>
    public static string SnapshotQuotas(Package p, IReadOnlyDictionary<Guid, string> names) =>
        JsonSerializer.Serialize(p.Quotas.Select(q => new QuotaDto(q.ServiceTypeId, q.Count, names.GetValueOrDefault(q.ServiceTypeId))), Json);
}
