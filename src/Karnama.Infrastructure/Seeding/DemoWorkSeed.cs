using System.Text.Json;
using Karnama.Domain.Catalog;
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

        ServiceType S(string fa) => services.First(s => s.Name.Get("fa") == fa);
        Guid C(string name) => clients.First(c => c.BusinessName == name).Id;
        Guid? U(string startsWith) => users.FirstOrDefault(u => u.DisplayName.StartsWith(startsWith, StringComparison.Ordinal))?.Id;
        var ali = U("علی");
        var maryam = U("مریم");
        var reza = U("رضا");
        var negar = U("نگار");

        // (client, service, title, topic, stage index reached, status of that stage, assignees per stage, deadline in hours)
        (string Client, string Service, string Title, string Topic, int Stage, WorkStatus Status, Guid?[] People, int Hours, OrderPriority P)[] rows =
        [
            ("کافه نمونه", "ریلز", "ریلز منوی پاییزه", "معرفی نوشیدنی‌های پاییزی", 3, WorkStatus.InProgress, [maryam, ali, ali], 20, OrderPriority.High),
            ("کافه نمونه", "پست تکی", "پست تخفیف آخر هفته", "۲۰٪ تخفیف پنجشنبه و جمعه", 1, WorkStatus.ClientReview, [maryam], 30, OrderPriority.Normal),
            ("کافه نمونه", "استوری", "استوری ساعت کاری جدید", "ساعت کاری زمستان", 1, WorkStatus.Done, [negar], 5, OrderPriority.Normal),
            ("پوشاک نمونه", "ریلز", "ریلز کالکشن زمستانه", "کالکشن کاپشن و پالتو", 2, WorkStatus.Todo, [maryam, reza, reza], 44, OrderPriority.Normal),
            ("پوشاک نمونه", "پست اسلایدی", "اسلاید راهنمای سایز", "جدول سایز مانتو و شلوار", 2, WorkStatus.InReview, [maryam, negar], 10, OrderPriority.High),
            ("پوشاک نمونه", "کپشن", "کپشن‌های هفته", "۵ کپشن برای پست‌های هفته", 1, WorkStatus.ChangesRequested, [maryam], 8, OrderPriority.Normal),
            ("کلینیک زیبایی نمونه", "ریلز", "ریلز قبل و بعد", "نتیجه‌ی لیزر موهای زائد", 1, WorkStatus.Todo, [null, null, null], 70, OrderPriority.Urgent),
            ("کلینیک زیبایی نمونه", "پست تکی", "پست معرفی دکتر", "معرفی دکتر احمدی و تخصص‌ها", 1, WorkStatus.InProgress, [negar], -3, OrderPriority.High),
            ("شیرینی‌فروشی نمونه", "پست تکی", "پست کیک تولد", "کیک‌های سفارشی تولد", 1, WorkStatus.Todo, [maryam], 50, OrderPriority.Normal),
            ("گل‌فروشی نمونه", "استوری", "استوری دسته‌گل روز مادر", "پیش‌فروش دسته‌گل", 1, WorkStatus.InProgress, [negar], 26, OrderPriority.Normal),
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
            var order = Order.Create(number, clientId, svc.Id, svc.Name, svc.FormVersion, data, r.Title, "نمونه — سفارش آزمایشی", OrderSource.Admin, r.P,
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

    /// <summary>Sample leave and absence rows so the approvals, calendar and report pages aren't empty in the demo.</summary>
    private static async Task SeedLeaveAsync(AppDbContext db, List<(Guid Id, string Name)> users, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.LeaveRequests.AnyAsync(ct)) return;
        var types = await db.LeaveTypes.ToListAsync(ct);
        var annual = types.FirstOrDefault(t => t.Kind == Domain.Attendance.LeaveKind.Annual);
        var mission = types.FirstOrDefault(t => t.Kind == Domain.Attendance.LeaveKind.Mission);
        Guid? U(string p) => users.FirstOrDefault(u => u.Name.StartsWith(p, StringComparison.Ordinal)).Id is var id && id != Guid.Empty ? id : null;
        var manager = U("سارا");
        if (annual is null || mission is null || manager is null) return;
        var today = Karnama.Application.Attendance.Iran.Today(now);
        DateTimeOffset At(int days, int h) => Karnama.Application.Attendance.Iran.At(today.AddDays(days), new TimeOnly(h, 0));

        if (U("رضا") is { } reza)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(reza, annual, false, At(2, 0), At(4, 0), "سفر خانوادگی (نمونه)", null, manager.Value, true, now).Value!);
        if (U("نگار") is { } negar)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(negar, annual, true, At(1, 10), At(1, 13), "کار اداری (نمونه)", null, negar, false, now).Value!);
        if (U("علی") is { } ali)
            db.LeaveRequests.Add(Domain.Attendance.LeaveRequest.Create(ali, mission, false, At(3, 0), At(4, 0), "فیلمبرداری در محل مشتری (نمونه)", null, ali, false, now).Value!);
        if (U("محمد") is { } mohammad)
        {
            var a = Domain.Attendance.Absence.Detect(mohammad, today.AddDays(-1), At(-1, 9).AddMinutes(0), At(-1, 9).AddMinutes(15), 48);
            a.SubmitReason("بیمار بودم؛ گواهی را فردا می‌آورم (نمونه)", At(-1, 9), At(-1, 17), null);
            db.Absences.Add(a);
        }
        await db.SaveChangesAsync(ct);
    }
}
