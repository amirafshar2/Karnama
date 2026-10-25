using Karnama.Domain.Crm;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo sales pipeline: a few leads in different columns for the demo marketer, with calls and follow-ups.</summary>
internal static class DemoCrmSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Leads.AnyAsync(ct)) return;
        var marketer = await db.Users.Where(u => u.Phone == "09000000008" && u.IsDemo).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        var sources = await db.LeadSources.ToDictionaryAsync(s => s.Name, s => s.Id, ct);
        var stages = await db.LeadStages.ToDictionaryAsync(s => s.Id, ct);
        if (!stages.ContainsKey(CrmDefaults.New)) return;
        var now = DateTimeOffset.UtcNow;
        Guid? Src(string n) => sources.TryGetValue(n, out var id) ? id : null;

        Lead Add(string name, string phone, string business, string industry, string source, Guid stage, Guid? owner, int hoursAgo, string? message = null, long? budget = null,
            string? instagram = null, string? lostReason = null, Guid? clientId = null)
        {
            var at = now.AddHours(-hoursAgo);
            var l = Lead.Create(new LeadSpec(name, phone, business, industry, "شیراز", instagram, Src(source), ["نمونه"], budget, null), CrmDefaults.New, at, message,
                source == "فرم سایت" ? "/" : null).Value!;
            l.Assign(owner, at);
            db.Leads.Add(l);
            db.LeadActivities.Add(LeadActivity.Log(l.Id, null, ActivityKind.Created, at, source));
            if (owner is { } o) db.LeadActivities.Add(LeadActivity.Log(l.Id, null, ActivityKind.Assigned, at.AddMinutes(5), null, null, o));
            if (stage != CrmDefaults.New)
            {
                l.Move(stages[stage], lostReason, at.AddHours(1));
                db.LeadActivities.Add(LeadActivity.Log(l.Id, owner, ActivityKind.StageChanged, at.AddHours(1), lostReason, CrmDefaults.New, stage));
            }
            if (clientId is { } c) l.ConvertedTo(c, at.AddHours(2));
            return l;
        }

        var kazemi = Add("خانم کاظمی (نمونه)", "09000000201", "گالری صنایع دستی", "صنایع دستی", "فرم سایت", CrmDefaults.New, marketer, 3, "برای پیج گالری‌مان مدیریت کامل می‌خواهیم.");
        kazemi.FollowUp(now.AddHours(1), now);

        var noori = Add("آقای نوری (نمونه)", "09000000202", "باشگاه ورزشی", "ورزش", "فرم سایت", CrmDefaults.FirstCall, marketer, 26, "قیمت پکیج ریلز را می‌خواستم.");
        if (marketer is { } m1)
            db.LeadActivities.Add(LeadActivity.Call(noori.Id, m1, CallDirection.Outgoing, 240, CallOutcome.Interested, "علاقه‌مند به ۸ ریلز در ماه؛ فردا پیشنهاد قیمت بفرستیم.", now.AddHours(-20)).Value!);
        noori.FollowUp(now.AddHours(-1), now);   // overdue: shows in "today"

        var sadeghi = Add("خانم صادقی (نمونه)", "09000000203", "آرایشگاه رز", "زیبایی", "اینستاگرام", CrmDefaults.Proposal, marketer, 72, budget: 150_000_000, instagram: "rose_salon_sample");
        if (marketer is { } m2)
            db.LeadActivities.Add(LeadActivity.Contact(sadeghi.Id, m2, ActivityKind.Message, MessageChannel.WhatsApp, "پیشنهاد پکیج نقره‌ای فرستاده شد.", now.AddHours(-30)));
        sadeghi.FollowUp(now.AddDays(1), now);

        Add("آقای رحیمی (نمونه)", "09000000204", "رستوران سنتی", "رستوران", "معرفی", CrmDefaults.Meeting, null, 48);
        Add("خانم امینی (نمونه)", "09000000205", "کلینیک دندان‌پزشکی", "پزشکی", "تماس ورودی", CrmDefaults.Lost, marketer, 120, lostReason: "فعلاً بودجه‌ی تبلیغات ندارد.");

        var shop = await db.Clients.Where(c => c.Mobile == "09000000101").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        Add("آقای کریمی (نمونه)", "09000000101", "پوشاک نمونه", "پوشاک", "اینستاگرام", CrmDefaults.Won, marketer, 24 * 26, budget: 300_000_000, clientId: shop);

        await db.SaveChangesAsync(ct);
    }
}
