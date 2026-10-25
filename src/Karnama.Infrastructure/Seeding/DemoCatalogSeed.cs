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
/// Every name and number here is SAMPLE data ("نمونه"), never the agency's real prices.
/// </summary>
internal static class DemoCatalogSeed
{
    private static LocalizedText N(string fa, string en) => new(new Dictionary<string, string> { ["fa"] = fa, ["en"] = en });
    private static Dictionary<string, string> D(string fa, string en) => new() { ["fa"] = fa, ["en"] = en };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.ServiceTypes.IgnoreQueryFilters().AnyAsync(ct)) return;

        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        Guid? Role(string fa) => roles.FirstOrDefault(r => r.Name.Get("fa") == fa)?.Id;
        var editor = Role("تدوینگر");
        var writer = Role("عکاس و متن‌نویس");
        var ai = Role("کار با هوش مصنوعی");

        var topic = new FormField("topic", "text", D("موضوع محتوا", "Topic"), true, D("مثلاً معرفی محصول جدید", "e.g. new product launch"), null, null, null);
        var publishAt = new FormField("publishAt", "datetime", D("زمان انتشار", "Publish at"), false, D("اگر مهم است، ساعت دقیق را بنویسید", "Exact time if it matters"), null, null, null);
        var files = new FormField("files", "file", D("فایل‌ها و عکس‌ها", "Files"), false, D("عکس محصول، لوگو، ویدیوی خام", "Product photos, raw video"), null, null, null);
        var mood = new FormField("mood", "select", D("حس و حال", "Mood"), false, null, null, null,
            [new FormOption("formal", D("رسمی", "Formal")), new FormOption("friendly", D("صمیمی", "Friendly")), new FormOption("funny", D("طنز", "Funny"))]);

        ServiceType Service(string fa, string en, string icon, long price, int weight, FormField[] form, (string Fa, string En, Guid? Role, int Minutes, bool Review, bool Client)[] stages)
        {
            var s = ServiceType.Create(N(fa, en), null, icon, price, true, weight, 0).Value;
            s.SetForm(FormSchema.Normalize(form).Value);
            s.SetStages(stages.Select(x => new StageSpec(null, N(x.Fa, x.En), x.Role, x.Minutes, 48, x.Review, x.Client)).ToList());
            db.ServiceTypes.Add(s);
            db.ServiceFormVersions.Add(new ServiceFormVersion(s.Id, s.FormVersion, s.FormSchemaJson, DateTimeOffset.UtcNow));
            return s;
        }

        var reel = Service("ریلز", "Reel", "clapperboard", 25_000_000, 3, [topic, mood, files, publishAt],
            [("سناریو و کپشن", "Script & caption", writer, 60, false, false), ("فیلمبرداری", "Shooting", editor, 120, false, false),
             ("تدوین", "Editing", editor, 180, true, true)]);
        var post = Service("پست تکی", "Single post", "image", 8_000_000, 1, [topic, mood, files, publishAt],
            [("طراحی", "Design", writer, 90, true, true)]);
        var carousel = Service("پست اسلایدی", "Carousel", "images", 12_000_000, 2, [topic, files, publishAt],
            [("متن اسلایدها", "Slides copy", writer, 60, false, false), ("طراحی", "Design", ai, 120, true, true)]);
        var story = Service("استوری", "Story", "smartphone", 3_000_000, 1, [topic, publishAt],
            [("طراحی استوری", "Story design", ai, 30, false, true)]);
        var caption = Service("کپشن", "Caption", "pen-line", 2_000_000, 1, [topic, mood],
            [("نوشتن کپشن", "Writing", writer, 30, true, false)]);
        Service("کمپین اینفلوئنسر", "Influencer campaign", "megaphone", 50_000_000, 4, [topic, files],
            [("انتخاب اینفلوئنسر", "Pick influencers", null, 120, true, true), ("اجرا و گزارش", "Run & report", null, 240, true, false)]);
        await db.SaveChangesAsync(ct);

        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        Guid P(string key) => platforms.First(p => p.Key == key).Id;

        Package Pack(string fa, string en, string slug, string sku, long price, long? special, int validity, (ServiceType S, int C)[] quotas, PageAccess access, PackageBadge badge, string md)
        {
            var spec = new PackageSpec(N(fa, en), slug, sku, "مدیریت پیج", ["اینستاگرام"], N("نمونه — " + fa, "Sample — " + en), md,
                price, null, special, null, null, PackagePeriod.Monthly, validity, null, null, 10,
                [P("instagram")], access == PageAccess.FullAccess ? PageManagement.AgencyManaged : PageManagement.ClientManaged, access,
                1, 2, 48, [N("گزارش ماهانه", "Monthly report"), N("پشتیبانی در ساعات کاری", "Support in office hours")],
                [new PackageFaq("پکیج از کی شروع می‌شود؟", "از روز تخصیص، به مدت اعتبار پکیج.")],
                PackageStatus.Active, true, 0, badge, null, null);
            var html = new Content.ContentRenderer().MarkdownToSafeHtml(md);
            var p = Package.Create(spec, quotas.Select(q => new QuotaSpec(q.S.Id, q.C)).ToList(), html).Value;
            db.Packages.Add(p);
            return p;
        }

        var bronze = Pack("برنزی (نمونه)", "Bronze (sample)", "برنزی-نمونه", "PKG-BRONZE", 80_000_000, null, 30,
            [(post, 8), (story, 8)], PageAccess.UsernameOnly, PackageBadge.None,
            "## مناسب شروع\n\n- ۸ پست تکی\n- ۸ استوری\n\n**پیج دست خود شما** می‌ماند؛ ما فایل و ساعت انتشار را می‌فرستیم.");
        var silver = Pack("نقره‌ای (نمونه)", "Silver (sample)", "نقره‌ای-نمونه", "PKG-SILVER", 150_000_000, 135_000_000, 30,
            [(post, 8), (reel, 4), (story, 12), (caption, 12)], PageAccess.FullAccess, PackageBadge.BestSeller,
            "## پرفروش‌ترین\n\nمدیریت کامل پیج توسط آژانس با **۴ ریلز** در ماه.");
        var gold = Pack("طلایی (نمونه)", "Gold (sample)", "طلایی-نمونه", "PKG-GOLD", 300_000_000, null, 30,
            [(post, 12), (reel, 8), (carousel, 4), (story, 20), (caption, 20)], PageAccess.FullAccess, PackageBadge.New,
            "## همه‌چیز با ما\n\nتولید محتوا، مدیریت پیج و **مشاوره‌ی ماهانه**.");
        await db.SaveChangesAsync(ct);

        (string Name, string Industry, string Contact, string Mobile, PageManagement M, Package Pkg, int DaysAgo)[] clients =
        [
            ("کافه نمونه", "کافه و رستوران", "خانم رضایی", "09000000004", PageManagement.AgencyManaged, silver, 10),
            ("پوشاک نمونه", "پوشاک", "آقای کریمی", "09000000101", PageManagement.AgencyManaged, gold, 25),
            ("کلینیک زیبایی نمونه", "زیبایی", "دکتر احمدی", "09000000102", PageManagement.AgencyManaged, silver, 3),
            ("شیرینی‌فروشی نمونه", "شیرینی", "آقای محمدی", "09000000103", PageManagement.ClientManaged, bronze, 28),
            ("کتاب‌فروشی نمونه", "کتاب", "خانم حسینی", "09000000104", PageManagement.ClientManaged, bronze, 15),
            ("گل‌فروشی نمونه", "گل و گیاه", "آقای جعفری", "09000000105", PageManagement.ClientManaged, bronze, 1),
        ];
        var portalUser = await db.Users.FirstOrDefaultAsync(u => u.Phone == "09000000004" && u.IsDemo, ct);
        var names = (await db.ServiceTypes.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get("fa"));
        var now = DateTimeOffset.UtcNow;
        foreach (var c in clients)
        {
            var client = Client.Create(new ClientSpec(c.Name, c.Industry, c.Contact, c.Mobile, "شیراز", "یادداشت داخلی نمونه — مشتری نمی‌بیند.",
                ClientStatus.Active, [new ClientAccountSpec(P("instagram"), c.Name.Replace(" ", "_").Replace("‌", "") , c.M)])).Value;
            if (c.Mobile == "09000000004" && portalUser is not null) client.LinkPortalUser(portalUser.Id);
            db.Clients.Add(client);
            var cp = ClientPackage.Assign(client.Id, c.Pkg, now.AddDays(-c.DaysAgo), PackageService.SnapshotQuotas(c.Pkg, names), now, null).Value;
            db.ClientPackages.Add(cp);
        }
        await db.SaveChangesAsync(ct);
    }
}
