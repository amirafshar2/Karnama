using System.Text.RegularExpressions;
using Karnama.Domain.Common;

namespace Karnama.Domain.Catalog;

public enum PackagePeriod { Monthly = 1, Quarterly = 2, OneTime = 3 }
public enum PackageStatus { Draft = 1, Active = 2, Archived = 3 }
public enum PageManagement { AgencyManaged = 1, ClientManaged = 2 }
/// <summary>What the agency needs from the client's page (docs/PROMPT2.md §10.1).</summary>
public enum PageAccess { FullAccess = 1, UsernameOnly = 2 }
public enum PackageBadge { None = 0, BestSeller = 1, New = 2 }

/// <summary>Everything a sales package is, like a product (docs/PROMPT.md §6.4 + docs/PROMPT2.md §10.1). Money in rials.</summary>
public sealed record PackageSpec(
    LocalizedText Name, string Slug, string Sku, string? Category, IReadOnlyList<string> Tags,
    LocalizedText ShortDescription, string? DescriptionMarkdown,
    long Price, long? CompareAtPrice, long? SpecialPrice, DateTimeOffset? SpecialFrom, DateTimeOffset? SpecialTo,
    PackagePeriod Period, int ValidityDays, DateTimeOffset? SaleFrom, DateTimeOffset? SaleTo, int? Capacity,
    IReadOnlyList<Guid> PlatformIds, PageManagement DefaultManagement, PageAccess Access,
    int ConsultSessions, int RevisionsPerItem, int DeliveryHours,
    IReadOnlyList<LocalizedText> Features, IReadOnlyList<PackageFaq> Faq,
    PackageStatus Status, bool ShowOnSite, int SortOrder, PackageBadge Badge,
    string? SeoTitle, string? SeoDescription);

public sealed record PackageFaq(string Question, string Answer);

public sealed partial class Package : AuditableEntity
{
    private readonly List<PackageQuota> _quotas = [];

    private Package()
    {
        Name = new LocalizedText();
        ShortDescription = new LocalizedText();
        Slug = string.Empty;
        Sku = string.Empty;
        Tags = [];
        PlatformIds = [];
        Features = [];
        Faq = [];
        GalleryIds = [];
    }

    public LocalizedText Name { get; private set; }
    public string Slug { get; private set; }
    public string Sku { get; private set; }
    public string? Category { get; private set; }
    public List<string> Tags { get; private set; }
    public LocalizedText ShortDescription { get; private set; }
    public string? DescriptionMarkdown { get; private set; }
    /// <summary>Server-rendered, sanitized HTML of the description (whitelist only).</summary>
    public string? DescriptionHtml { get; private set; }

    public long Price { get; private set; }
    public long? CompareAtPrice { get; private set; }
    public long? SpecialPrice { get; private set; }
    public DateTimeOffset? SpecialFrom { get; private set; }
    public DateTimeOffset? SpecialTo { get; private set; }

    public PackagePeriod Period { get; private set; }
    public int ValidityDays { get; private set; }
    public DateTimeOffset? SaleFrom { get; private set; }
    public DateTimeOffset? SaleTo { get; private set; }
    public int? Capacity { get; private set; }

    public List<Guid> PlatformIds { get; private set; }
    public PageManagement DefaultManagement { get; private set; }
    public PageAccess Access { get; private set; }
    public int ConsultSessions { get; private set; }
    public int RevisionsPerItem { get; private set; }
    public int DeliveryHours { get; private set; }

    public List<LocalizedText> Features { get; private set; }
    public List<PackageFaq> Faq { get; private set; }

    public PackageStatus Status { get; private set; }
    public bool ShowOnSite { get; private set; }
    public int SortOrder { get; private set; }
    public PackageBadge Badge { get; private set; }
    public string? SeoTitle { get; private set; }
    public string? SeoDescription { get; private set; }
    public string? CoverImage { get; private set; }
    public List<string> GalleryIds { get; private set; }

    public uint Version { get; private set; }
    public IReadOnlyCollection<PackageQuota> Quotas => _quotas;

    [GeneratedRegex("^[\\p{L}\\p{N}]+(?:-[\\p{L}\\p{N}]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,29}$")]
    private static partial Regex SkuPattern();

    public static Result<Package> Create(PackageSpec spec, IReadOnlyList<QuotaSpec> quotas, string? descriptionHtml)
    {
        var p = new Package();
        var r = p.Update(spec, quotas, descriptionHtml);
        return r.IsSuccess ? p : r.Error!;
    }

    public Result Update(PackageSpec s, IReadOnlyList<QuotaSpec> quotas, string? descriptionHtml)
    {
        // Slug is optional input: normalised, or generated from the Persian name (automatic SEO-friendly URL).
        s = s with { Slug = Common.Slug.From(string.IsNullOrWhiteSpace(s.Slug) ? s.Name.Get("fa") : s.Slug) };
        if (s.Name.IsEmpty) return Error.Validation("packages.nameRequired", "نام پکیج الزامی است.");
        if (!SlugPattern().IsMatch(s.Slug ?? string.Empty) || s.Slug!.Length > 80)
            return Error.Validation("packages.invalidSlug", "نامک فقط حروف، عدد و خط تیره (مثل مدیریت-پیج-طلایی).");
        if (!SkuPattern().IsMatch(s.Sku ?? string.Empty))
            return Error.Validation("packages.invalidSku", "کد پکیج فقط حروف بزرگ انگلیسی، عدد و خط تیره (مثل PKG-GOLD).");
        if (s.Price < 0 || s.CompareAtPrice is < 0 || s.SpecialPrice is < 0) return Error.Validation("packages.invalidPrice", "قیمت نمی‌تواند منفی باشد.");
        if (s.SpecialPrice is { } sp && sp >= s.Price) return Error.Validation("packages.specialTooHigh", "قیمت ویژه باید کمتر از قیمت اصلی باشد.");
        if (s.SpecialFrom is { } sf && s.SpecialTo is { } st && st <= sf) return Error.Validation("packages.specialDates", "پایان تخفیف باید بعد از شروعش باشد.");
        if (s.SaleFrom is { } a && s.SaleTo is { } b && b <= a) return Error.Validation("packages.saleDates", "پایان فروش باید بعد از شروعش باشد.");
        if (s.ValidityDays is < 1 or > 3650) return Error.Validation("packages.validity", "مدت اعتبار باید بین ۱ تا ۳۶۵۰ روز باشد.");
        if (s.Capacity is < 1) return Error.Validation("packages.capacity", "ظرفیت باید حداقل ۱ باشد (خالی = نامحدود).");
        if (s.ConsultSessions < 0 || s.RevisionsPerItem < 0 || s.DeliveryHours < 0) return Error.Validation("packages.negative", "اعداد نمی‌توانند منفی باشند.");
        if (quotas.Any(q => q.Count is < 1 or > 10_000)) return Error.Validation("packages.quota", "تعداد هر سهمیه باید بین ۱ تا ۱۰۰۰۰ باشد.");
        if (quotas.GroupBy(q => q.ServiceTypeId).Any(g => g.Count() > 1)) return Error.Validation("packages.quotaDuplicate", "هر نوع خدمت فقط یک بار در سهمیه‌ها.");
        if (s.Features.Count > 30 || s.Faq.Count > 30 || s.Tags.Count > 20) return Error.Validation("packages.tooMany", "حداکثر ۳۰ ویژگی، ۳۰ پرسش و ۲۰ برچسب.");

        Name = s.Name; Slug = s.Slug!; Sku = s.Sku!; Category = Clean(s.Category);
        Tags = s.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        ShortDescription = s.ShortDescription;
        DescriptionMarkdown = Clean(s.DescriptionMarkdown);
        DescriptionHtml = descriptionHtml;
        Price = s.Price; CompareAtPrice = s.CompareAtPrice; SpecialPrice = s.SpecialPrice; SpecialFrom = s.SpecialFrom; SpecialTo = s.SpecialTo;
        Period = s.Period; ValidityDays = s.ValidityDays; SaleFrom = s.SaleFrom; SaleTo = s.SaleTo; Capacity = s.Capacity;
        PlatformIds = s.PlatformIds.Distinct().ToList();
        DefaultManagement = s.DefaultManagement; Access = s.Access;
        ConsultSessions = s.ConsultSessions; RevisionsPerItem = s.RevisionsPerItem; DeliveryHours = s.DeliveryHours;
        Features = s.Features.Where(f => !f.IsEmpty).ToList();
        Faq = s.Faq.Where(f => !string.IsNullOrWhiteSpace(f.Question)).ToList();
        Status = s.Status; ShowOnSite = s.ShowOnSite; SortOrder = s.SortOrder; Badge = s.Badge;
        SeoTitle = Clean(s.SeoTitle); SeoDescription = Clean(s.SeoDescription);

        var wanted = quotas.ToDictionary(q => q.ServiceTypeId, q => q.Count);
        _quotas.RemoveAll(q => !wanted.ContainsKey(q.ServiceTypeId));
        foreach (var (serviceTypeId, count) in wanted)
        {
            var existing = _quotas.Find(q => q.ServiceTypeId == serviceTypeId);
            if (existing is null) _quotas.Add(new PackageQuota(Id, serviceTypeId, count));
            else existing.Count = count;
        }
        return Result.Success();
    }

    /// <summary>Effective price now: the special price inside its window, else the regular price.</summary>
    public long PriceAt(DateTimeOffset now) =>
        SpecialPrice is { } sp && (SpecialFrom is null || now >= SpecialFrom) && (SpecialTo is null || now < SpecialTo) ? sp : Price;

    public bool IsOnSale(DateTimeOffset now) =>
        Status == PackageStatus.Active && (SaleFrom is null || now >= SaleFrom) && (SaleTo is null || now < SaleTo);

    public void SetCover(string? mediaId) => CoverImage = mediaId;

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
    public void Restore() => MarkRestored();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record QuotaSpec(Guid ServiceTypeId, int Count);

public sealed class PackageQuota
{
    private PackageQuota() { }

    internal PackageQuota(Guid packageId, Guid serviceTypeId, int count)
    {
        PackageId = packageId;
        ServiceTypeId = serviceTypeId;
        Count = count;
    }

    public Guid PackageId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public int Count { get; internal set; }
}
