using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Attendance;

/// <summary>What the device sends with a clock-in/out. Coordinates are taken only at that moment — never tracked.</summary>
public sealed record Evidence(string? Qr, double? Latitude, double? Longitude, double? Accuracy, bool? Mock, string? Bssid);

/// <summary>
/// <paramref name="ElapsedMs"/>: for an event queued offline, how long ago it happened by the device's monotonic clock
/// (not its wall clock, which a user can change). <paramref name="DeviceAt"/> is only compared, never trusted.
/// </summary>
public sealed record ClockRequest(string IdempotencyKey, DateTimeOffset? DeviceAt, long? ElapsedMs, Evidence? Evidence, string? Note);

public sealed record MethodResultDto(string Method, bool Passed, string Evidence);
public sealed record ShiftDto(Guid Id, Guid UserId, string? UserName, DateOnly Day, DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt, bool OnBreak,
    int BreakMinutes, int WorkedMinutes, Trust Trust, bool NeedsReview, string? ReviewReason, bool Offline, int LateMinutes, int EarlyLeaveMinutes,
    string? EarlyLeaveReason, IReadOnlyList<MethodResultDto> Methods);
public sealed record TimerDto(Guid WorkItemId, string Title, string ClientName, DateTimeOffset StartedAt, int MinutesToday);
public sealed record TodayDto(ShiftDto? Shift, TimerDto? Timer, int WorkedMinutesToday, int WorkedMinutesWeek, ScheduleDay? Schedule, bool IsHoliday,
    string Rule, IReadOnlyList<string> Methods, IReadOnlyList<ShiftDto> RecentShifts);
public sealed record PresenceDto(Guid UserId, string Name, string State, DateTimeOffset? Since, int WorkedMinutes, int LateMinutes, Trust? Trust, bool NeedsReview,
    string? TimerTitle, int? TimerMinutes);
public sealed record CorrectionRequest(DateOnly Day, DateTimeOffset? ClockIn, DateTimeOffset? ClockOut, string Reason);
public sealed record CorrectionDto(Guid Id, Guid UserId, string UserName, DateOnly Day, DateTimeOffset? ClockIn, DateTimeOffset? ClockOut, string Reason,
    CorrectionStatus Status, string? DecisionNote, DateTimeOffset CreatedAt);
public sealed record DecisionRequest(bool Approve, string? Note);
public sealed record KioskCodeDto(string Code, int SecondsLeft);

public sealed class ClockRequestValidator : AbstractValidator<ClockRequest>
{
    public ClockRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9_-]+$").WithMessage("شناسه‌ی درخواست معتبر نیست.");
        RuleFor(x => x.ElapsedMs).InclusiveBetween(0, 72L * 3600 * 1000).When(x => x.ElapsedMs is not null).WithMessage("زمان ثبت آفلاین معتبر نیست.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>
/// Clock in/out, breaks, the one running work timer, corrections and the live presence board (docs/PROMPT.md §6.6, PROMPT2 §2).
/// The server clock is the reference; a failed verification never blocks — it marks the shift "needs review".
/// </summary>
public sealed class AttendanceService(IAppDbContext db, ICurrentUser me, IRequestContext request, AttendanceSettingsStore settings, IAuditWriter audit,
    TimeProvider clock, IChangeFeed changes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int QrStepSeconds = 30;
    private Guid Me => me.UserId ?? Guid.Empty;

    // ---------------- my day ----------------

    public async Task<TodayDto> TodayAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = Iran.Today(now);
        var shifts = await db.Shifts.AsNoTracking().Where(s => s.UserId == Me && s.Day >= today.AddDays(-6)).OrderByDescending(s => s.ClockInAt).ToListAsync(ct);
        var open = shifts.FirstOrDefault(s => s.Status == ShiftStatus.Open);
        var schedule = await ScheduleForAsync(Me, ct);
        var holiday = await db.Holidays.AnyAsync(h => h.Date == today, ct);
        var rule = await RuleForAsync(Me, ct);
        var timer = await TimerDtoAsync(Me, now, ct);
        var weekStart = today.AddDays(-WorkSchedule.DayIndex(today));
        return new TodayDto(open is null ? null : ToDto(open, null, now), timer,
            shifts.Where(s => s.Day == today).Sum(s => s.WorkedMinutes(now)), shifts.Where(s => s.Day >= weekStart).Sum(s => s.WorkedMinutes(now)),
            schedule?.For(today), holiday, rule.Text, rule.Rule.Names().Distinct().ToList(), shifts.Take(14).Select(s => ToDto(s, null, now)).ToList());
    }

    public async Task<Result<TodayDto>> ClockInAsync(ClockRequest r, CancellationToken ct)
    {
        if (await DuplicateAsync(r.IdempotencyKey, ct)) return await TodayAsync(ct);
        var now = clock.GetUtcNow();
        var (at, offline, timeIssue) = await WhenAsync(r, now, ct);
        if (await db.Shifts.AnyAsync(s => s.UserId == Me && s.Status == ShiftStatus.Open, ct))
            return Error.Conflict("attendance.alreadyIn", "ورود شما از قبل ثبت شده است.");

        var check = await VerifyAsync(r.Evidence, now, ct);
        var day = Iran.Today(at);
        var sched = await ScheduleForAsync(Me, ct);
        var late = 0;
        if (sched?.For(day) is { } today && !sched.Flexible)
        {
            var minutes = (int)(at - Iran.At(day, today.Start)).TotalMinutes;
            late = minutes > sched.GraceMinutes ? minutes : 0;
        }
        var reasons = new[] { check.Reason, timeIssue }.Where(x => x is not null).ToList();
        var shift = Shift.Open(Me, day, at, check.Results, check.Trust, reasons.Count > 0, reasons.Count > 0 ? string.Join(" | ", reasons) : null, offline, late);
        db.Shifts.Add(shift);
        // Detected as absent, then arrived after all: it's a late arrival of exactly this many minutes.
        foreach (var absence in await db.Absences.Where(a => a.UserId == Me && a.Day == day).ToListAsync(ct)) absence.BecomeLate(at);
        db.AttendanceEvents.Add(Event(shift.Id, AttendanceEventKind.ClockIn, at, now, r, check.Results, check.Trust));
        audit.Add("attendance.clockIn", nameof(Shift), shift.Id.ToString(), new { trust = check.Trust, offline, late });
        changes.Record("presence", Me, null, Me, null, "clockIn");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await TodayAsync(ct);
    }

    public async Task<Result<TodayDto>> ClockOutAsync(ClockRequest r, CancellationToken ct)
    {
        if (await DuplicateAsync(r.IdempotencyKey, ct)) return await TodayAsync(ct);
        var now = clock.GetUtcNow();
        var (at, offline, timeIssue) = await WhenAsync(r, now, ct);
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.UserId == Me && s.Status == ShiftStatus.Open, ct);
        if (shift is null) return Error.Conflict("attendance.notIn", "ورودی ثبت نشده است.");

        var check = await VerifyAsync(r.Evidence, now, ct);
        var sched = await ScheduleForAsync(Me, ct);
        var early = 0;
        if (sched?.For(shift.Day) is { } day && !sched.Flexible)
        {
            var end = Iran.At(shift.Day, day.End);
            early = at < end ? (int)(end - at).TotalMinutes : 0;
        }
        var reasons = new[] { check.Reason, timeIssue }.Where(x => x is not null).ToList();
        var closed = shift.Close(at, check.Results, check.Trust, reasons.Count > 0, reasons.Count > 0 ? string.Join(" | ", reasons) : null, offline, early, r.Note);
        if (closed.IsFailure) return closed.Error!;
        await StopTimerCoreAsync(at, ct);   // leaving the shift stops the timer
        db.AttendanceEvents.Add(Event(shift.Id, AttendanceEventKind.ClockOut, at, now, r, check.Results, check.Trust));
        audit.Add("attendance.clockOut", nameof(Shift), shift.Id.ToString(), new { trust = check.Trust, offline, early });
        changes.Record("presence", Me, null, Me, null, "clockOut");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await TodayAsync(ct);
    }

    public async Task<Result<TodayDto>> BreakAsync(bool start, ClockRequest r, CancellationToken ct)
    {
        if (await DuplicateAsync(r.IdempotencyKey, ct)) return await TodayAsync(ct);
        var now = clock.GetUtcNow();
        var (at, _, _) = await WhenAsync(r, now, ct);
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.UserId == Me && s.Status == ShiftStatus.Open, ct);
        if (shift is null) return Error.Conflict("attendance.notIn", "اول ورود بزنید.");
        var res = start ? shift.StartBreak(at) : shift.EndBreak(at);
        if (res.IsFailure) return res.Error!;
        if (start) await StopTimerCoreAsync(at, ct);
        db.AttendanceEvents.Add(Event(shift.Id, start ? AttendanceEventKind.BreakStart : AttendanceEventKind.BreakEnd, at, now, r, [], Trust.Green));
        changes.Record("presence", Me, null, Me, null, start ? "breakStart" : "breakEnd");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await TodayAsync(ct);
    }

    // ---------------- work timer ----------------

    public async Task<Result<TodayDto>> StartTimerAsync(Guid workItemId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var item = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, ct);
        if (item is null || item.AssigneeId != Me) return Error.NotFound("work.notFound", "این کار به شما سپرده نشده است.");
        if (!item.IsOpen) return Error.Conflict("work.closed", "این کار بسته شده است.");
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.UserId == Me && s.Status == ShiftStatus.Open, ct);
        if (shift is null)
        {
            var s = await settings.GetAsync(ct);
            if (!s.AutoClockInOnTimer) return Error.Conflict("attendance.notIn", "تایمر فقط داخل شیفت کار می‌کند؛ اول ورود بزنید.");
            var check = await VerifyAsync(null, now, ct);
            shift = Shift.Open(Me, Iran.Today(now), now, check.Results, check.Trust, check.Reason is not null, check.Reason, false, 0);
            db.Shifts.Add(shift);
        }
        if (shift.OnBreak) return Error.Conflict("attendance.onBreak", "اول استراحت را تمام کنید.");
        await StopTimerCoreAsync(now, ct);   // one timer at a time: a new one pauses the previous
        // Saved first: the "one running timer" unique index must see the old one stopped before the new insert.
        await db.SaveChangesAsync(ct);
        db.WorkTimers.Add(new WorkTimer(Me, workItemId, shift.Id, now));
        changes.Record("presence", Me, null, Me, null, "timer");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await TodayAsync(ct);
    }

    public async Task<TodayDto> StopTimerAsync(CancellationToken ct)
    {
        await StopTimerCoreAsync(clock.GetUtcNow(), ct);
        changes.Record("presence", Me, null, Me, null, "timer");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await TodayAsync(ct);
    }

    private async Task StopTimerCoreAsync(DateTimeOffset at, CancellationToken ct)
    {
        foreach (var t in await db.WorkTimers.Where(t => t.UserId == Me && t.StoppedAt == null).ToListAsync(ct)) t.Stop(at);
    }

    private async Task<TimerDto?> TimerDtoAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var t = await db.WorkTimers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.StoppedAt == null, ct);
        if (t is null) return null;
        var w = await db.WorkItems.AsNoTracking().Where(x => x.Id == t.WorkItemId).Select(x => new { x.Title, x.ClientId }).FirstOrDefaultAsync(ct);
        var client = w is null ? null : await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == w.ClientId).Select(c => c.BusinessName).FirstOrDefaultAsync(ct);
        var dayStart = Iran.At(Iran.Today(now), TimeOnly.MinValue);
        var earlier = await db.WorkTimers.AsNoTracking().Where(x => x.UserId == userId && x.WorkItemId == t.WorkItemId && x.StartedAt >= dayStart && x.StoppedAt != null).SumAsync(x => x.Minutes, ct);
        return new TimerDto(t.WorkItemId, w?.Title ?? "—", client ?? "—", t.StartedAt, earlier + (int)(now - t.StartedAt).TotalMinutes);
    }

    /// <summary>Minutes spent on a work item (all people), for the card.</summary>
    public async Task<int> MinutesOnAsync(Guid workItemId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var timers = await db.WorkTimers.AsNoTracking().Where(t => t.WorkItemId == workItemId).ToListAsync(ct);
        return timers.Sum(t => t.StoppedAt is null ? (int)(now - t.StartedAt).TotalMinutes : t.Minutes);
    }

    // ---------------- verification (PROMPT2 §2) ----------------

    private sealed record Check(List<MethodResult> Results, Trust Trust, string? Reason);

    private async Task<(PolicyRule Rule, string Text)> RuleForAsync(Guid userId, CancellationToken ct)
    {
        var roleIds = await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync(ct);
        var policies = await db.AttendancePolicies.AsNoTracking().ToListAsync(ct);
        var p = policies.FirstOrDefault(x => x.UserIds.Contains(userId))
                ?? policies.FirstOrDefault(x => x.RoleIds.Any(roleIds.Contains))
                ?? policies.FirstOrDefault(x => x.IsDefault);
        var text = p?.Rule ?? "ManagerApproval";
        return (PolicyRule.Parse(text).Value!, text);
    }

    private async Task<Check> VerifyAsync(Evidence? e, DateTimeOffset now, CancellationToken ct)
    {
        var (rule, text) = await RuleForAsync(Me, ct);
        var s = await settings.GetAsync(ct);
        var results = new List<MethodResult>();
        foreach (var m in rule.Names().Distinct())
        {
            results.Add(m switch
            {
                "OfficeNetwork" => AttendanceSettingsStore.InNetworks(request.RemoteIp, s.OfficeNetworks)
                    ? new MethodResult(m, true, "اینترنت دفتر ✓")
                    : new MethodResult(m, false, "از اینترنت دفتر نبود"),
                "RotatingQr" => await CheckQrAsync(e?.Qr, now, ct),
                "Geofence" => await CheckPlaceAsync(e, ct),
                "ManagerApproval" => new MethodResult(m, false, "نیاز به تأیید مدیر"),
                _ => new MethodResult(m, false, "این روش در این نسخه فعال نیست"),
            });
        }
        var passed = rule.Evaluate(name => results.Any(r => r.Method == name && r.Passed));
        var fraud = results.Any(r => r.Fraud);
        var trust = fraud ? Trust.Red : passed ? Trust.Green : Trust.Yellow;
        string? reason = fraud ? "شواهد مشکوک: " + string.Join("، ", results.Where(r => r.Fraud).Select(r => r.Evidence))
            : passed ? null : $"قانون ورود ({text}) برقرار نشد: " + string.Join("، ", results.Where(r => !r.Passed).Select(r => r.Evidence));
        return new Check(results, trust, reason);
    }

    private async Task<MethodResult> CheckPlaceAsync(Evidence? e, CancellationToken ct)
    {
        if (e?.Latitude is not { } lat || e.Longitude is not { } lng) return new MethodResult("Geofence", false, "موقعیت ارسال نشد");
        if (e.Mock == true) return new MethodResult("Geofence", false, "مکان جعلی", Fraud: true);
        if (e.Accuracy is > 200) return new MethodResult("Geofence", false, $"دقت موقعیت کم بود ({e.Accuracy:0} متر)");
        var places = await db.AttendancePlaces.AsNoTracking().ToListAsync(ct);
        var nearest = places.Select(p => (p, d: p.DistanceMeters(lat, lng))).OrderBy(x => x.d).FirstOrDefault();
        if (nearest.p is null) return new MethodResult("Geofence", false, "هنوز مکانی تعریف نشده");
        return nearest.d <= nearest.p.RadiusMeters
            ? new MethodResult("Geofence", true, $"{nearest.d:0} متر از «{nearest.p.Name}» ✓")
            : new MethodResult("Geofence", false, $"{nearest.d:0} متر از «{nearest.p.Name}» (بیرون از محدوده)");
    }

    /// <summary>QR = "k1.{step}.{n}.{mac}": valid for the current or previous 30-second step, each (step, n) usable once.</summary>
    private async Task<MethodResult> CheckQrAsync(string? qr, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qr)) return new MethodResult("RotatingQr", false, "QR اسکن نشد");
        var parts = qr.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != "k1" || !long.TryParse(parts[1], out var step) || !int.TryParse(parts[2], out var n) || n is < 0 or > 10_000)
            return new MethodResult("RotatingQr", false, "QR نامعتبر", Fraud: true);
        var key = await settings.KioskKeyAsync(ct);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(key, step, n)), Encoding.ASCII.GetBytes(parts[3])))
            return new MethodResult("RotatingQr", false, "QR جعلی", Fraud: true);
        var current = now.ToUnixTimeSeconds() / QrStepSeconds;
        if (step < current - 1 || step > current) return new MethodResult("RotatingQr", false, "QR منقضی شده (عکس قدیمی؟)", Fraud: true);
        // Single use: the insert fails for the second scan of the same code.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO kiosk_code_uses (\"Step\", \"N\", \"UserId\", \"At\") VALUES ({step}, {n}, {Me}, {now}) ON CONFLICT DO NOTHING", ct);
        return inserted == 1 ? new MethodResult("RotatingQr", true, "QR دفتر ✓") : new MethodResult("RotatingQr", false, "QR تکراری", Fraud: true);
    }

    private static string Mac(byte[] key, long step, int n) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{step}.{n}")))[..16];

    /// <summary>The office kiosk shows this; a fresh code appears as soon as one is used.</summary>
    public async Task<Result<KioskCodeDto>> KioskCodeAsync(CancellationToken ct)
    {
        // A kiosk login used from home must not be able to mint valid office codes.
        var networks = (await settings.GetAsync(ct)).OfficeNetworks;
        if (networks.Count > 0 && !AttendanceSettingsStore.InNetworks(request.RemoteIp, networks))
            return Error.Forbidden("kiosk.officeOnly", "صفحه‌ی QR فقط از اینترنت دفتر باز می‌شود.");
        var now = clock.GetUtcNow();
        var step = now.ToUnixTimeSeconds() / QrStepSeconds;
        var used = await db.KioskCodeUses.CountAsync(u => u.Step == step, ct);
        var key = await settings.KioskKeyAsync(ct);
        return new KioskCodeDto($"k1.{step}.{used}.{Mac(key, step, used)}", (int)(QrStepSeconds - now.ToUnixTimeSeconds() % QrStepSeconds));
    }

    // ---------------- helpers ----------------

    private async Task<bool> DuplicateAsync(string key, CancellationToken ct) =>
        await db.AttendanceEvents.AnyAsync(e => e.UserId == Me && e.IdempotencyKey == key, ct);

    /// <summary>
    /// Server time minus the elapsed time the device measured (offline queue). A device wall clock far from that is a
    /// changed phone clock → review. Returns (when, wasOffline, problem).
    /// </summary>
    private async Task<(DateTimeOffset At, bool Offline, string? Issue)> WhenAsync(ClockRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var elapsed = TimeSpan.FromMilliseconds(Math.Clamp(r.ElapsedMs ?? 0, 0, 72L * 3600 * 1000));
        var at = now - elapsed;
        var offline = elapsed > TimeSpan.FromMinutes(2);
        string? issue = null;
        // The elapsed time comes from the device, so an offline event is never trusted on its own: a manager confirms it.
        // (Otherwise "I was offline for 2 hours" would turn an 11:00 arrival into a green 09:00.)
        if (offline) issue = $"ثبت آفلاین ({(int)elapsed.TotalMinutes} دقیقه قبل) — مدیر تأیید کند";
        if (r.DeviceAt is { } d && Math.Abs((d - at).TotalMinutes) > s.OfflineToleranceMinutes)
            issue = $"ساعت گوشی {Math.Abs((d - at).TotalMinutes):0} دقیقه با ساعت سرور فرق داشت";
        // Never before the person's previous event: no overlapping or reordered shifts.
        var uid = Me;
        var last = await db.AttendanceEvents.Where(e => e.UserId == uid).MaxAsync(e => (DateTimeOffset?)e.At, ct);
        if (last is { } l && at < l) at = l;
        return (at, offline, issue);
    }

    private AttendanceEvent Event(Guid shiftId, AttendanceEventKind kind, DateTimeOffset at, DateTimeOffset now, ClockRequest r, List<MethodResult> results, Trust trust) =>
        new(Me, shiftId, kind, at, now, r.DeviceAt, r.IdempotencyKey, JsonSerializer.Serialize(results, Json), trust, r.Note, Me);

    private async Task<WorkSchedule?> ScheduleForAsync(Guid userId, CancellationToken ct) =>
        await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
        ?? await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == null, ct);

    internal static ShiftDto ToDto(Shift s, string? name, DateTimeOffset now) => new(s.Id, s.UserId, name, s.Day, s.ClockInAt, s.ClockOutAt, s.OnBreak, s.BreakMinutes,
        s.WorkedMinutes(now), s.Trust, s.NeedsReview, s.ReviewReason, s.Offline, s.LateMinutes, s.EarlyLeaveMinutes, s.EarlyLeaveReason,
        s.Methods.Select(m => new MethodResultDto(m.Method, m.Passed, m.Evidence)).ToList());
}
