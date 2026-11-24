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
        var marketer = await db.Users.Where(u => u.Phone == "015100000008" && u.IsDemo).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        var sources = await db.LeadSources.ToDictionaryAsync(s => s.Name, s => s.Id, ct);
        var stages = await db.LeadStages.ToDictionaryAsync(s => s.Id, ct);
        if (!stages.ContainsKey(CrmDefaults.New)) return;
        var now = DateTimeOffset.UtcNow;
        Guid? Src(string n) => sources.TryGetValue(n, out var id) ? id : null;

        Lead Add(string name, string phone, string business, string industry, string source, Guid stage, Guid? owner, int hoursAgo, string? message = null, long? budget = null,
            string? instagram = null, string? lostReason = null, Guid? clientId = null)
        {
            var at = now.AddHours(-hoursAgo);
            var l = Lead.Create(new LeadSpec(name, phone, business, industry, "Stuttgart", instagram, Src(source), ["Beispiel"], budget, null), CrmDefaults.New, at, message,
                source == "Website-Formular" ? "/" : null).Value!;
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

        var kazemi = Add("Frau Keller (Beispiel)", "015100000201", "Kunsthandwerk-Galerie", "Kunsthandwerk", "Website-Formular", CrmDefaults.New, marketer, 3, "Wir möchten die komplette Betreuung für die Seite unserer Galerie.");
        kazemi.FollowUp(now.AddHours(1), now);

        var noori = Add("Herr Neumann (Beispiel)", "015100000202", "Fitnessstudio", "Sport", "Website-Formular", CrmDefaults.FirstCall, marketer, 26, "Ich wollte den Preis des Reel-Pakets wissen.");
        if (marketer is { } m1)
            db.LeadActivities.Add(LeadActivity.Call(noori.Id, m1, CallDirection.Outgoing, 240, CallOutcome.Interested, "Interessiert an 8 Reels pro Monat; morgen ein Angebot schicken.", now.AddHours(-20)).Value!);
        noori.FollowUp(now.AddHours(-1), now);   // overdue: shows in "today"

        var sadeghi = Add("Frau Schulz (Beispiel)", "015100000203", "Friseursalon Rose", "Beauty", "Instagram", CrmDefaults.Proposal, marketer, 72, budget: 1500, instagram: "rose_salon_sample");
        if (marketer is { } m2)
            db.LeadActivities.Add(LeadActivity.Contact(sadeghi.Id, m2, ActivityKind.Message, MessageChannel.WhatsApp, "Angebot für das Silber-Paket gesendet.", now.AddHours(-30)));
        sadeghi.FollowUp(now.AddDays(1), now);

        Add("Herr Richter (Beispiel)", "015100000204", "Traditionsrestaurant", "Restaurant", "Vorstellung", CrmDefaults.Meeting, null, 48);
        Add("Frau Albrecht (Beispiel)", "015100000205", "Zahnklinik", "Medizin", "Eingehender Anruf", CrmDefaults.Lost, marketer, 120, lostReason: "Hat derzeit kein Werbebudget.");

        var shop = await db.Clients.Where(c => c.Mobile == "015100000101").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        Add("Herr Krämer (Beispiel)", "015100000101", "Mode Beispiel", "Mode", "Instagram", CrmDefaults.Won, marketer, 24 * 26, budget: 3000, clientId: shop);

        await db.SaveChangesAsync(ct);
    }
}
