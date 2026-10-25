using System.Text.Json.Nodes;
using Karnama.Application.Blog;
using Karnama.Domain.Content;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Sample posts (marked «نمونه») so the blog, the editor and the SEO checklist aren't empty in the demo.</summary>
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
        var admin = await db.Users.Where(u => u.IsDemo && u.DisplayName.StartsWith("سارا")).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var writer = await db.Users.Where(u => u.IsDemo && u.DisplayName.StartsWith("مریم")).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (admin == Guid.Empty) return;
        if (writer == Guid.Empty) writer = admin;

        var insta = BlogCategory.Create("اینستاگرام", "مدیریت پیج، ریلز و استوری", 1).Value!;
        var content = BlogCategory.Create("تولید محتوا", "عکاسی، کپشن و ایده‌ی محتوا", 2).Value!;
        db.BlogCategories.AddRange(insta, content);

        var now = DateTimeOffset.UtcNow;
        var reelsSlug = SeoAutomation.SlugFor("ریلز یا پست؟ کدام برای فروش کسب‌وکار بهتر است (نمونه)");
        var posts = new (string Title, Guid Author, Guid Cat, string[] Tags, string Json, PostStatus Status, int DaysAgo)[]
        {
            ("۱۰ نکته برای مدیریت پیج اینستاگرام کسب‌وکارهای شیراز (نمونه)", admin, insta.Id, ["اینستاگرام", "شیراز"], Doc(
                P("مدیریت پیج اینستاگرام برای یک کافه یا فروشگاه در شیراز فقط گذاشتن چند پست در هفته نیست؛ برنامه، لحن ثابت و جواب سریع به پیام‌ها مشتری را نگه می‌دارد. این مطلب نمونه است و برای نمایش ویرایشگر و سئوی خودکار نوشته شده."),
                H(2, "برنامه‌ی انتشار ثابت داشته باشید"),
                P("مخاطب وقتی بداند چه روزهایی منتظر محتوای شما باشد، بیشتر برمی‌گردد. یک تقویم انتشار ساده با سه پست و پنج استوری در هفته برای شروع کافی است."),
                Ul("روزهای ثابت برای ریلز", "استوری روزانه از پشت صحنه", "یک پست آموزشی در هفته"),
                Tip("در کارنما از «تقویم انتشار» برای زمان‌بندی و یادآوری استفاده کنید."),
                H(2, "محتوای بومی و نزدیک به مشتری"),
                P(Text("عکس واقعی از فضای مغازه و چهره‌ی آشنای کارکنان بیشتر از عکس‌های آماده اعتماد می‌سازد. برای ویدیو، "), Link("مقایسه‌ی ریلز و پست", "/blog/" + reelsSlug), Text(" را هم بخوانید.")),
                H(2, "پرسش‌های متداول"),
                H(3, "هر چند وقت یک بار پست بگذاریم؟"),
                P("برای بیشتر کسب‌وکارهای کوچک سه پست در هفته و استوری روزانه نقطه‌ی شروع خوبی است."),
                H(3, "به پیام‌ها چقدر سریع جواب بدهیم؟"),
                P("تا جای ممکن در ساعات کاری کمتر از یک ساعت؛ سرعت جواب مستقیم روی فروش اثر دارد.")), PostStatus.Published, 6),
            ("ریلز یا پست؟ کدام برای فروش کسب‌وکار بهتر است (نمونه)", admin, insta.Id, ["ریلز", "اینستاگرام"], Doc(
                P("ریلز برای دیده‌شدن توسط آدم‌های تازه عالی است و پست برای معرفی دقیق محصول. ترکیب درست این دو بستگی به هدف شما دارد. این مطلب نمونه است."),
                H(2, "ریلز برای دیده‌شدن"),
                P("ریلزهای کوتاه زیر سی ثانیه با یک قلاب قوی در دو ثانیه‌ی اول، بیشترین بازدید را از کسانی می‌گیرند که هنوز شما را دنبال نمی‌کنند."),
                H(2, "پست و کاروسل برای فروش"),
                P("کاروسل با چند عکس از محصول، قیمت و راه سفارش، تصمیم خرید را ساده‌تر می‌کند."),
                Ul("هفته‌ای دو ریلز برای جذب", "یک کاروسل محصول", "استوری با نظرسنجی برای تعامل")), PostStatus.Published, 3),
            ("کپشن‌نویسی: ۵ قالب ساده که جواب می‌دهد (نمونه)", writer, content.Id, ["کپشن", "تولید محتوا"], Doc(
                P("کپشن خوب کوتاه شروع می‌شود، یک مشکل را نام می‌برد و آخرش می‌گوید مخاطب چه کار کند. این پیش‌نویس نمونه منتظر بازبینی است."),
                H(2, "قالب مشکل و راه‌حل"),
                P("یک جمله درباره‌ی مشکل مشتری، یک جمله درباره‌ی راه‌حل شما و یک دعوت به اقدام.")), PostStatus.InReview, 1),
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
