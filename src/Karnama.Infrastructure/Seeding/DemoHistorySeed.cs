using System.Text.Json;
using Karnama.Application.Attendance;
using Karnama.Domain.Attendance;
using Karnama.Domain.Work;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Demo-only past month for the work report and scores (PROMPT2 §9, §11): shifts, finished work with timers,
/// deadlines, a few rounds of changes and star ratings — different for each person so the comparison means something.
/// </summary>
internal static class DemoHistorySeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = Iran.Today(now);
        if (await db.Shifts.AnyAsync(s => s.Day < today.AddDays(-7), ct)) return;
        var users = await db.Users.AsNoTracking().Where(u => u.IsDemo && u.Kind == Karnama.Domain.Identity.UserKind.Staff).ToListAsync(ct);
        var services = await db.ServiceTypes.AsNoTracking().Include(s => s.Stages).ToListAsync(ct);
        var clients = await db.Clients.AsNoTracking().ToListAsync(ct);
        if (services.Count == 0 || clients.Count == 0) return;

        // (name prefix, late every Nth day, items per week, speed factor, revision every Nth item, on-time every Nth miss)
        (string Who, int LateEvery, int PerWeek, double Speed, int ReviseEvery, int MissEvery)[] people =
        [
            ("علی", 9, 3, 0.95, 5, 8),
            ("مریم", 6, 4, 1.15, 3, 4),
            ("نگار", 0, 3, 0.85, 0, 0),
            ("رضا", 4, 2, 1.4, 2, 3),
        ];
        var rnd = new Random(1405);
        var serviceList = services.Where(s => s.Stages.Count > 0).ToList();
        foreach (var p in people)
        {
            var user = users.FirstOrDefault(u => u.DisplayName.StartsWith(p.Who, StringComparison.Ordinal));
            if (user is null) continue;
            var n = 0;
            for (var back = 35; back >= 1; back--)
            {
                var day = today.AddDays(-back);
                if (day.DayOfWeek == DayOfWeek.Friday) continue;
                var late = p.LateEvery > 0 && back % p.LateEvery == 0 ? 10 + rnd.Next(25) : 0;
                var inAt = Iran.At(day, new TimeOnly(9, 0)).AddMinutes(late);
                var shift = Shift.Open(user.Id, day, inAt, [], Trust.Green, false, null, false, late);
                shift.Close(Iran.At(day, new TimeOnly(17, 0)).AddMinutes(rnd.Next(40)), [], Trust.Green, false, null, false, 0, null);
                db.Shifts.Add(shift);

                // Finished work on some days.
                if (rnd.NextDouble() > p.PerWeek / 6.0) continue;
                n++;
                var svc = serviceList[rnd.Next(serviceList.Count)];
                var stage = svc.Stages.OrderBy(s => s.Order).First();
                var client = clients[rnd.Next(clients.Count)];
                var number = await db.NextOrderNumberAsync(ct);
                var data = JsonSerializer.Serialize(new Dictionary<string, string> { ["topic"] = "نمونه" });
                var start = inAt.AddMinutes(30 + rnd.Next(60));
                var order = Order.Create(number, client.Id, svc.Id, svc.Name, svc.FormVersion, data, $"{svc.Name.Get("fa")} — نمونه‌ی ماه قبل", null, OrderSource.Admin,
                    OrderPriority.Normal, start.AddHours(8), start.AddMinutes(-60)).Value!;
                order.Accept(null, true, svc.BasePrice, start.AddMinutes(-50));
                db.Orders.Add(order);

                var estimate = Math.Max(30, stage.EstimatedMinutes);
                var deadline = start.AddHours(6);
                var item = WorkItem.ForStage(order, stage, 1, deadline, n * 10);
                item.Assign(user.Id);
                item.MoveTo(WorkStatus.InProgress, WorkActor.Manager, start);
                var actual = (int)Math.Round(estimate * p.Speed * (0.85 + rnd.NextDouble() * 0.3));
                var timer = new WorkTimer(user.Id, item.Id, shift.Id, start);
                timer.Stop(start.AddMinutes(actual));
                db.WorkTimers.Add(timer);
                // Some deliveries miss the deadline.
                var delivered = p.MissEvery > 0 && n % p.MissEvery == 0 ? deadline.AddHours(2) : start.AddMinutes(actual + 5);
                item.MoveTo(WorkStatus.InReview, WorkActor.Manager, delivered);
                if (p.ReviseEvery > 0 && n % p.ReviseEvery == 0)
                {
                    item.MoveTo(WorkStatus.ChangesRequested, WorkActor.Manager, delivered.AddMinutes(30));
                    item.MoveTo(WorkStatus.InReview, WorkActor.Manager, delivered.AddMinutes(90));
                }
                var done = delivered.AddHours(2);
                item.MoveTo(WorkStatus.Done, WorkActor.Manager, done);
                item.RateByManager(p.ReviseEvery == 0 ? 5 : n % 2 == 0 ? 4 : 3);
                db.WorkItems.Add(item);
                order.Complete(done);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
