using System.Text.Json.Nodes;
using Karnama.Application.Blog;
using Karnama.Domain.Content;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Sample posts (marked «Beispiel») so the blog, the editor and the SEO checklist aren't empty in the demo.</summary>
internal static class DemoBlogSeed
{
    private static JsonObject Text(string t, params string[] marks)
    {
        var o = new JsonObject { ["type"] = "text", ["text"] = t };
        if (marks.Length > 0) o["marks"] = new JsonArray(marks.Select(m => (JsonNode)new JsonObject { ["type"] = m }).ToArray());
        return o;
    }
    private static JsonObject Link(string t, string href) =>
        new() { ["type"] = "text", ["text"] = t, ["marks"] = new JsonArray(new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } }) };
    private static JsonObject P(params JsonObject[] inline) => new() { ["type"] = "paragraph", ["content"] = new JsonArray(inline.Select(i => (JsonNode)i).ToArray()) };
    private static JsonObject P(string t) => P(Text(t));
    private static JsonObject H(int level, string t) => new() { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = level }, ["content"] = new JsonArray(Text(t)) };
    private static JsonObject Ul(params string[] items) => new()
    {
        ["type"] = "bulletList",
        ["content"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["type"] = "listItem", ["content"] = new JsonArray(P(i)) }).ToArray()),
    };
    private static JsonObject Tip(string t) => new() { ["type"] = "callout", ["attrs"] = new JsonObject { ["tone"] = "tip" }, ["content"] = new JsonArray(P(t)) };
    private static string Doc(params JsonObject[] blocks) => new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(blocks.Select(b => (JsonNode)b).ToArray()) }.ToJsonString();

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.BlogPosts.IgnoreQueryFilters().AnyAsync(ct)) return;
        var admin = await db.Users.Where(u => u.IsDemo && u.DisplayName.StartsWith("Sarah")).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var writer = await db.Users.Where(u => u.IsDemo && u.DisplayName.StartsWith("Marie")).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (admin == Guid.Empty) return;
        if (writer == Guid.Empty) writer = admin;

        var insta = BlogCategory.Create("Instagram", "Seitenbetreuung, Reels und Storys", 1).Value!;
        var content = BlogCategory.Create("Content-Produktion", "Fotografie, Caption und Content-Ideen", 2).Value!;
        db.BlogCategories.AddRange(insta, content);

        var now = DateTimeOffset.UtcNow;
        var reelsSlug = SeoAutomation.SlugFor("Reels oder Posts? Was ist besser für den Umsatz eines Unternehmens (Beispiel)");
        var posts = new (string Title, Guid Author, Guid Cat, string[] Tags, string Json, PostStatus Status, int DaysAgo)[]
        {
            ("10 Tipps für die Instagram-Betreuung von Unternehmen in Stuttgart (Beispiel)", admin, insta.Id, ["Instagram", "Stuttgart"], Doc(
                P("Instagram-Betreuung für ein Café oder einen Laden in Stuttgart heißt nicht nur ein paar Posts pro Woche; Plan, einheitliche Tonalität und schnelle Antworten auf Nachrichten binden die Kunden. Dies ist ein Beispielbeitrag, geschrieben zur Demonstration des Editors und der automatischen SEO."),
                H(2, "Haben Sie einen festen Veröffentlichungsplan"),
                P("Wenn Ihr Publikum weiß, an welchen Tagen neue Inhalte kommen, kommt es öfter wieder. Ein einfacher Veröffentlichungskalender mit drei Posts und fünf Storys pro Woche reicht für den Anfang."),
                Ul("Feste Tage für Reels", "Tägliche Story hinter den Kulissen", "Ein Lern-Post pro Woche"),
                Tip("Nutzen Sie in Karnama den „Veröffentlichungskalender“ für Planung und Erinnerungen."),
                H(2, "Lokale, kundennahe Inhalte"),
                P(Text("Echte Fotos vom Laden und vertraute Gesichter der Mitarbeiter schaffen mehr Vertrauen als Stockfotos. Für Videos "), Link("Reels und Posts im Vergleich", "/blog/" + reelsSlug), Text(" ebenfalls lesen.")),
                H(2, "Häufige Fragen"),
                H(3, "Wie oft sollten wir posten?"),
                P("Für die meisten kleinen Unternehmen sind drei Posts pro Woche und eine tägliche Story ein guter Start."),
                H(3, "Wie schnell sollten wir auf Nachrichten antworten?"),
                P("Während der Arbeitszeit möglichst in unter einer Stunde; die Antwortgeschwindigkeit wirkt sich direkt auf den Umsatz aus.")), PostStatus.Published, 6),
            ("Reels oder Posts? Was ist besser für den Umsatz eines Unternehmens (Beispiel)", admin, insta.Id, ["Reels", "Instagram"], Doc(
                P("Reels sind ideal, um von neuen Leuten gesehen zu werden, Posts für die genaue Vorstellung eines Produkts. Die richtige Mischung hängt von Ihrem Ziel ab. Dies ist ein Beispielbeitrag."),
                H(2, "Reels für Reichweite"),
                P("Kurze Reels unter dreißig Sekunden mit einem starken Hook in den ersten zwei Sekunden holen die meisten Aufrufe von Leuten, die Ihnen noch nicht folgen."),
                H(2, "Posts und Karussells für den Verkauf"),
                P("Ein Karussell mit mehreren Produktfotos, Preis und Bestellweg macht die Kaufentscheidung leichter."),
                Ul("Zwei Reels pro Woche für Reichweite", "Ein Produkt-Karussell", "Story mit Umfrage für mehr Interaktion")), PostStatus.Published, 3),
            ("Captions schreiben: 5 einfache Vorlagen, die funktionieren (Beispiel)", writer, content.Id, ["Caption", "Content-Produktion"], Doc(
                P("Eine gute Caption beginnt kurz, nennt ein Problem und sagt am Ende, was das Publikum tun soll. Dieser Beispielentwurf wartet auf Prüfung."),
                H(2, "Vorlage Problem und Lösung"),
                P("Ein Satz zum Problem des Kunden, ein Satz zu Ihrer Lösung und eine Handlungsaufforderung.")), PostStatus.InReview, 1),
        };

        foreach (var x in posts)
        {
            var p = BlogPost.Create(x.Author, x.Title, now.AddDays(-x.DaysAgo)).Value!;
            p.Edit(new BlogPost.EditSpec(x.Title, x.Json, x.Cat, x.Tags, null, null, null, null, null, null), now.AddDays(-x.DaysAgo));
            var seo = SeoAutomation.Run(x.Title, x.Json, null);
            p.ApplySeo(seo, seo.Slug);
            if (x.Status == PostStatus.Published) p.Publish(now.AddDays(-x.DaysAgo), null);
            else if (x.Status == PostStatus.InReview) p.Submit();
            db.BlogPosts.Add(p);
        }
        await db.SaveChangesAsync(ct);
    }
}
