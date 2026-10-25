using Karnama.Domain.Common;

namespace Karnama.Domain.Content;

public sealed record CaseMetric(string Label, decimal? Before, decimal? After, string? Unit);
public sealed record CaseShot(string Image, string? Caption, DateOnly? TakenOn, bool IsAfter);

/// <summary>
/// A portfolio case (docs/PROMPT2.md §12.2): real numbers only, published only with the client's written consent
/// (a file of the consent is kept). Revenue can be shown as a growth percentage only.
/// </summary>
public sealed class CaseStudy : AuditableEntity
{
    private CaseStudy() { BusinessName = string.Empty; Slug = string.Empty; }

    public string BusinessName { get; private set; }
    public string Slug { get; private set; }
    public string? Industry { get; private set; }
    public string? City { get; private set; }
    public string? Logo { get; private set; }
    public string? Handle { get; private set; }
    public DateOnly? From { get; private set; }
    public DateOnly? To { get; private set; }
    public List<string> Services { get; private set; } = [];
    public string? Summary { get; private set; }
    public long? FollowersBefore { get; private set; }
    public long? FollowersAfter { get; private set; }
    public long? RevenueBefore { get; private set; }
    public long? RevenueAfter { get; private set; }
    /// <summary>The client agreed to the growth only, not the amounts.</summary>
    public bool RevenueAsPercent { get; private set; }
    public List<CaseMetric> Metrics { get; private set; } = [];
    public List<CaseShot> Shots { get; private set; } = [];
    public string? Quote { get; private set; }
    public string? QuoteBy { get; private set; }
    public string? VideoUrl { get; private set; }
    /// <summary>"Per the client's report" or "screenshot of date X" under every number.</summary>
    public string? Source { get; private set; }
    public bool ConsentObtained { get; private set; }
    public string? ConsentFile { get; private set; }
    public bool IsSample { get; private set; }
    public bool Published { get; private set; }
    public bool Featured { get; private set; }
    public int SortOrder { get; private set; }
    public uint Version { get; private set; }

    public sealed record Spec(string BusinessName, string? Slug, string? Industry, string? City, string? Logo, string? Handle, DateOnly? From, DateOnly? To,
        IReadOnlyList<string> Services, string? Summary, long? FollowersBefore, long? FollowersAfter, long? RevenueBefore, long? RevenueAfter, bool RevenueAsPercent,
        IReadOnlyList<CaseMetric> Metrics, IReadOnlyList<CaseShot> Shots, string? Quote, string? QuoteBy, string? VideoUrl, string? Source,
        bool ConsentObtained, string? ConsentFile, bool Featured, int SortOrder);

    public static Result<CaseStudy> Create(Spec s, bool isSample = false)
    {
        var c = new CaseStudy { IsSample = isSample };
        var r = c.Update(s);
        return r.IsSuccess ? c : r.Error!;
    }

    private static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];

    public Result Update(Spec s)
    {
        var name = s.BusinessName?.Trim() ?? "";
        if (name.Length is < 2 or > 80) return Error.Validation("cases.name", "نام کسب‌وکار بین ۲ تا ۸۰ کاراکتر.");
        if (s.From is { } f && s.To is { } t && t < f) return Error.Validation("cases.period", "پایان همکاری قبل از شروع است.");
        if (new[] { s.FollowersBefore, s.FollowersAfter, s.RevenueBefore, s.RevenueAfter }.Any(v => v is < 0)) return Error.Validation("cases.negative", "عدد منفی قبول نیست.");
        if (s.VideoUrl is { Length: > 0 } v && !(Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == "https")) return Error.Validation("cases.video", "لینک ویدیو باید با https:// شروع شود.");
        var slug = Common.Slug.From(string.IsNullOrWhiteSpace(s.Slug) ? name : s.Slug, 60);
        if (slug.Length == 0) return Error.Validation("cases.slug", "نامک نامعتبر است.");
        BusinessName = name; Slug = slug; Industry = Clean(s.Industry, 60); City = Clean(s.City, 40); Logo = Clean(s.Logo, 100); Handle = Clean(s.Handle?.TrimStart('@'), 60);
        From = s.From; To = s.To; Services = s.Services.Select(x => x.Trim()).Where(x => x.Length is > 0 and <= 60).Distinct().Take(10).ToList();
        Summary = Clean(s.Summary, 600); FollowersBefore = s.FollowersBefore; FollowersAfter = s.FollowersAfter; RevenueBefore = s.RevenueBefore; RevenueAfter = s.RevenueAfter;
        RevenueAsPercent = s.RevenueAsPercent;
        Metrics = s.Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Label)).Take(8).Select(m => m with { Label = m.Label.Trim()[..Math.Min(60, m.Label.Trim().Length)] }).ToList();
        Shots = s.Shots.Take(12).ToList();
        Quote = Clean(s.Quote, 600); QuoteBy = Clean(s.QuoteBy, 80); VideoUrl = Clean(s.VideoUrl, 300); Source = Clean(s.Source, 120);
        ConsentObtained = s.ConsentObtained; ConsentFile = Clean(s.ConsentFile, 100);
        Featured = s.Featured; SortOrder = s.SortOrder;
        if (Published && !CanPublish) Published = false;   // consent withdrawn → off the site
        return Result.Success();
    }

    /// <summary>Written consent (and its file) is required — sample cases in the demo are marked as samples instead.</summary>
    public bool CanPublish => IsSample || (ConsentObtained && ConsentFile is not null);

    public Result SetPublished(bool on)
    {
        if (on && !CanPublish) return Error.Validation("cases.consent", "بدون رضایت کتبی مشتری (و فایل آن) نمونه‌کار منتشر نمی‌شود.");
        Published = on;
        return Result.Success();
    }

    public void Delete(Guid? by, DateTimeOffset now) { Published = false; MarkDeleted(by, now); }

    /// <summary>Follower growth in percent (for the big number on the card).</summary>
    public int? FollowerGrowth => FollowersBefore is > 0 && FollowersAfter is { } a ? (int)Math.Round((a - FollowersBefore.Value) * 100.0 / FollowersBefore.Value) : null;
    public int? RevenueGrowth => RevenueBefore is > 0 && RevenueAfter is { } a ? (int)Math.Round((a - RevenueBefore.Value) * 100.0 / RevenueBefore.Value) : null;
}
