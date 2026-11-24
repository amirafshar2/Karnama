using System.Text.Json;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Work;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo-only sample orders and work cards in every column, so the board is alive the moment someone logs in.</summary>
internal static class DemoWorkSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Orders.IgnoreQueryFilters().AnyAsync(ct)) return;
        var services = await db.ServiceTypes.AsNoTracking().Include(s => s.Stages).ToListAsync(ct);
        var clients = await db.Clients.AsNoTracking().ToListAsync(ct);
        var packages = await db.ClientPackages.AsNoTracking().ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => u.IsDemo).ToListAsync(ct);
        if (services.Count == 0 || clients.Count == 0) return;

        ServiceType S(string fa) => services.First(s => s.Name.Get("de") == fa);
        Guid C(string name) => clients.First(c => c.BusinessName == name).Id;
        Guid? U(string startsWith) => users.FirstOrDefault(u => u.DisplayName.StartsWith(startsWith, StringComparison.Ordinal))?.Id;
        var ali = U("Lukas");
        var maryam = U("Marie");
        var reza = U("Jonas");
        var negar = U("Lena");

        // (client, service, title, topic, stage index reached, status of that stage, assignees per stage, deadline in hours)
        (string Client, string Service, string Title, string Topic, int Stage, WorkStatus Status, Guid?[] People, int Hours, OrderPriority P)[] rows =
        [
            ("Café Beispiel", "Reels", "Reel zur Herbstkarte", "Vorstellung der Herbstgetränke", 3, WorkStatus.InProgress, [maryam, ali, ali], 20, OrderPriority.High),
            ("Café Beispiel", "Einzelpost", "Post zum Wochenend-Rabatt", "20 % Rabatt am Freitag und Samstag", 1, WorkStatus.ClientReview, [maryam], 30, OrderPriority.Normal),
            ("Café Beispiel", "Story", "Story zu den neuen Öffnungszeiten", "Winter-Öffnungszeiten", 1, WorkStatus.Done, [negar], 5, OrderPriority.Normal),
            ("Mode Beispiel", "Reels", "Reel zur Winterkollektion", "Kollektion Jacken und Mäntel", 2, WorkStatus.Todo, [maryam, reza, reza], 44, OrderPriority.Normal),
            ("Mode Beispiel", "Karussell-Post", "Slide Größentabelle", "Größentabelle Mäntel und Hosen", 2, WorkStatus.InReview, [maryam, negar], 10, OrderPriority.High),
            ("Mode Beispiel", "Caption", "Captions der Woche", "5 Captions für die Posts der Woche", 1, WorkStatus.ChangesRequested, [maryam], 8, OrderPriority.Normal),
            ("Kosmetikstudio Beispiel", "Reels", "Vorher-Nachher-Reel", "Ergebnis der Laser-Haarentfernung", 1, WorkStatus.Todo, [null, null, null], 70, OrderPriority.Urgent),
            ("Kosmetikstudio Beispiel", "Einzelpost", "Post zur Vorstellung der Ärztin", "Vorstellung von Dr. Albers und ihren Fachgebieten", 1, WorkStatus.InProgress, [negar], -3, OrderPriority.High),
            ("Konditorei Beispiel", "Einzelpost", "Post Geburtstagstorte", "Individuelle Geburtstagstorten", 1, WorkStatus.Todo, [maryam], 50, OrderPriority.Normal),
            ("Blumenladen Beispiel", "Story", "Story Blumenstrauß zum Muttertag", "Vorverkauf Blumensträuße", 1, WorkStatus.InProgress, [negar], 26, OrderPriority.Normal),
        ];

        var now = DateTimeOffset.UtcNow;
        var number = 1000L;
        var rank = 0d;
        foreach (var r in rows)
        {
            var svc = S(r.Service);
            var clientId = C(r.Client);
            var data = JsonSerializer.Serialize(new Dictionary<string, string> { ["topic"] = r.Topic, ["mood"] = "friendly" });
            number = await db.NextOrderNumberAsync(ct);
            var order = Order.Create(number, clientId, svc.Id, svc.Name, svc.FormVersion, data, r.Title, "Beispiel – Testauftrag", OrderSource.Admin, r.P,
                now.AddHours(Math.Max(r.Hours, 1)), now.AddMinutes(-10)).Value!;
            var cp = packages.FirstOrDefault(p => p.ClientId == clientId);
            order.Accept(cp?.Id, cp is null, svc.BasePrice, now.AddHours(-30));
            db.Orders.Add(order);

            var stages = svc.Stages.OrderBy(s => s.Order).ToList();
            for (var i = 0; i < stages.Count; i++)
            {
                var item = WorkItem.ForStage(order, stages[i], stages.Count, now.AddHours(r.Hours - 6 * (stages.Count - 1 - i)), rank += 1000);
                item.Assign(r.People.ElementAtOrDefault(i));
                var stageNo = i + 1;
                if (stageNo < r.Stage) item.MoveTo(WorkStatus.Done, WorkActor.Manager, now.AddHours(-20 + i));
                else if (stageNo == r.Stage)
                {
                    item.Unlock();
                    if (r.Status == WorkStatus.ChangesRequested) item.MoveTo(WorkStatus.InReview, WorkActor.Manager, now.AddHours(-3));
                    if (r.Status != WorkStatus.Todo) item.MoveTo(r.Status, WorkActor.Manager, now.AddHours(-2));
                }
                db.WorkItems.Add(item);
            }
            if (r.Stage == stages.Count && r.Status == WorkStatus.Done) order.Complete(now.AddHours(-1));
        }
        await db.SaveChangesAsync(ct);
        await SeedLeaveAsync(db, users.Select(u => (u.Id, u.DisplayName)).ToList(), now, ct);
    }

    /// <summary>A week of sample posts on the calendar; one of the café's own channel posts is due now (portal reminder).</summary>
    public static async Task SeedPublishingAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.PublishJobs.AnyAsync(ct)) return;
        var now = DateTimeOffset.UtcNow;
        var clients = await db.Clients.AsNoTracking().ToListAsync(ct);
        var today = Karnama.Application.Attendance.Iran.Today(now);
        (string Client, int Day, int Hour, string Caption, string[] Tags, bool Own)[] rows =
        [
            ("Café Beispiel", 0, -1, "Unsere Herbstkarte ist da 🍂 Probieren Sie den Apfel-Zimt-Tee.", ["Café", "Stuttgart", "Herbst"], true),
            ("Café Beispiel", 1, 19, "Donnerstagabends gibt es Livemusik 🎶", ["Café", "Livemusik"], false),
            ("Café Beispiel", 2, 12, "Wochenendfrühstück ab 9 Uhr 🥐", ["Frühstück", "Café"], true),
            ("Mode Beispiel", 0, 21, "Winterkollektion – 20 % Rabatt bis zum Wochenende", ["Mode", "Rabatt"], false),
            ("Mode Beispiel", 3, 20, "Hinter den Kulissen beim Shooting der neuen Kollektion", ["hinter_den_kulissen"], false),
            ("Konditorei Beispiel", 2, 18, "Individuelle Geburtstagstorten; bitte zwei Tage vorher bestellen 🎂", ["Kuchen", "Konditorei"], false),
            ("Kosmetikstudio Beispiel", 4, 17, "Antworten auf häufige Fragen zur Hautpflege", ["Haut", "Pflege"], false),
        ];
        foreach (var r in rows)
        {
            var client = clients.FirstOrDefault(c => c.BusinessName == r.Client);
            if (client is null) continue;
            var account = r.Own ? client.Accounts.FirstOrDefault(a => a.Management == PageManagement.ClientManaged) : client.Accounts.FirstOrDefault();
            if (account is null) continue;
            // Hour -1 = 20 minutes ago (already due).
            var at = r.Hour < 0 ? now.AddMinutes(-20) : Karnama.Application.Attendance.Iran.At(today.AddDays(r.Day), new TimeOnly(r.Hour, 0));
            var job = Karnama.Domain.Publishing.PublishJob.Create(client.Id, null, account.PlatformId, account.Handle, account.Management, at, r.Caption, r.Tags, [], at.AddHours(-1)).Value!;
            job.MarkDue(now);
            db.PublishJobs.Add(job);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sample leave and absence rows so the approvals, calendar and report pages aren't empty in the demo.</summary>
    private static async Task SeedLeaveAsync(AppDbContext db, List<(Guid Id, string Name)> users, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.LeaveRequests.AnyAsync(ct)) return;
        var types = await db.LeaveTypes.ToListAsync(ct);
        var annual = types.FirstOrDefault(t => t.Kind == Domain.Attendance.LeaveKind.Annual);
        var mission = types.FirstOrDefault(t => t.Kind == Domain.Attendance.LeaveKind.Mission);
        Guid? U(string p) => users.FirstOrDefault(u => u.Name.StartsWith(p, StringComparison.Ordinal)).Id is var id && id != Guid.Empty ? id : null;
        var manager = U("Sarah");
        if (annual is null || mission is null || manager is null) return;
        var today = Karnama.Application.Attendance.Iran.Today(now);
        DateTimeOffset At(int days, int h) => Karnama.Application.Attendance.Iran.At(today.AddDays(days), new TimeOnly(h, 0));

        if (U("Jonas") is { } reza)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(reza, annual, false, At(2, 0), At(4, 0), "Familienreise (Beispiel)", null, manager.Value, true, now).Value!);
        if (U("Lena") is { } negar)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(negar, annual, true, At(1, 10), At(1, 13), "Bürotätigkeit (Beispiel)", null, negar, false, now).Value!);
        if (U("Lukas") is { } ali)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(ali, mission, false, At(3, 0), At(4, 0), "Dreh beim Kunden vor Ort (Beispiel)", null, ali, false, now).Value!);
        if (U("Daniel") is { } mohammad)
        {
            var a = Domain.Attendance.Absence.Detect(mohammad, today.AddDays(-1), At(-1, 9).AddMinutes(0), At(-1, 9).AddMinutes(15), 48);
            a.SubmitReason("Ich war krank; die Bescheinigung bringe ich morgen (Beispiel)", At(-1, 9), At(-1, 17), null);
            db.Absences.Add(a);
        }
        await db.SaveChangesAsync(ct);
    }
}
