using Karnama.Domain.Content;
using Karnama.Domain.Crm;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo portfolio and consultation requests. Every sample case is flagged «نمونه» on the site — never shown as real results.</summary>
internal static class DemoSiteSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.CaseStudies.IgnoreQueryFilters().AnyAsync(ct))
        {
            (string Name, string Industry, long Fb, long Fa, string[] Services, string Summary, string Quote, string By, int Sort)[] rows =
            [
                ("کافه نمونه", "کافه و رستوران", 1_200, 18_000, ["مدیریت پیج", "ریلز", "عکاسی"], "برنامه‌ی انتشار ثابت، ریلز هفتگی از پشت صحنه و کمپین منوی فصلی.", "پیج ما بالاخره زنده شد؛ هر هفته مشتری تازه می‌گوید از اینستاگرام ما را پیدا کرده.", "مدیر کافه (نمونه)", 1),
                ("پوشاک نمونه", "پوشاک", 4_500, 31_000, ["مدیریت پیج", "تبلیغات", "عکاسی محصول"], "عکاسی محصول یکدست، کاروسل کالکشن‌ها و تبلیغات هدفمند در شیراز.", "فروش آنلاین‌مان از فروش مغازه جلو زد.", "مدیر فروشگاه (نمونه)", 2),
                ("کلینیک زیبایی نمونه", "زیبایی", 2_100, 9_800, ["تولید محتوا", "ریلز"], "محتوای آموزشی کوتاه و پاسخ به پرسش‌های پرتکرار در ریلز.", "نوبت‌های اینترنتی دو برابر شد.", "مدیر کلینیک (نمونه)", 3),
            ];
            foreach (var r in rows)
            {
                var c = CaseStudy.Create(new CaseStudy.Spec(r.Name + " (نمونه)", null, r.Industry, "شیراز", null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-8)), null,
                    r.Services, r.Summary, r.Fb, r.Fa, null, null, true, [new CaseMetric("بازدید ماهانه‌ی ریلز", 3_000, 120_000, null)], [], r.Quote, r.By, null,
                    "داده‌ی نمونه برای نمایش", false, null, r.Sort == 1, r.Sort), isSample: true).Value!;
                c.SetPublished(true);
                db.CaseStudies.Add(c);
            }
        }
        if (!await db.Leads.AnyAsync(ct))
        {
            var now = DateTimeOffset.UtcNow;
            db.Leads.Add(Lead.Create("خانم کاظمی (نمونه)", "09000000201", "گالری صنایع دستی", "برای پیج گالری‌مان مدیریت کامل می‌خواهیم.", "/", now.AddHours(-3)).Value!);
            db.Leads.Add(Lead.Create("آقای نوری (نمونه)", "09000000202", "باشگاه ورزشی", "قیمت پکیج ریلز را می‌خواستم.", "/packages", now.AddDays(-1)).Value!);
        }
        await db.SaveChangesAsync(ct);
    }
}
