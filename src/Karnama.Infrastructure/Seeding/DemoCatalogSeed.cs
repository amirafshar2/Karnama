using System.Text.Json;
using Karnama.Application.Catalog;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Demo-only sample catalog (docs/PROMPT.md §15): service types with forms and workflows, packages, clients and contracts.
/// Every name and number here is SAMPLE data ("Beispiel"), never the agency's real prices.
/// </summary>
internal static class DemoCatalogSeed
{
    private static LocalizedText N(string fa, string en) => new(new Dictionary<string, string> { ["de"] = fa, ["en"] = en });
    private static Dictionary<string, string> D(string fa, string en) => new() { ["de"] = fa, ["en"] = en };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.ServiceTypes.IgnoreQueryFilters().AnyAsync(ct)) return;

        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        Guid? Role(string fa) => roles.FirstOrDefault(r => r.Name.Get("de") == fa)?.Id;
        var editor = Role("Cutter");
        var writer = Role("Fotograf & Texter");
        var ai = Role("KI-Spezialist");

        var topic = new FormField("topic", "text", D("Thema des Inhalts", "Topic"), true, D("Z. B. Vorstellung des neuen Produkts", "e.g. new product launch"), null, null, null);
        var publishAt = new FormField("publishAt", "datetime", D("Veröffentlichungszeit", "Publish at"), false, D("Falls wichtig, schreiben Sie die genaue Uhrzeit dazu", "Exact time if it matters"), null, null, null);
        var files = new FormField("files", "file", D("Dateien und Fotos", "Files"), false, D("Produktfoto, Logo, Rohvideo", "Product photos, raw video"), null, null, null);
        var mood = new FormField("mood", "select", D("Stimmung", "Mood"), false, null, null, null,
            [new FormOption("formal", D("Gesetzlich", "Formal")), new FormOption("friendly", D("Locker", "Friendly")), new FormOption("funny", D("Humorvoll", "Funny"))]);

        ServiceType Service(string fa, string en, string icon, long price, int weight, FormField[] form, (string Fa, string En, Guid? Role, int Minutes, bool Review, bool Client)[] stages)
        {
            var s = ServiceType.Create(N(fa, en), null, icon, price, true, weight, 0).Value;
            s.SetForm(FormSchema.Normalize(form).Value);
            s.SetStages(stages.Select(x => new StageSpec(null, N(x.Fa, x.En), x.Role, x.Minutes, 48, x.Review, x.Client)).ToList());
            db.ServiceTypes.Add(s);
            db.ServiceFormVersions.Add(new ServiceFormVersion(s.Id, s.FormVersion, s.FormSchemaJson, DateTimeOffset.UtcNow));
            return s;
        }

        var reel = Service("Reels", "Reel", "clapperboard", 250, 3, [topic, mood, files, publishAt],
            [("Konzept und Caption", "Script & caption", writer, 60, false, false), ("Dreharbeiten", "Shooting", editor, 120, false, false),
             ("Schnitt", "Editing", editor, 180, true, true)]);
        var post = Service("Einzelpost", "Single post", "image", 80, 1, [topic, mood, files, publishAt],
            [("Design", "Design", writer, 90, true, true)]);
        var carousel = Service("Karussell-Post", "Carousel", "images", 120, 2, [topic, files, publishAt],
            [("Text der Slides", "Slides copy", writer, 60, false, false), ("Design", "Design", ai, 120, true, true)]);
        var story = Service("Story", "Story", "smartphone", 30, 1, [topic, publishAt],
            [("Story-Design", "Story design", ai, 30, false, true)]);
        var caption = Service("Caption", "Caption", "pen-line", 20, 1, [topic, mood],
            [("Caption schreiben", "Writing", writer, 30, true, false)]);
        Service("Influencer-Kampagne", "Influencer campaign", "megaphone", 500, 4, [topic, files],
            [("Influencer auswählen", "Pick influencers", null, 120, true, true), ("Ausführen und Bericht", "Run & report", null, 240, true, false)]);
        await db.SaveChangesAsync(ct);

        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        Guid P(string key) => platforms.First(p => p.Key == key).Id;

        Package Pack(string fa, string en, string slug, string sku, long price, long? special, int validity, (ServiceType S, int C)[] quotas, PageAccess access, PackageBadge badge, string md)
        {
            var spec = new PackageSpec(N(fa, en), slug, sku, "Seitenbetreuung", ["Instagram"], N("Beispiel – " + fa, "Sample — " + en), md,
                price, null, special, null, null, PackagePeriod.Monthly, validity, null, null, 10,
                [P("instagram")], access == PageAccess.FullAccess ? PageManagement.AgencyManaged : PageManagement.ClientManaged, access,
                1, 2, 48, [N("Monatsbericht", "Monthly report"), N("Support während der Geschäftszeiten", "Support in office hours")],
                [new PackageFaq("Ab wann beginnt das Paket?", "Ab dem Tag der Zuweisung, für die Laufzeit des Pakets.")],
                PackageStatus.Active, true, 0, badge, null, null);
            var html = new Content.ContentRenderer().MarkdownToSafeHtml(md);
            var p = Package.Create(spec, quotas.Select(q => new QuotaSpec(q.S.Id, q.C)).ToList(), html).Value;
            db.Packages.Add(p);
            return p;
        }

        var bronze = Pack("Bronze (Beispiel)", "Bronze (sample)", "bronze-beispiel", "PKG-BRONZE", 800, null, 30,
            [(post, 8), (story, 8)], PageAccess.UsernameOnly, PackageBadge.None,
            "## Ideal für den Start\n\n- 8 Einzelposts\n- 8 Storys\n\n**Ihre Seite bleibt in Ihrer Hand**; wir schicken Datei und Veröffentlichungszeit.");
        var silver = Pack("Silber (Beispiel)", "Silver (sample)", "silber-beispiel", "PKG-SILVER", 1500, 1350, 30,
            [(post, 8), (reel, 4), (story, 12), (caption, 12)], PageAccess.FullAccess, PackageBadge.BestSeller,
            "## Bestseller\n\nKomplette Seitenbetreuung durch die Agentur mit **4 Reels** pro Monat.");
        var gold = Pack("Gold (Beispiel)", "Gold (sample)", "gold-beispiel", "PKG-GOLD", 3000, null, 30,
            [(post, 12), (reel, 8), (carousel, 4), (story, 20), (caption, 20)], PageAccess.FullAccess, PackageBadge.New,
            "## Alles aus einer Hand\n\nContent-Produktion, Seitenbetreuung und **monatliche Beratung**.");
        await db.SaveChangesAsync(ct);

        (string Name, string Industry, string Contact, string Mobile, PageManagement M, Package Pkg, int DaysAgo)[] clients =
        [
            ("Café Beispiel", "Café und Restaurant", "Frau Reiter", "015100000004", PageManagement.AgencyManaged, silver, 10),
            ("Mode Beispiel", "Mode", "Herr Krämer", "015100000101", PageManagement.AgencyManaged, gold, 25),
            ("Kosmetikstudio Beispiel", "Beauty", "Dr. Albers", "015100000102", PageManagement.AgencyManaged, silver, 3),
            ("Konditorei Beispiel", "Konditorei", "Herr Müller", "015100000103", PageManagement.ClientManaged, bronze, 28),
            ("Buchhandlung Beispiel", "Bücher", "Frau Hoffmann", "015100000104", PageManagement.ClientManaged, bronze, 15),
            ("Blumenladen Beispiel", "Blumen und Pflanzen", "Herr Jansen", "015100000105", PageManagement.ClientManaged, bronze, 1),
        ];
        var portalUser = await db.Users.FirstOrDefaultAsync(u => u.Phone == "015100000004" && u.IsDemo, ct);
        var names = (await db.ServiceTypes.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get("de"));
        var now = DateTimeOffset.UtcNow;
        foreach (var c in clients)
        {
            var client = Client.Create(new ClientSpec(c.Name, c.Industry, c.Contact, c.Mobile, "Stuttgart", "Beispiel einer internen Notiz – der Kunde sieht sie nicht.",
                ClientStatus.Active, [new ClientAccountSpec(P("instagram"), c.Name.Replace(" ", "_").Replace("‌", "") , c.M),
                    // The café also runs its own Telegram channel: the portal shows publish reminders for it.
                    .. (c.Mobile == "015100000004" ? new[] { new ClientAccountSpec(P("telegram"), "cafe_beispiel", PageManagement.ClientManaged) } : [])])).Value;
            if (c.Mobile == "015100000004" && portalUser is not null) client.LinkPortalUser(portalUser.Id);
            db.Clients.Add(client);
            var cp = ClientPackage.Assign(client.Id, c.Pkg, now.AddDays(-c.DaysAgo), PackageService.SnapshotQuotas(c.Pkg, names), now, null).Value;
            db.ClientPackages.Add(cp);
        }
        await db.SaveChangesAsync(ct);
    }
}
