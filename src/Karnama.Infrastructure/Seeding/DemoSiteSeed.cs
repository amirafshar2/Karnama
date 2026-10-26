using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Site;
using Karnama.Domain.Content;
using Karnama.Domain.Settings;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Demo portfolio, photos and the home page's video loop. Every sample case is flagged «نمونه» on the site — never shown as real results.
/// The photos and the loop are demo-only files embedded in this assembly; an empty install never gets them.
/// </summary>
internal static class DemoSiteSeed
{
    public static async Task SeedAsync(AppDbContext db, IMediaStore media, CancellationToken ct)
    {
        async Task<string?> Image(string name, string folder)
        {
            await using var s = typeof(DemoSiteSeed).Assembly.GetManifestResourceStream("DemoMedia." + name);
            if (s is null) return null;
            var r = await media.SaveImageAsync(s, s.Length, folder, ct);
            return r.IsSuccess ? r.Value : null;
        }

        (string Name, string Industry, long Fb, long Fa, string[] Services, string Summary, string Quote, string By, int Sort, string Photo)[] rows =
        [
            ("کافه نمونه", "کافه و رستوران", 1_200, 18_000, ["مدیریت پیج", "ریلز", "عکاسی"], "برنامه‌ی انتشار ثابت، ریلز هفتگی از پشت صحنه و کمپین منوی فصلی.", "پیج ما بالاخره زنده شد؛ هر هفته مشتری تازه می‌گوید از اینستاگرام ما را پیدا کرده.", "مدیر کافه (نمونه)", 1, "cafe.jpg"),
            ("پوشاک نمونه", "پوشاک", 4_500, 31_000, ["مدیریت پیج", "تبلیغات", "عکاسی محصول"], "عکاسی محصول یکدست، کاروسل کالکشن‌ها و تبلیغات هدفمند در شیراز.", "فروش آنلاین‌مان از فروش مغازه جلو زد.", "مدیر فروشگاه (نمونه)", 2, "boutique.jpg"),
            ("کلینیک زیبایی نمونه", "زیبایی", 2_100, 9_800, ["تولید محتوا", "ریلز"], "محتوای آموزشی کوتاه و پاسخ به پرسش‌های پرتکرار در ریلز.", "نوبت‌های اینترنتی دو برابر شد.", "مدیر کلینیک (نمونه)", 3, "beauty.jpg"),
            ("بستنی سنتی نمونه", "شیرینی و بستنی", 800, 12_400, ["ریلز", "عکاسی غذا"], "ریلزهای کوتاه از تهیه‌ی بستنی و کمپین تابستانه با اینفلوئنسرهای شهر.", "تابستان امسال صف جلوی مغازه از اینستاگرام آمد.", "مدیر بستنی‌فروشی (نمونه)", 4, "icecream.jpg"),
            ("رستوران نمونه", "کافه و رستوران", 3_000, 22_500, ["مدیریت پیج", "عکاسی غذا", "تبلیغات"], "عکاسی منو، استوری روزانه‌ی غذای روز و تبلیغ رزرو آخر هفته.", "رزرو آخر هفته‌ها پر می‌شود.", "مدیر رستوران (نمونه)", 5, "restaurant.jpg"),
        ];

        if (!await db.CaseStudies.IgnoreQueryFilters().AnyAsync(ct))
        {
            foreach (var r in rows)
            {
                var c = CaseStudy.Create(new CaseStudy.Spec(r.Name + " (نمونه)", null, r.Industry, "شیراز", null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-8)), null,
                    r.Services, r.Summary, r.Fb, r.Fa, null, null, true, [new CaseMetric("بازدید ماهانه‌ی ریلز", 3_000, 120_000, null)], [], r.Quote, r.By, null,
                    "داده‌ی نمونه برای نمایش", false, null, r.Sort == 1, r.Sort), isSample: true).Value!;
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
        var posts = await db.BlogPosts.Where(p => p.CoverImage == null && p.Title.Contains("(نمونه)")).OrderBy(p => p.Id).ToListAsync(ct);
        var photos = new[] { ("creator.jpg", "فیلم‌برداری ریلز با گوشی در باغ"), ("cafe.jpg", "باریستا در حال ریختن لاته‌آرت"), ("boutique.jpg", "عکاسی محصول در بوتیک") };
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
            if (s is not null && await media.SaveVideoAsync(s, s.Length, "site", ct) is { IsSuccess: true } v) video = v.Value;
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
