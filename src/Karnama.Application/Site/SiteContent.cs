using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Site;

public sealed record StepItem(string Title, string Body);
public sealed record FaqEntry(string Question, string Answer);
public sealed record ContactInfo(string? Phone, string? Address, string? City, string? Hours, string? Email, string? Instagram, string? Telegram, string? WhatsApp,
    string? NeshanUrl, string? BaladUrl, double? Latitude, double? Longitude);

/// <summary>Everything on the home page that isn't data (docs/PROMPT2.md §12.4). Edited in the panel; SEO fields are required.</summary>
public sealed record HomeContent(
    string SeoTitle, string SeoDescription,
    string HeroTitle, string HeroSubtitle, string CtaLabel,
    string ServicesTitle, string CasesTitle, string PackagesTitle, string ProcessTitle, IReadOnlyList<StepItem> Process,
    string TestimonialsTitle, string BlogTitle, string FaqTitle, IReadOnlyList<FaqEntry> Faq,
    string ConsultTitle, string ConsultText, ContactInfo Contact, bool ShowPackages, bool ShowCases, bool ShowBlog,
    string? HeroVideo = null, string? HeroPoster = null, IReadOnlyList<string>? HeroImages = null)
{
    /// <summary>Uploaded media ids this content may point at (never a free URL: only files we stored and checked).</summary>
    public static readonly System.Text.RegularExpressions.Regex VideoId = new("^site/[0-9a-f]{32}\\.(mp4|webm)$");
    public static readonly System.Text.RegularExpressions.Regex ImageId = new("^(site|cases)/[0-9a-f]{32}\\.(png|jpg|webp)$");

    public static HomeContent Default { get; } = new(
        "Instagram-Betreuung und Content-Produktion in Stuttgart",
        "Instagram-Betreuung, Content-Produktion, Reels und Werbung für Unternehmen in Stuttgart – mit Plan, Monatsbericht und eigenem Panel.",
        "Ihre Seite – jeden Tag pünktlich und in Top-Qualität",
        "Von der Idee und dem Fotoshooting bis zu Reels, Captions und Veröffentlichung; wir erledigen alles für Sie, und Sie sehen den Fortschritt in Ihrem eigenen Panel.",
        "Kostenlose Beratung",
        "Leistungen", "Referenzen", "Pakete", "Ablauf der Zusammenarbeit",
        [
            new("Erster Chat", "Wir hören uns Ziel, Zielgruppe und aktuellen Stand Ihrer Seite an."),
            new("Content-Plan", "Ein monatlicher Veröffentlichungskalender mit Ideen und Terminen wird erstellt."),
            new("Produktion und Freigabe", "Inhalte werden erstellt, und Sie geben sie im Panel frei oder fordern eine Überarbeitung an."),
            new("Veröffentlichung und Bericht", "Wir veröffentlichen pünktlich und berichten am Monatsende."),
        ],
        "Kundenstimmen", "Neueste Beiträge", "Häufige Fragen",
        [
            new("Wie viele Monate läuft der Vertrag mindestens?", "Die Pakete sind monatlich und lassen sich jeden Monat verlängern."),
            new("Bleibt die Seite bei uns oder bei Ihnen?", "Beides ist möglich; bleibt die Seite in Ihrer Hand, schicken wir Ihnen Datei, Caption und Veröffentlichungszeit."),
            new("Wie sehen wir den Arbeitsfortschritt?", "Jeder Kunde hat sein eigenes Panel: Aufträge, Inhalte zur Freigabe und Restkontingent des Pakets sind immer im Blick."),
        ],
        "Kostenlose Beratung", "Hinterlassen Sie Ihre Nummer; wir rufen während der Geschäftszeiten an.",
        new ContactInfo(null, null, "Stuttgart", "Montag bis Freitag 9 bis 17 Uhr", null, null, null, null, null, null, null, null),
        true, true, true);
}

public sealed class HomeContentValidator : AbstractValidator<HomeContent>
{
    public HomeContentValidator()
    {
        RuleFor(x => x.SeoTitle).NotEmpty().MaximumLength(60).WithMessage("Der SEO-Titel ist Pflicht und darf höchstens 60 Zeichen haben.");
        RuleFor(x => x.SeoDescription).NotEmpty().Length(50, 160).WithMessage("Die SEO-Beschreibung ist Pflicht, zwischen 50 und 160 Zeichen.");
        RuleFor(x => x.HeroTitle).NotEmpty().MaximumLength(90).WithMessage("Die Hauptüberschrift ist Pflicht (höchstens 90 Zeichen).");
        RuleFor(x => x.HeroSubtitle).MaximumLength(300);
        RuleFor(x => x.CtaLabel).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Process).Must(p => p is null || p.Count <= 8).WithMessage("Höchstens 8 Schritte.");
        RuleFor(x => x.Faq).Must(p => p is null || p.Count <= 20).WithMessage("Höchstens 20 Fragen.");
        RuleForEach(x => x.Faq).Must(f => !string.IsNullOrWhiteSpace(f.Question) && f.Question.Length <= 200 && !string.IsNullOrWhiteSpace(f.Answer) && f.Answer.Length <= 1000)
            .WithMessage("Jede Frage und Antwort muss ausgefüllt sein (Frage bis 200, Antwort bis 1000 Zeichen).");
        RuleFor(x => x.Contact.NeshanUrl).Must(Https).WithMessage("Der Google-Maps-Link muss mit https:// beginnen.");
        RuleFor(x => x.Contact.BaladUrl).Must(Https).WithMessage("Der Apple-Maps-Link muss mit https:// beginnen.");
        RuleFor(x => x.HeroVideo).Must(v => v is null || HomeContent.VideoId.IsMatch(v)).WithMessage("Das Header-Video muss über dieses Panel hochgeladen sein.");
        RuleFor(x => x.HeroPoster).Must(v => v is null || HomeContent.ImageId.IsMatch(v)).WithMessage("Das Vorschaubild des Videos muss über dieses Panel hochgeladen sein.");
        RuleFor(x => x.HeroImages).Must(v => v is null || (v.Count <= 6 && v.All(HomeContent.ImageId.IsMatch)))
            .WithMessage("Höchstens 6 Header-Fotos, alle aus diesem Panel hochgeladen.");
        RuleFor(x => x.Contact.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Contact.Longitude).InclusiveBetween(-180, 180);
    }

    private static bool Https(string? u) => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == "https");
}

public sealed class SiteContentStore(IAppDbContext db, ICurrentUser me, TimeProvider clock, IAuditWriter audit, IPublicCache cache, IMediaStore media)
{
    public const string Key = "site.home";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HomeContent> GetAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null) return HomeContent.Default;
        try { return JsonSerializer.Deserialize<HomeContent>(row.Json, Json) ?? HomeContent.Default; }
        catch (JsonException) { return HomeContent.Default; }
    }

    public async Task<Domain.Common.Result<HomeContent>> SaveAsync(HomeContent c, CancellationToken ct)
    {
        if (!me.Has(Domain.Identity.Permissions.ContentManage, Domain.Identity.PermissionScope.All)) return Domain.Common.Error.Forbidden();
        c = c with { Process = c.Process ?? [], Faq = c.Faq ?? [], HeroImages = c.HeroImages ?? [] };
        var json = JsonSerializer.Serialize(c, Json);
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (row is null) db.SiteSettings.Add(new SiteSetting(Key, json));
        else row.Replace(json, me.UserId, clock.GetUtcNow());
        audit.Add("site.homeUpdated", "SiteSetting", Key);
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return c;
    }

    /// <summary>Hero media upload: an image (≤2MB) or a short MP4/WebM loop (≤18MB, under the proxy's 20MB cap) for the home page's first screen.</summary>
    public async Task<Domain.Common.Result<Blog.MediaUploaded>> UploadAsync(Stream s, long length, bool video, CancellationToken ct)
    {
        if (!me.Has(Domain.Identity.Permissions.ContentManage, Domain.Identity.PermissionScope.All)) return Domain.Common.Error.Forbidden();
        var saved = video ? await media.SaveVideoAsync(s, length, "site", ct) : await media.SaveImageAsync(s, length, "site", ct);
        if (saved.IsFailure) return saved.Error!;
        audit.Add(video ? "site.videoUploaded" : "site.imageUploaded", "SiteSetting", saved.Value!);
        await db.SaveChangesAsync(ct);
        return new Blog.MediaUploaded(saved.Value!, media.PublicUrl(saved.Value!));
    }
}
