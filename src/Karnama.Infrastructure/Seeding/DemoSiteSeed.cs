using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Site;
using Karnama.Domain.Content;
using Karnama.Domain.Settings;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Demo portfolio, photos and the home page's video loop. Every sample case is flagged «Beispiel» on the site — never shown as real results.
/// The photos and the loop are demo-only files embedded in this assembly; an empty install never gets them.
/// </summary>
internal static class DemoSiteSeed
{
    /// <summary>Same demo file → same name on every rebuild of a demo install (see IMediaStore.SaveImageAsync).</summary>
    private static string StableName(string key) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("karnama-demo:" + key)))[..32].ToLowerInvariant();

    public static async Task SeedAsync(AppDbContext db, IMediaStore media, CancellationToken ct)
    {
        async Task<string?> Image(string name, string folder)
        {
            await using var s = typeof(DemoSiteSeed).Assembly.GetManifestResourceStream("DemoMedia." + name);
            if (s is null) return null;
            var r = await media.SaveImageAsync(s, s.Length, folder, ct, StableName(folder + "/" + name));
            return r.IsSuccess ? r.Value : null;
        }

        (string Name, string Industry, long Fb, long Fa, string[] Services, string Summary, string Quote, string By, int Sort, string Photo)[] rows =
        [
            ("Café Beispiel", "Café und Restaurant", 1_200, 18_000, ["Seitenbetreuung", "Reels", "Fotografie"], "Fester Veröffentlichungsplan, wöchentliches Reel hinter den Kulissen und eine Kampagne zur Saisonkarte.", "Unsere Seite lebt endlich; jede Woche sagt ein neuer Kunde, dass er uns über Instagram gefunden hat.", "Leitung Café (Beispiel)", 1, "cafe.jpg"),
            ("Mode Beispiel", "Mode", 4_500, 31_000, ["Seitenbetreuung", "Werbung", "Produktfotografie"], "Einheitliche Produktfotos, Kollektions-Karussells und gezielte Werbung in Stuttgart.", "Unser Onlineumsatz hat den Ladenumsatz überholt.", "Leitung Shop (Beispiel)", 2, "boutique.jpg"),
            ("Kosmetikstudio Beispiel", "Beauty", 2_100, 9_800, ["Content-Produktion", "Reels"], "Kurze Lerninhalte und Antworten auf häufige Fragen in Reels.", "Die Online-Buchungen haben sich verdoppelt.", "Leitung Klinik (Beispiel)", 3, "beauty.jpg"),
            ("Eiscafé Beispiel", "Konditorei und Eis", 800, 12_400, ["Reels", "Food-Fotografie"], "Kurze Reels über die Eisherstellung und eine Sommerkampagne mit Influencern aus der Stadt.", "Diesen Sommer kam die Schlange vor dem Laden über Instagram.", "Leitung Eiscafé (Beispiel)", 4, "icecream.jpg"),
            ("Restaurant Beispiel", "Café und Restaurant", 3_000, 22_500, ["Seitenbetreuung", "Food-Fotografie", "Werbung"], "Fotos der Speisekarte, tägliche Story zum Tagesgericht und Werbung für die Wochenend-Reservierung.", "Am Wochenende sind die Reservierungen ausgebucht.", "Leitung Restaurant (Beispiel)", 5, "restaurant.jpg"),
        ];

        if (!await db.CaseStudies.IgnoreQueryFilters().AnyAsync(ct))
        {
            foreach (var r in rows)
            {
                var c = CaseStudy.Create(new CaseStudy.Spec(r.Name + " (Beispiel)", null, r.Industry, "Stuttgart", null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-8)), null,
                    r.Services, r.Summary, r.Fb, r.Fa, null, null, true, [new CaseMetric("Monatliche Reel-Aufrufe", 3_000, 120_000, null)], [], r.Quote, r.By, null,
                    "Beispieldaten zur Demonstration", false, null, r.Sort == 1, r.Sort), isSample: true).Value!;
                c.SetPublished(true);
                db.CaseStudies.Add(c);
            }
            await db.SaveChangesAsync(ct);
        }

        // Sample cases without photos (older demo databases too) get a cover.
        var bare = await db.CaseStudies.Where(x => x.IsSample).ToListAsync(ct);
        foreach (var c in bare.Where(x => x.Shots.Count == 0))
        {
            var row = rows.FirstOrDefault(r => r.Sort == c.SortOrder);
            if (row.Photo is null) continue;
            var id = await Image(row.Photo, "cases");
            if (id is null) continue;
            c.SetCover(id);
        }
        await db.SaveChangesAsync(ct);

        // Sample blog posts without a cover get a photo, so the blog cards aren't blank.
        var posts = await db.BlogPosts.Where(p => p.CoverImage == null && p.Title.Contains("(Beispiel)")).OrderBy(p => p.Id).ToListAsync(ct);
        var photos = new[] { ("creator.jpg", "Reel-Dreh mit dem Handy im Garten"), ("cafe.jpg", "Barista gießt Latte Art"), ("boutique.jpg", "Produktfotografie in der Boutique") };
        for (var i = 0; i < posts.Count; i++)
            if (await Image(photos[i % photos.Length].Item1, "blog") is { } id) posts[i].SetCover(id, photos[i % photos.Length].Item2);
        await db.SaveChangesAsync(ct);

        await SeedHeroAsync(db, media, Image, ct);
    }

    /// <summary>The first screen: a looping montage plus a fan of post photos, set only if the admin hasn't chosen any.</summary>
    private static async Task SeedHeroAsync(AppDbContext db, IMediaStore media, Func<string, string, Task<string?>> image, CancellationToken ct)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == SiteContentStore.Key, ct);
        HomeContent content;
        try { content = row is null ? HomeContent.Default : JsonSerializer.Deserialize<HomeContent>(row.Json, json) ?? HomeContent.Default; }
        catch (JsonException) { content = HomeContent.Default; }
        if (content.HeroVideo is not null || content.HeroImages is { Count: > 0 }) return;

        string? video = null;
        await using (var s = typeof(DemoSiteSeed).Assembly.GetManifestResourceStream("DemoMedia.hero.mp4"))
            if (s is not null && await media.SaveVideoAsync(s, s.Length, "site", ct, StableName("site/video")) is { IsSuccess: true } v) video = v.Value;
        var poster = await image("poster.jpg", "site");
        var photos = new List<string>();
        foreach (var name in new[] { "restaurant.jpg", "cafe.jpg", "icecream.jpg", "boutique.jpg", "beauty.jpg", "creator.jpg" })
            if (await image(name, "site") is { } id) photos.Add(id);

        content = content with { HeroVideo = video, HeroPoster = poster, HeroImages = photos };
        var text = JsonSerializer.Serialize(content, json);
        if (row is null) db.SiteSettings.Add(new SiteSetting(SiteContentStore.Key, text));
        else row.Replace(text, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
