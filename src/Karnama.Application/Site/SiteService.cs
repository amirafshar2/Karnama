using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Content;
using Karnama.Domain.Crm;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Site;

// ---- Public DTOs ----
public sealed record SiteStats(int Clients, int Deliveries, int? MedianFollowerGrowth);
public sealed record SiteService(string Name, string? Description, string? Icon);
public sealed record SitePackageCard(string Name, string Slug, string? Short, long Price, long? CompareAt, long? Special, PackagePeriod Period, int ValidityDays, PackageBadge Badge,
    string? CoverUrl, IReadOnlyList<string> Highlights);
public sealed record SitePackage(string Name, string Slug, string? Short, string? DescriptionHtml, long Price, long? CompareAt, long? Special, PackagePeriod Period, int ValidityDays,
    PackageBadge Badge, string? CoverUrl, IReadOnlyList<string> Quotas, IReadOnlyList<string> Features, IReadOnlyList<PackageFaq> Faq, PageManagement Management,
    int ConsultSessions, int RevisionsPerItem, int DeliveryHours, string? SeoTitle, string? SeoDescription);
public sealed record SiteCaseCard(string BusinessName, string Slug, string? Industry, string? City, string? LogoUrl, long? FollowersBefore, long? FollowersAfter,
    int? FollowerGrowth, int? RevenueGrowth, IReadOnlyList<string> Services, string? Summary, string? Source, bool IsSample, string? CoverUrl);
public sealed record SiteCase(SiteCaseCard Card, string? Handle, DateOnly? From, DateOnly? To, long? RevenueBefore, long? RevenueAfter, bool RevenueAsPercent,
    IReadOnlyList<CaseMetric> Metrics, IReadOnlyList<SiteShot> Shots, string? Quote, string? QuoteBy, string? VideoUrl);
public sealed record SiteShot(string Url, string? Caption, DateOnly? TakenOn, bool IsAfter);
public sealed record SiteTestimonial(string Quote, string By, string BusinessName, string Slug);
public sealed record SiteHome(HomeContent Content, SiteStats Stats, IReadOnlyList<SiteService> Services, IReadOnlyList<SiteCaseCard> Cases,
    IReadOnlyList<SitePackageCard> Packages, IReadOnlyList<SiteTestimonial> Testimonials);
public sealed record SiteSitemapEntry(string Path, DateTimeOffset Modified);

// ---- Admin DTOs ----
public sealed record CaseDto(Guid Id, string BusinessName, string Slug, string? Industry, string? City, string? Logo, string? LogoUrl, string? Handle, DateOnly? From, DateOnly? To,
    IReadOnlyList<string> Services, string? Summary, long? FollowersBefore, long? FollowersAfter, long? RevenueBefore, long? RevenueAfter, bool RevenueAsPercent,
    IReadOnlyList<CaseMetric> Metrics, IReadOnlyList<CaseShotDto> Shots, string? Quote, string? QuoteBy, string? VideoUrl, string? Source,
    bool ConsentObtained, string? ConsentFile, bool IsSample, bool Published, bool Featured, int SortOrder, uint Version);
public sealed record CaseShotDto(string Image, string Url, string? Caption, DateOnly? TakenOn, bool IsAfter);
public sealed record SaveCaseRequest(string BusinessName, string? Slug, string? Industry, string? City, string? Logo, string? Handle, DateOnly? From, DateOnly? To,
    List<string>? Services, string? Summary, long? FollowersBefore, long? FollowersAfter, long? RevenueBefore, long? RevenueAfter, bool RevenueAsPercent,
    List<CaseMetric>? Metrics, List<CaseShot>? Shots, string? Quote, string? QuoteBy, string? VideoUrl, string? Source,
    bool ConsentObtained, string? ConsentFile, bool Featured, int SortOrder, uint? Version);
public sealed record PublishCaseRequest(bool Published);

public sealed record LeadRequest(string Name, string Phone, string? Business, string? Message, string? Source, string? Website);
public sealed record LeadDto(Guid Id, string Name, string Phone, string? Business, string? Message, string? Source, LeadStatus Status, string? Note, DateTimeOffset CreatedAt, uint Version);
public sealed record UpdateLeadRequest(LeadStatus Status, string? Note);

public sealed class LeadRequestValidator : AbstractValidator<LeadRequest>
{
    public LeadRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).WithMessage("نام را بنویسید.");
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20).WithMessage("شماره‌ی موبایل را بنویسید.");
        RuleFor(x => x.Business).MaximumLength(80);
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.Source).MaximumLength(200);
    }
}

/// <summary>The public website's data (N10): home page, packages, portfolio, consultation requests. Read-only for visitors.</summary>
public sealed class PublicSiteService(IAppDbContext db, IMediaStore media, SiteContentStore content, TimeProvider clock, Notify.Notifier notifier, ICurrentUser me)
{
    private string? Url(string? id) => id is null ? null : media.PublicUrl(id);

    public async Task<SiteHome> HomeAsync(CancellationToken ct)
    {
        var c = await content.GetAsync(ct);
        var clients = await db.Clients.CountAsync(x => x.Status == ClientStatus.Active, ct);
        var deliveries = await db.Orders.CountAsync(o => o.Status == Domain.Work.OrderStatus.Completed, ct);
        var cases = await LiveCases().OrderByDescending(x => x.Featured).ThenBy(x => x.SortOrder).ThenByDescending(x => x.Id).Take(12).ToListAsync(ct);
        // Real numbers only: the median growth of published cases, and only when there are cases that measured it.
        var growths = cases.Where(x => !x.IsSample).Select(x => x.FollowerGrowth).OfType<int>().OrderBy(x => x).ToList();
        int? median = growths.Count == 0 ? null : growths[growths.Count / 2];
        var services = await db.ServiceTypes.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).Take(12).ToListAsync(ct);
        var packages = c.ShowPackages ? await PackageCardsAsync(ct) : [];
        var testimonials = cases.Where(x => x.Quote != null && x.QuoteBy != null).Take(6)
            .Select(x => new SiteTestimonial(x.Quote!, x.QuoteBy!, x.BusinessName, x.Slug)).ToList();
        return new SiteHome(c, new SiteStats(clients, deliveries, median),
            services.Select(s => new SiteService(s.Name.Get("fa"), s.Description, s.Icon)).ToList(),
            c.ShowCases ? cases.Take(3).Select(Card).ToList() : [], packages, testimonials);
    }

    private IQueryable<CaseStudy> LiveCases() => db.CaseStudies.AsNoTracking().Where(x => x.Published);

    private SiteCaseCard Card(CaseStudy x) => new(x.BusinessName, x.Slug, x.Industry, x.City, Url(x.Logo), x.FollowersBefore, x.FollowersAfter, x.FollowerGrowth,
        x.RevenueGrowth, x.Services, x.Summary, x.Source, x.IsSample, Url(x.Shots.FirstOrDefault(s => s.IsAfter)?.Image ?? x.Shots.FirstOrDefault()?.Image));

    private async Task<List<SitePackageCard>> PackageCardsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Packages.AsNoTracking().Include(p => p.Quotas)
            .Where(p => p.ShowOnSite && p.Status == PackageStatus.Active && (p.SaleFrom == null || p.SaleFrom <= now) && (p.SaleTo == null || p.SaleTo > now))
            .OrderBy(p => p.SortOrder).Take(12).ToListAsync(ct);
        var names = await ServiceNamesAsync(ct);
        return rows.Select(p => new SitePackageCard(p.Name.Get("fa"), p.Slug, p.ShortDescription.Get("fa"), p.Price, p.CompareAtPrice, ActiveSpecial(p, now), p.Period, p.ValidityDays,
            p.Badge, Url(p.CoverImage), p.Quotas.Select(q => $"{q.Count} {names.GetValueOrDefault(q.ServiceTypeId)}").Take(5).ToList())).ToList();
    }

    private static long? ActiveSpecial(Package p, DateTimeOffset now) =>
        p.SpecialPrice is { } s && (p.SpecialFrom == null || p.SpecialFrom <= now) && (p.SpecialTo == null || p.SpecialTo > now) ? s : null;

    private async Task<Dictionary<Guid, string>> ServiceNamesAsync(CancellationToken ct) =>
        (await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get("fa"));

    public async Task<IReadOnlyList<SitePackageCard>> PackagesAsync(CancellationToken ct) => await PackageCardsAsync(ct);

    public async Task<SitePackage?> PackageAsync(string slug, CancellationToken ct)
    {
        slug = Slug.From(slug, 200);
        var now = clock.GetUtcNow();
        var p = await db.Packages.AsNoTracking().Include(x => x.Quotas).FirstOrDefaultAsync(x => x.Slug == slug && x.ShowOnSite && x.Status == PackageStatus.Active, ct);
        if (p is null) return null;
        var names = await ServiceNamesAsync(ct);
        return new SitePackage(p.Name.Get("fa"), p.Slug, p.ShortDescription.Get("fa"), p.DescriptionHtml, p.Price, p.CompareAtPrice, ActiveSpecial(p, now), p.Period, p.ValidityDays,
            p.Badge, Url(p.CoverImage), p.Quotas.Select(q => $"{q.Count} {names.GetValueOrDefault(q.ServiceTypeId)}").ToList(), p.Features.Select(f => f.Get("fa")).ToList(),
            p.Faq, p.DefaultManagement, p.ConsultSessions, p.RevisionsPerItem, p.DeliveryHours, p.SeoTitle, p.SeoDescription);
    }

    public async Task<IReadOnlyList<SiteCaseCard>> CasesAsync(CancellationToken ct) =>
        (await LiveCases().OrderByDescending(x => x.Featured).ThenBy(x => x.SortOrder).ThenByDescending(x => x.Id).Take(60).ToListAsync(ct)).Select(Card).ToList();

    public async Task<SiteCase?> CaseAsync(string slug, CancellationToken ct)
    {
        slug = Slug.From(slug, 200);
        var x = await LiveCases().FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (x is null) return null;
        return new SiteCase(Card(x), x.Handle, x.From, x.To, x.RevenueAsPercent ? null : x.RevenueBefore, x.RevenueAsPercent ? null : x.RevenueAfter, x.RevenueAsPercent,
            x.Metrics, x.Shots.Select(s => new SiteShot(media.PublicUrl(s.Image), s.Caption, s.TakenOn, s.IsAfter)).ToList(), x.Quote, x.QuoteBy, x.VideoUrl);
    }

    public async Task<IReadOnlyList<SiteSitemapEntry>> SitemapAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var list = new List<SiteSitemapEntry>();
        foreach (var p in await PackageCardsAsync(ct)) list.Add(new SiteSitemapEntry("/plans/" + p.Slug, now));
        var cases = await LiveCases().Select(c => new { c.Slug, c.UpdatedAt, c.CreatedAt }).ToListAsync(ct);
        foreach (var c in cases) list.Add(new SiteSitemapEntry("/cases/" + c.Slug, c.UpdatedAt ?? c.CreatedAt));
        if (cases.Count > 0) list.Add(new SiteSitemapEntry("/cases", cases.Max(c => c.UpdatedAt ?? c.CreatedAt)));
        return list;
    }

    /// <summary>Consultation form → a lead + an important notification to whoever manages leads.</summary>
    public async Task<Result> LeadAsync(LeadRequest r, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(r.Website)) return Result.Success();   // honeypot: bots fill every field; pretend it worked
        var created = Lead.Create(r.Name, r.Phone, r.Business, r.Message, r.Source, clock.GetUtcNow());
        if (created.IsFailure) return created.Error!;
        var lead = created.Value!;
        // The same phone within an hour is one request (double taps, impatient visitors).
        if (await db.Leads.AnyAsync(l => l.Phone == lead.Phone && l.CreatedAt > lead.CreatedAt.AddHours(-1), ct)) return Result.Success();
        db.Leads.Add(lead);
        var who = await notifier.StaffWithAsync(Permissions.LeadsManage, PermissionScope.All, ct);
        await notifier.NotifyAsync(who, "lead.new", $"درخواست مشاوره: {lead.Name}", lead.Business ?? lead.Message, "/leads", true, $"lead:{lead.Id:N}", ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- Admin: leads ----
    private bool CanLeads => me.Kind == UserKind.Staff && me.Has(Permissions.LeadsManage, PermissionScope.All);

    public async Task<Result<KeysetPage<LeadDto>>> LeadsAsync(KeysetQuery q, LeadStatus? status, CancellationToken ct)
    {
        if (!CanLeads) return Error.Forbidden();
        var query = db.Leads.AsNoTracking();
        if (status is { } s) query = query.Where(l => l.Status == s);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(l => EF.Functions.ILike(l.Name, "%" + q.Search.Trim() + "%") || l.Phone.Contains(q.Search.Trim()));
        return await Keyset.PageAsync(query, q, x => x.Select(l => new LeadDto(l.Id, l.Name, l.Phone, l.Business, l.Message, l.Source, l.Status, l.Note, l.CreatedAt, l.Version)), x => x.Id, ct);
    }

    public async Task<Result<LeadDto>> UpdateLeadAsync(Guid id, UpdateLeadRequest r, CancellationToken ct)
    {
        if (!CanLeads) return Error.Forbidden();
        var l = await db.Leads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return Error.NotFound();
        l.Update(r.Status, r.Note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new LeadDto(l.Id, l.Name, l.Phone, l.Business, l.Message, l.Source, l.Status, l.Note, l.CreatedAt, l.Version);
    }
}

/// <summary>Portfolio admin (content.manage): real numbers, screenshots, and the client's written consent.</summary>
public sealed class CaseStudyService(IAppDbContext db, ICurrentUser me, IMediaStore media, IAuditWriter audit, TimeProvider clock, IPublicCache cache)
{
    private bool Can => me.Kind == UserKind.Staff && me.Has(Permissions.ContentManage, PermissionScope.All);

    public async Task<Result<IReadOnlyList<CaseDto>>> ListAsync(CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        var rows = await db.CaseStudies.AsNoTracking().OrderBy(c => c.SortOrder).ThenByDescending(c => c.Id).ToListAsync(ct);
        return rows.Select(Dto).ToList();
    }

    public async Task<Result<CaseDto>> GetAsync(Guid id, CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        var c = await db.CaseStudies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? Error.NotFound() : Dto(c);
    }

    public async Task<Result<CaseDto>> SaveAsync(Guid? id, SaveCaseRequest r, CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        foreach (var img in new[] { r.Logo }.Concat((r.Shots ?? []).Select(s => s.Image)).Where(x => x is not null))
        {
            var opened = media.OpenPublic(img!);
            if (opened is null) return Error.Validation("cases.image", "یکی از عکس‌ها پیدا نشد؛ دوباره آپلود کنید.");
            opened.Value.Stream.Dispose();
        }
        if (r.ConsentFile is { } cf)
        {
            var opened = media.OpenPrivate(cf);
            if (opened is null) return Error.Validation("cases.consentFile", "فایل رضایت پیدا نشد؛ دوباره آپلود کنید.");
            opened.Value.Stream.Dispose();
        }
        var spec = new CaseStudy.Spec(r.BusinessName, r.Slug, r.Industry, r.City, r.Logo, r.Handle, r.From, r.To, r.Services ?? [], r.Summary,
            r.FollowersBefore, r.FollowersAfter, r.RevenueBefore, r.RevenueAfter, r.RevenueAsPercent, r.Metrics ?? [], r.Shots ?? [], r.Quote, r.QuoteBy, r.VideoUrl, r.Source,
            r.ConsentObtained, r.ConsentFile, r.Featured, r.SortOrder);
        CaseStudy c;
        if (id is { } cid)
        {
            c = (await db.CaseStudies.FirstOrDefaultAsync(x => x.Id == cid, ct))!;
            if (c is null) return Error.NotFound();
            if (r.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
            db.SetOriginalVersion(c, v);
            var u = c.Update(spec);
            if (u.IsFailure) return u.Error!;
        }
        else
        {
            var created = CaseStudy.Create(spec);
            if (created.IsFailure) return created.Error!;
            c = created.Value!;
            db.CaseStudies.Add(c);
        }
        if (await db.CaseStudies.AnyAsync(x => x.Slug == c.Slug && x.Id != c.Id, ct)) return Error.Conflict("cases.slugTaken", "نمونه‌کاری با همین نامک هست؛ نامک دیگری بنویسید.");
        audit.Add(id is null ? "cases.created" : "cases.updated", nameof(CaseStudy), c.Id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return Dto(c);
    }

    public async Task<Result<CaseDto>> PublishAsync(Guid id, PublishCaseRequest r, CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        var c = await db.CaseStudies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound();
        var res = c.SetPublished(r.Published);
        if (res.IsFailure) return res.Error!;
        audit.Add(r.Published ? "cases.published" : "cases.unpublished", nameof(CaseStudy), c.Id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return Dto(c);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        var c = await db.CaseStudies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound();
        c.Delete(me.UserId, clock.GetUtcNow());
        audit.Add("cases.deleted", nameof(CaseStudy), c.Id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Blog.MediaUploaded>> UploadAsync(Stream s, long length, bool consent, CancellationToken ct)
    {
        if (!Can) return Error.Forbidden();
        var saved = await media.SaveImageAsync(s, length, consent ? "consent" : "cases", ct);
        if (saved.IsFailure) return saved.Error!;
        return new Blog.MediaUploaded(saved.Value!, consent ? $"/api/v1/cases/consent/{saved.Value!.Split('/')[1]}" : media.PublicUrl(saved.Value!));
    }

    public Result<(Stream Stream, string ContentType)> OpenConsent(string file)
    {
        if (!Can) return Error.Forbidden();
        var opened = media.OpenPrivate("consent/" + file);
        return opened is null ? Error.NotFound() : opened.Value;
    }

    private CaseDto Dto(CaseStudy c) => new(c.Id, c.BusinessName, c.Slug, c.Industry, c.City, c.Logo, c.Logo is null ? null : media.PublicUrl(c.Logo), c.Handle, c.From, c.To,
        c.Services, c.Summary, c.FollowersBefore, c.FollowersAfter, c.RevenueBefore, c.RevenueAfter, c.RevenueAsPercent, c.Metrics,
        c.Shots.Select(s => new CaseShotDto(s.Image, media.PublicUrl(s.Image), s.Caption, s.TakenOn, s.IsAfter)).ToList(), c.Quote, c.QuoteBy, c.VideoUrl, c.Source,
        c.ConsentObtained, c.ConsentFile, c.IsSample, c.Published, c.Featured, c.SortOrder, c.Version);
}
