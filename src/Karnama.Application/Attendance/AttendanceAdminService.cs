using Karnama.Application.Abstractions;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Attendance;

public sealed record ScheduleDto(Guid Id, Guid? UserId, IReadOnlyList<ScheduleDay> Days, int BreakMinutes, int GraceMinutes, bool Flexible, int WeeklyHours, uint Version);
public sealed record UpsertScheduleRequest(Guid? UserId, List<ScheduleDay> Days, int BreakMinutes, int GraceMinutes, bool Flexible, int WeeklyHours);
public sealed record PolicyDto(Guid Id, string Name, string Rule, IReadOnlyList<Guid> RoleIds, IReadOnlyList<Guid> UserIds, bool IsDefault, uint Version);
public sealed record UpsertPolicyRequest(string Name, string Rule, List<Guid>? RoleIds, List<Guid>? UserIds, bool IsDefault);
public sealed record PlaceDto(Guid Id, string Name, double Latitude, double Longitude, int RadiusMeters);
public sealed record UpsertPlaceRequest(string Name, double Latitude, double Longitude, int RadiusMeters);
public sealed record HolidayDto(Guid Id, DateOnly Date, string Name);
public sealed record UpsertHolidayRequest(DateOnly Date, string Name);
public sealed record AttendanceSetupDto(AttendanceSettingsDto Settings, IReadOnlyList<ScheduleDto> Schedules, IReadOnlyList<PolicyDto> Policies,
    IReadOnlyList<PlaceDto> Places, IReadOnlyList<HolidayDto> Holidays, string? YourIp);

/// <summary>Managers: live presence board, shifts and review, corrections, and the setup (schedules, rules, places, holidays).</summary>
public sealed class AttendanceAdminService(IAppDbContext db, ICurrentUser me, IRequestContext request, AttendanceSettingsStore store, IAuditWriter audit,
    TimeProvider clock, IChangeFeed changes, Notify.Notifier notifier)
{
    private Guid Me => me.UserId ?? Guid.Empty;
    private bool SeesAll => me.Has(Permissions.AttendanceView, PermissionScope.All);

    // ---- live presence ----
    public async Task<Result<IReadOnlyList<PresenceDto>>> LiveAsync(CancellationToken ct)
    {
        if (!SeesAll) return Error.Forbidden();
        var now = clock.GetUtcNow();
        var today = Iran.Today(now);
        var staff = await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive && (!u.IsDemo || me.IsDemo)).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var shifts = await db.Shifts.AsNoTracking().Where(s => s.Day == today || s.Status == ShiftStatus.Open).ToListAsync(ct);
        var timers = await db.WorkTimers.AsNoTracking().Where(t => t.StoppedAt == null).ToListAsync(ct);
        var itemIds = timers.Select(t => t.WorkItemId).ToList();
        var titles = await db.WorkItems.AsNoTracking().Where(w => itemIds.Contains(w.Id)).Select(w => new { w.Id, w.Title }).ToDictionaryAsync(w => w.Id, w => w.Title, ct);
        var schedules = await db.WorkSchedules.AsNoTracking().ToListAsync(ct);
        var fallback = schedules.FirstOrDefault(s => s.UserId == null);
        var holiday = await db.Holidays.AnyAsync(h => h.Date == today, ct);

        var list = staff.Select(u =>
        {
            var mine = shifts.Where(s => s.UserId == u.Id).OrderByDescending(s => s.ClockInAt).ToList();
            var open = mine.FirstOrDefault(s => s.Status == ShiftStatus.Open);
            var timer = timers.FirstOrDefault(t => t.UserId == u.Id);
            var sched = (schedules.FirstOrDefault(s => s.UserId == u.Id) ?? fallback)?.For(today);
            var expected = !holiday && sched is not null;
            var lateNow = expected && open is null && mine.Count == 0 && now > Iran.At(today, sched!.Start).AddMinutes((schedules.FirstOrDefault(s => s.UserId == u.Id) ?? fallback)!.GraceMinutes);
            var state = open is not null ? (open.OnBreak ? "break" : "in") : mine.Count > 0 ? "out" : lateNow ? "absent" : expected ? "expected" : "off";
            return new PresenceDto(u.Id, u.DisplayName, state, open?.OnBreak == true ? open.BreakStartedAt : open?.ClockInAt ?? mine.FirstOrDefault()?.ClockOutAt,
                mine.Sum(s => s.WorkedMinutes(now)), mine.Sum(s => s.LateMinutes), open?.Trust ?? mine.FirstOrDefault()?.Trust, mine.Any(s => s.NeedsReview),
                timer is null ? null : titles.GetValueOrDefault(timer.WorkItemId), timer is null ? null : (int)(now - timer.StartedAt).TotalMinutes);
        }).OrderBy(p => p.State switch { "in" => 0, "break" => 1, "absent" => 2, "expected" => 3, "out" => 4, _ => 5 }).ThenBy(p => p.Name).ToList();
        return list;
    }

    // ---- shifts ----
    public async Task<Result<IReadOnlyList<ShiftDto>>> ShiftsAsync(Guid? userId, DateOnly from, DateOnly to, bool needsReviewOnly, CancellationToken ct)
    {
        if (!SeesAll) userId = Me;
        if (to < from || to.DayNumber - from.DayNumber > 62) return Error.Validation("attendance.range", "بازه باید حداکثر ۶۲ روز باشد.");
        var q = db.Shifts.AsNoTracking().Where(s => s.Day >= from && s.Day <= to);
        if (userId is { } uid) q = q.Where(s => s.UserId == uid);
        if (needsReviewOnly) q = q.Where(s => s.NeedsReview);
        var rows = await q.OrderByDescending(s => s.ClockInAt).Take(1000).ToListAsync(ct);
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var now = clock.GetUtcNow();
        return rows.Select(s => AttendanceService.ToDto(s, names.GetValueOrDefault(s.UserId), now)).ToList();
    }

    public async Task<Result> ReviewAsync(Guid shiftId, DecisionRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.AttendanceApprove, PermissionScope.All) && !me.Has(Permissions.AttendanceCorrect, PermissionScope.All)) return Error.Forbidden();
        var s = await db.Shifts.FirstOrDefaultAsync(x => x.Id == shiftId, ct);
        if (s is null) return Error.NotFound();
        if (s.UserId == Me) return Error.Forbidden("attendance.selfReview", "شیفت خودتان را مدیر دیگری باید بررسی کند.");
        s.Review(Me, r.Approve);
        audit.Add(r.Approve ? "attendance.reviewAccepted" : "attendance.reviewRejected", nameof(Shift), shiftId.ToString(), new { r.Note });
        changes.Record("presence", s.UserId, null, s.UserId, null, "review");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    // ---- corrections ----
    public async Task<Result<CorrectionDto>> RequestCorrectionAsync(CorrectionRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var shift = await db.Shifts.AsNoTracking().Where(s => s.UserId == Me && s.Day == r.Day).OrderByDescending(s => s.ClockInAt).FirstOrDefaultAsync(ct);
        var c = AttendanceCorrection.Request(Me, shift?.Id, r.Day, r.ClockIn, r.ClockOut, r.Reason, now);
        if (c.IsFailure) return c.Error!;
        db.AttendanceCorrections.Add(c.Value!);
        audit.Add("attendance.correctionRequested", nameof(AttendanceCorrection), c.Value!.Id.ToString(), new { r.Day });
        changes.Record("presence", Me, null, Me, null, "correction");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await CorrectionDtoAsync(c.Value!, ct);
    }

    public async Task<IReadOnlyList<CorrectionDto>> CorrectionsAsync(bool pendingOnly, CancellationToken ct)
    {
        var q = db.AttendanceCorrections.AsNoTracking();
        if (!me.Has(Permissions.AttendanceCorrect, PermissionScope.All)) q = q.Where(c => c.UserId == Me);
        if (pendingOnly) q = q.Where(c => c.Status == CorrectionStatus.Pending);
        var rows = await q.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct);
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(c => new CorrectionDto(c.Id, c.UserId, names.GetValueOrDefault(c.UserId) ?? "—", c.Day, c.ClockIn, c.ClockOut, c.Reason, c.Status, c.DecisionNote, c.CreatedAt)).ToList();
    }

    /// <summary>
    /// Approving appends a correction event and updates (or creates) the shift; nothing is deleted. Lateness and early
    /// leave are recomputed from the schedule for the corrected times. A shift the correction creates or closes also
    /// gets its clock-in / clock-out events (actor = the approver), so hours always match the event log.
    /// </summary>
    public async Task<Result<CorrectionDto>> DecideAsync(Guid id, DecisionRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.AttendanceCorrect, PermissionScope.All)) return Error.Forbidden();
        var c = await db.AttendanceCorrections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound();
        if (c.UserId == Me) return Error.Forbidden("corrections.self", "درخواست اصلاح خودتان را مدیر دیگری باید تأیید کند.");
        var now = clock.GetUtcNow();
        var d = c.Decide(Me, r.Approve, r.Note, now);
        if (d.IsFailure) return d.Error!;
        if (r.Approve)
        {
            var shift = c.ShiftId is { } sid ? await db.Shifts.FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
            if (shift is null)
            {
                if (c.ClockIn is not { } inAt) return Error.Validation("corrections.needIn", "برای روزی که ورود ندارد، ساعت ورود لازم است.");
                // Without a clock-out the new shift would stay open: never a second open shift next to the current one.
                if (c.ClockOut is null && await db.Shifts.AnyAsync(s => s.UserId == c.UserId && s.Status == ShiftStatus.Open, ct))
                    return Error.Conflict("corrections.openShift",
                        "این همکار الان شیفت باز دارد؛ برای روزی که شیفت ندارد، ساعت خروج را هم در درخواست اصلاح بخواهید یا بعد از خروج او تأیید کنید.");
                shift = Shift.Open(c.UserId, c.Day, inAt, [new MethodResult("ManagerApproval", true, "اصلاح تأییدشده")], Trust.Green, false, null, false, 0);
                db.Shifts.Add(shift);
            }
            var sched = await ScheduleForAsync(c.UserId, ct);
            var applied = shift.Correct(c.ClockIn, c.ClockOut,
                c.ClockIn is { } ci ? AttendanceService.LateMinutes(sched, shift.Day, ci) : null,
                c.ClockOut is { } co ? AttendanceService.EarlyLeaveMinutes(sched, shift.Day, co) : null);
            if (applied.IsFailure) return applied.Error!;
            if (c.ClockIn is { } corrected)
                foreach (var absence in await db.Absences.Where(a => a.UserId == c.UserId && a.Day == shift.Day).ToListAsync(ct))
                {
                    absence.BecomeLate(corrected);   // a whole missed day was in fact worked (late, if after the grace)
                    absence.RecomputeLate(corrected);
                }

            var json = System.Text.Json.JsonSerializer.Serialize(new { c.ClockIn, c.ClockOut });
            db.AttendanceEvents.Add(new AttendanceEvent(c.UserId, shift.Id, AttendanceEventKind.Correction, now, now, null, $"corr-{c.Id:N}", json, Trust.Green, c.Reason, Me));
            var kinds = await db.AttendanceEvents.AsNoTracking().Where(e => e.ShiftId == shift.Id).Select(e => e.Kind).ToListAsync(ct);
            if (!kinds.Contains(AttendanceEventKind.ClockIn))
                db.AttendanceEvents.Add(new AttendanceEvent(c.UserId, shift.Id, AttendanceEventKind.ClockIn, shift.ClockInAt, now, null, $"corr-in-{c.Id:N}", json, Trust.Green,
                    "اصلاح تأییدشده", Me));
            if (shift.Status == ShiftStatus.Closed && !kinds.Contains(AttendanceEventKind.ClockOut))
                db.AttendanceEvents.Add(new AttendanceEvent(c.UserId, shift.Id, AttendanceEventKind.ClockOut, shift.ClockOutAt!.Value, now, null, $"corr-out-{c.Id:N}", json,
                    Trust.Green, "اصلاح تأییدشده", Me));
        }
        audit.Add(r.Approve ? "attendance.correctionApproved" : "attendance.correctionRejected", nameof(AttendanceCorrection), id.ToString(), new { c.UserId, c.Day });
        changes.Record("presence", c.UserId, null, c.UserId, null, "correction");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await CorrectionDtoAsync(c, ct);
    }

    private async Task<WorkSchedule?> ScheduleForAsync(Guid userId, CancellationToken ct) =>
        await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
        ?? await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == null, ct);

    private async Task<CorrectionDto> CorrectionDtoAsync(AttendanceCorrection c, CancellationToken ct)
    {
        var name = await db.Users.IgnoreQueryFilters().Where(u => u.Id == c.UserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new CorrectionDto(c.Id, c.UserId, name ?? "—", c.Day, c.ClockIn, c.ClockOut, c.Reason, c.Status, c.DecisionNote, c.CreatedAt);
    }

    // ---- setup (attendance.policies.manage) ----
    public async Task<AttendanceSetupDto> SetupAsync(CancellationToken ct)
    {
        var schedules = await db.WorkSchedules.AsNoTracking().ToListAsync(ct);
        var policies = await db.AttendancePolicies.AsNoTracking().ToListAsync(ct);
        var places = await db.AttendancePlaces.AsNoTracking().ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= Iran.Today(clock.GetUtcNow()).AddDays(-30)).OrderBy(h => h.Date).ToListAsync(ct);
        return new AttendanceSetupDto(await store.GetAsync(ct),
            schedules.Select(s => new ScheduleDto(s.Id, s.UserId, s.Days, s.BreakMinutes, s.GraceMinutes, s.Flexible, s.WeeklyHours, s.Version)).ToList(),
            policies.Select(p => new PolicyDto(p.Id, p.Name, p.Rule, p.RoleIds, p.UserIds, p.IsDefault, p.Version)).ToList(),
            places.Select(p => new PlaceDto(p.Id, p.Name, p.Latitude, p.Longitude, p.RadiusMeters)).ToList(),
            holidays.Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToList(),
            request.RemoteIp?.ToString());
    }

    public async Task<Result> SaveSettingsAsync(AttendanceSettingsDto s, CancellationToken ct)
    {
        if (s.OfficeNetworks.Count > 20 || s.OfficeNetworks.Any(n => !System.Net.IPNetwork.TryParse(n.Contains('/') ? n : $"{n}/{(n.Contains(':') ? 128 : 32)}", out _)))
            return Error.Validation("attendance.networks", "آدرس IP دفتر معتبر نیست (مثل 5.160.10.20 یا 5.160.10.0/24).");
        if (s.OfflineToleranceMinutes is < 1 or > 120 || s.ForgottenAfterHours is < 1 or > 24 || s.SelfieRetentionDays is < 1 or > 365 || s.LocationRetentionDays is < 1 or > 365)
            return Error.Validation("attendance.settings", "یکی از عددها خارج از محدوده است.");
        await store.SaveAsync(s with { OfficeNetworks = s.OfficeNetworks.Select(x => x.Trim()).Distinct().ToList() }, Me, clock.GetUtcNow(), ct);
        audit.Add("attendance.settings", "SiteSetting", AttendanceSettingsStore.Key, new { networks = s.OfficeNetworks.Count });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ScheduleDto>> SaveScheduleAsync(UpsertScheduleRequest r, CancellationToken ct)
    {
        var s = await db.WorkSchedules.FirstOrDefaultAsync(x => x.UserId == r.UserId, ct);
        if (s is null)
        {
            var created = WorkSchedule.Create(r.UserId, r.Days, r.BreakMinutes, r.GraceMinutes, r.Flexible, r.WeeklyHours);
            if (created.IsFailure) return created.Error!;
            s = created.Value!;
            db.WorkSchedules.Add(s);
        }
        else
        {
            var u = s.Update(r.Days, r.BreakMinutes, r.GraceMinutes, r.Flexible, r.WeeklyHours);
            if (u.IsFailure) return u.Error!;
            db.MarkModified(s);
        }
        audit.Add("attendance.schedule", nameof(WorkSchedule), s.Id.ToString(), new { r.UserId });
        await db.SaveChangesAsync(ct);
        return new ScheduleDto(s.Id, s.UserId, s.Days, s.BreakMinutes, s.GraceMinutes, s.Flexible, s.WeeklyHours, s.Version);
    }

    public async Task<Result<PolicyDto>> SavePolicyAsync(Guid? id, UpsertPolicyRequest r, CancellationToken ct)
    {
        AttendancePolicy p;
        if (id is { } pid)
        {
            p = (await db.AttendancePolicies.FirstOrDefaultAsync(x => x.Id == pid, ct))!;
            if (p is null) return Error.NotFound();
            var u = p.Update(r.Name, r.Rule, r.RoleIds ?? [], r.UserIds ?? [], r.IsDefault);
            if (u.IsFailure) return u.Error!;
            db.MarkModified(p);
        }
        else
        {
            var c = AttendancePolicy.Create(r.Name, r.Rule, r.RoleIds ?? [], r.UserIds ?? [], r.IsDefault);
            if (c.IsFailure) return c.Error!;
            p = c.Value!;
            db.AttendancePolicies.Add(p);
        }
        if (r.IsDefault)
            foreach (var other in await db.AttendancePolicies.Where(x => x.Id != p.Id && x.IsDefault).ToListAsync(ct))
                other.Update(other.Name, other.Rule, other.RoleIds, other.UserIds, false);
        audit.Add("attendance.policy", nameof(AttendancePolicy), p.Id.ToString(), new { r.Name, r.Rule });
        await db.SaveChangesAsync(ct);
        return new PolicyDto(p.Id, p.Name, p.Rule, p.RoleIds, p.UserIds, p.IsDefault, p.Version);
    }

    public async Task<Result> DeletePolicyAsync(Guid id, CancellationToken ct)
    {
        var p = await db.AttendancePolicies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound();
        db.AttendancePolicies.Remove(p);
        audit.Add("attendance.policyDeleted", nameof(AttendancePolicy), id.ToString(), new { p.Name });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PlaceDto>> AddPlaceAsync(UpsertPlaceRequest r, CancellationToken ct)
    {
        var p = AttendancePlace.Create(r.Name, r.Latitude, r.Longitude, r.RadiusMeters);
        if (p.IsFailure) return p.Error!;
        db.AttendancePlaces.Add(p.Value!);
        audit.Add("attendance.place", nameof(AttendancePlace), p.Value!.Id.ToString(), new { r.Name });
        await db.SaveChangesAsync(ct);
        return new PlaceDto(p.Value!.Id, p.Value.Name, p.Value.Latitude, p.Value.Longitude, p.Value.RadiusMeters);
    }

    public async Task<Result> DeletePlaceAsync(Guid id, CancellationToken ct)
    {
        var p = await db.AttendancePlaces.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound();
        db.AttendancePlaces.Remove(p);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<HolidayDto>> AddHolidayAsync(UpsertHolidayRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100) return Error.Validation("holidays.name", "نام تعطیلی الزامی است.");
        if (await db.Holidays.AnyAsync(h => h.Date == r.Date, ct)) return Error.Conflict("holidays.exists", "این روز قبلاً ثبت شده است.");
        var h = new Holiday(r.Date, r.Name);
        db.Holidays.Add(h);
        await db.SaveChangesAsync(ct);
        return new HolidayDto(h.Id, h.Date, h.Name);
    }

    public async Task<Result> DeleteHolidayAsync(Guid id, CancellationToken ct)
    {
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return Error.NotFound();
        db.Holidays.Remove(h);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- background job ----
    /// <summary>
    /// A shift still open at the scheduled end + N hours (PROMPT §6.6 "forgotten"): the worker and the approvers are
    /// warned once, and the shift is closed at the scheduled end, marked for review (never a guessed time). The close is
    /// a system clock-out event (no actor) so the hours stay backed by the event log.
    /// </summary>
    public async Task<int> CloseForgottenAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var s = await store.GetAsync(ct);
        var open = await db.Shifts.Where(x => x.Status == ShiftStatus.Open).ToListAsync(ct);
        var schedules = await db.WorkSchedules.AsNoTracking().ToListAsync(ct);
        var fallback = schedules.FirstOrDefault(x => x.UserId == null);
        var due = new List<(Shift Shift, DateTimeOffset End)>();
        foreach (var shift in open)
        {
            var day = (schedules.FirstOrDefault(x => x.UserId == shift.UserId) ?? fallback)?.For(shift.Day);
            var end = day is null ? shift.ClockInAt.AddHours(8) : Iran.At(shift.Day, day.End);
            if (end < shift.ClockInAt) end = shift.ClockInAt;
            if (now >= end.AddHours(s.ForgottenAfterHours)) due.Add((shift, end));
        }
        if (due.Count == 0) return 0;

        var userIds = due.Select(x => x.Shift.UserId).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var approvers = await notifier.StaffWithAsync(Permissions.AttendanceApprove, PermissionScope.All, ct);
        foreach (var (shift, end) in due)
        {
            shift.AutoClose(end);
            const string note = "خروج ثبت نشد؛ سیستم شیفت را در پایان برنامه بست.";
            db.AttendanceEvents.Add(new AttendanceEvent(shift.UserId, shift.Id, AttendanceEventKind.ClockOut, shift.ClockOutAt!.Value, now, null, $"auto-out-{shift.Id:N}",
                "[]", Trust.Yellow, note, null));
            foreach (var t in await db.WorkTimers.Where(t => t.UserId == shift.UserId && t.StoppedAt == null).ToListAsync(ct)) t.Stop(end);

            var at = Iran.Local(end).ToString("HH:mm");
            var name = names.GetValueOrDefault(shift.UserId) ?? "—";
            await notifier.NotifyAsync([shift.UserId], "attendance.forgotten", "خروج شما ثبت نشد",
                $"شیفت شما ساعت {at} (پایان برنامه) بسته شد و نیازمند بررسی است. ساعت واقعی خروج را با «درخواست اصلاح» ثبت کنید.", "/attendance", true,
                $"forgot:{shift.Id:N}", ct);
            await notifier.NotifyAsync(approvers.Where(u => u != shift.UserId), "attendance.forgotten", $"خروج ثبت نشد: {name}",
                $"شیفت {name} تا {s.ForgottenAfterHours} ساعت بعد از پایان برنامه باز ماند؛ سیستم آن را ساعت {at} بست و نیازمند بررسی است.", "/attendance/live", false,
                $"forgot:{shift.Id:N}", ct);

            audit.Add("attendance.autoClosed", nameof(Shift), shift.Id.ToString(), actorOverride: null);
            changes.Record("presence", shift.UserId, null, shift.UserId, null, "autoClosed");
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return due.Count;
    }
}
