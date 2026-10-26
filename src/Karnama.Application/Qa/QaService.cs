using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Assets;
using Karnama.Domain.Attendance;
using Karnama.Domain.Notify;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.Application.Qa;

public sealed record QaClockDto(DateTimeOffset Now, double OffsetMinutes);
public sealed record QaClockRequest(double? AdvanceMinutes, DateTimeOffset? Set, bool? Reset);
public sealed record QaJobResult(string Job, int Count);
public sealed record QaInvariant(string Name, bool Ok, int Violations, string Detail);

/// <summary>
/// QA tools (docs/PROMPT3.md Q0–Q1): move the clock, run the background jobs on demand (they normally run in the Worker),
/// and check the database invariants after every crowd run. Only mapped when Qa:Enabled (Development only).
/// </summary>
public sealed class QaService(IServiceProvider sp, IAppDbContext db, TimeProvider clock)
{
    public static readonly string[] Jobs = ["absences", "forgotten-shifts", "publish-tick", "package-expiry", "asset-alerts", "blog-schedule", "crm-followups", "outbox"];

    public QaClockDto Clock() => new(clock.GetUtcNow(), (sp.GetService<IQaClock>()?.Offset ?? TimeSpan.Zero).TotalMinutes);

    public QaClockDto MoveClock(QaClockRequest r)
    {
        var qa = sp.GetRequiredService<IQaClock>();
        if (r.Reset == true) qa.Reset();
        if (r.Set is { } at) qa.Set(at);
        if (r.AdvanceMinutes is { } m) qa.Advance(TimeSpan.FromMinutes(m));
        return Clock();
    }

    public async Task<QaJobResult?> RunJobAsync(string job, CancellationToken ct)
    {
        int n;
        switch (job)
        {
            case "absences": { var (d, e) = await sp.GetRequiredService<Attendance.LeaveService>().DetectAsync(ct); n = d + e; break; }
            case "forgotten-shifts": n = await sp.GetRequiredService<Attendance.AttendanceAdminService>().CloseForgottenAsync(ct); break;
            case "publish-tick": { var (d, o) = await sp.GetRequiredService<Publishing.PublishService>().TickAsync(ct); n = d + o; break; }
            case "package-expiry": n = await sp.GetRequiredService<Notify.PackageExpiryJob>().RunAsync(ct); break;
            case "asset-alerts": n = await sp.GetRequiredService<Assets.AssetService>().AlertsAsync(ct); break;
            case "blog-schedule": n = await sp.GetRequiredService<Blog.BlogService>().PublishDueAsync(ct); break;
            case "outbox": n = await sp.GetRequiredService<IQaOutbox>().DispatchOnceAsync(ct); break;
            case "crm-followups": n = await sp.GetRequiredService<Crm.CrmService>().RemindFollowUpsAsync(ct); break;
            default: return null;
        }
        return new QaJobResult(job, n);
    }

    /// <summary>The invariants of docs/PROMPT3.md §2 — every crowd run must end with all of them green.</summary>
    public async Task<IReadOnlyList<QaInvariant>> InvariantsAsync(CancellationToken ct)
    {
        var list = new List<QaInvariant>();
        void Add(string name, int bad, string detail) => list.Add(new QaInvariant(name, bad == 0, bad, detail));

        var timers = await db.WorkTimers.AsNoTracking().Where(t => t.StoppedAt == null).GroupBy(t => t.UserId).Where(g => g.Count() > 1).CountAsync(ct);
        Add("one-open-timer-per-user", timers, "users with more than one running timer");

        var shifts = await db.Shifts.AsNoTracking().Where(s => s.Status == ShiftStatus.Open).GroupBy(s => s.UserId).Where(g => g.Count() > 1).CountAsync(ct);
        Add("one-open-shift-per-user", shifts, "users with more than one open shift");

        var live = new[] { AssignmentState.PendingAcceptance, AssignmentState.Active, AssignmentState.ReturnSubmitted };
        var assets = await db.AssetAssignments.AsNoTracking().Where(a => live.Contains(a.State)).GroupBy(a => a.AssetId).Where(g => g.Count() > 1).CountAsync(ct);
        Add("one-active-handover-per-asset", assets, "assets handed to more than one person at once");

        var overQuota = 0;
        var packages = await db.ClientPackages.AsNoTracking().Select(p => new { p.Id, p.QuotasJson }).ToListAsync(ct);
        var used = await db.Orders.AsNoTracking().Where(o => o.ClientPackageId != null && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => new { o.ClientPackageId, o.ServiceTypeId }).Select(g => new { g.Key.ClientPackageId, g.Key.ServiceTypeId, N = g.Count() }).ToListAsync(ct);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var p in packages)
        {
            var quotas = JsonSerializer.Deserialize<List<Catalog.QuotaDto>>(p.QuotasJson, json) ?? [];
            foreach (var u in used.Where(u => u.ClientPackageId == p.Id))
                if (u.N > quotas.Where(q => q.ServiceTypeId == u.ServiceTypeId).Sum(q => q.Count)) overQuota++;
        }
        Add("quota-never-negative", overQuota, "package/service pairs that used more than their quota");

        var kiosk = await db.KioskCodeUses.AsNoTracking().GroupBy(k => new { k.Step, k.N }).Where(g => g.Count() > 1).CountAsync(ct);
        Add("kiosk-qr-used-once", kiosk, "kiosk codes used more than once");

        var dupNotices = await db.Notifications.AsNoTracking().Where(n => n.Kind.StartsWith("package")).GroupBy(n => new { n.UserId, n.EventKey }).Where(g => g.Count() > 1).CountAsync(ct);
        Add("no-duplicate-expiry-notice", dupNotices, "repeated package notices for the same person and event");

        var stuckBefore = clock.GetUtcNow().AddMinutes(-15);
        var stuck = await db.Outbox.AsNoTracking().CountAsync(o => o.Status == OutboxStatus.Pending && o.DueAt < stuckBefore && o.Attempts >= 5, ct);
        Add("outbox-not-stuck", stuck, "outbox messages pending for 15+ minutes after 5+ attempts");

        var orphanMedia = await db.MediaAssets.AsNoTracking().CountAsync(m => !db.Users.Any(u => u.Id == m.SenderId), ct);
        Add("media-has-linked-sender", orphanMedia, "files whose sender is not a known user");

        // Hours = events: every closed shift has its clock-in and clock-out events, and its minutes are never negative.
        var closed = db.Shifts.AsNoTracking().Where(s => s.Status == ShiftStatus.Closed);
        var noEvents = await closed.CountAsync(s => !db.AttendanceEvents.Any(e => e.ShiftId == s.Id && e.Kind == AttendanceEventKind.ClockIn)
                                                    || !db.AttendanceEvents.Any(e => e.ShiftId == s.Id && e.Kind == AttendanceEventKind.ClockOut), ct);
        var negative = await closed.CountAsync(s => s.ClockOutAt < s.ClockInAt, ct);
        Add("hours-match-events", noEvents + negative, $"{noEvents} closed shifts missing an in/out event, {negative} with negative length");
        return list;
    }
}
