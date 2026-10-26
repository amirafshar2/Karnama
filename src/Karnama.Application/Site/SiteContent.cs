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
        "مدیریت پیج اینستاگرام و تولید محتوا در شیراز",
        "مدیریت پیج اینستاگرام، تولید محتوا، ریلز و تبلیغات برای کسب‌وکارهای شیراز؛ با برنامه‌ی انتشار، گزارش ماهانه و پنل پیگیری برای هر مشتری.",
        "پیج شما، هر روز سر وقت و با کیفیت",
        "از ایده و عکاسی تا ریلز، کپشن و انتشار؛ همه را برایتان انجام می‌دهیم و شما در پنل خودتان پیشرفت کار را می‌بینید.",
        "مشاوره‌ی رایگان",
        "خدمات", "نمونه‌کارها", "پکیج‌ها", "روند همکاری",
        [
            new("گفتگوی اول", "هدف، مخاطب و وضعیت فعلی پیج شما را می‌شنویم."),
            new("برنامه‌ی محتوا", "تقویم انتشار ماهانه با ایده‌ها و زمان‌ها آماده می‌شود."),
            new("تولید و تأیید", "محتوا ساخته می‌شود و شما در پنل تأیید یا اصلاح می‌خواهید."),
            new("انتشار و گزارش", "سر وقت منتشر می‌کنیم و آخر ماه گزارش می‌دهیم."),
        ],
        "نظر مشتری‌ها", "آخرین مطالب", "پرسش‌های متداول",
        [
            new("قرارداد حداقل چند ماهه است؟", "پکیج‌ها ماهانه‌اند و می‌توانید هر ماه تمدید کنید."),
            new("پیج دست ما می‌ماند یا شما؟", "هر دو حالت ممکن است؛ اگر پیج دست خودتان بماند، فایل و کپشن و زمان انتشار را برایتان می‌فرستیم."),
            new("چطور پیشرفت کار را ببینیم؟", "هر مشتری پنل خودش را دارد: سفارش‌ها، محتوای منتظر تأیید و مانده‌ی پکیج همیشه جلوی چشمتان است."),
        ],
        "مشاوره‌ی رایگان", "شماره‌تان را بگذارید؛ در ساعات کاری تماس می‌گیریم.",
        new ContactInfo(null, null, "شیراز", "شنبه تا چهارشنبه ۹ تا ۱۷", null, null, null, null, null, null, null, null),
        true, true, true);
}

public sealed class HomeContentValidator : AbstractValidator<HomeContent>
{
    public HomeContentValidator()
    {
        RuleFor(x => x.SeoTitle).NotEmpty().MaximumLength(60).WithMessage("عنوان سئو الزامی است و حداکثر ۶۰ نویسه.");
        RuleFor(x => x.SeoDescription).NotEmpty().Length(50, 160).WithMessage("توضیح سئو الزامی است، بین ۵۰ تا ۱۶۰ نویسه.");
        RuleFor(x => x.HeroTitle).NotEmpty().MaximumLength(90).WithMessage("تیتر اصلی الزامی است (حداکثر ۹۰ نویسه).");
        RuleFor(x => x.HeroSubtitle).MaximumLength(300);
        RuleFor(x => x.CtaLabel).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Process).Must(p => p is null || p.Count <= 8).WithMessage("حداکثر ۸ مرحله.");
        RuleFor(x => x.Faq).Must(p => p is null || p.Count <= 20).WithMessage("حداکثر ۲۰ پرسش.");
        RuleForEach(x => x.Faq).Must(f => !string.IsNullOrWhiteSpace(f.Question) && f.Question.Length <= 200 && !string.IsNullOrWhiteSpace(f.Answer) && f.Answer.Length <= 1000)
            .WithMessage("هر پرسش و پاسخ باید پر باشد (پرسش تا ۲۰۰ و پاسخ تا ۱۰۰۰ نویسه).");
        RuleFor(x => x.Contact.NeshanUrl).Must(Https).WithMessage("لینک نشان باید با https:// شروع شود.");
        RuleFor(x => x.Contact.BaladUrl).Must(Https).WithMessage("لینک بلد باید با https:// شروع شود.");
        RuleFor(x => x.HeroVideo).Must(v => v is null || HomeContent.VideoId.IsMatch(v)).WithMessage("ویدیوی سربرگ باید از همین پنل بارگذاری شده باشد.");
        RuleFor(x => x.HeroPoster).Must(v => v is null || HomeContent.ImageId.IsMatch(v)).WithMessage("عکس پیش‌نمایش ویدیو باید از همین پنل بارگذاری شده باشد.");
        RuleFor(x => x.HeroImages).Must(v => v is null || (v.Count <= 6 && v.All(HomeContent.ImageId.IsMatch)))
            .WithMessage("حداکثر ۶ عکس سربرگ، همه بارگذاری‌شده از همین پنل.");
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
