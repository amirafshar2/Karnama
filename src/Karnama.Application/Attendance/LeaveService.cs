using Karnama.Application.Common;
using Karnama.Application.Abstractions;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Attendance;

public sealed record LeaveTypeDto(Guid Id, string Name, LeaveKind Kind, bool Paid, bool NeedsDocument, decimal? AnnualDays, string Color, bool IsActive, int SortOrder);
public sealed record UpsertLeaveTypeRequest(string Name, LeaveKind Kind, bool Paid, bool NeedsDocument, decimal? AnnualDays, string Color, int SortOrder, bool IsActive);
public sealed record LeaveRequestDto(Guid Id, Guid UserId, string UserName, Guid LeaveTypeId, string TypeName, string Color, bool Hourly, DateTimeOffset From, DateTimeOffset To,
    int Minutes, string Reason, LeaveStatus Status, string? DecisionNote, bool ByManager, DateTimeOffset CreatedAt, bool MissingDocument);
public sealed record CreateLeaveRequest(Guid LeaveTypeId, bool Hourly, DateTimeOffset From, DateTimeOffset To, string Reason, Guid? DocumentFileId, Guid? ForUserId);
public sealed record BalanceDto(Guid LeaveTypeId, string TypeName, string Color, decimal? AllowanceDays, decimal UsedDays, decimal? LeftDays);
public sealed record AbsenceDto(Guid Id, Guid UserId, string UserName, DateOnly Day, DateTimeOffset ExpectedAt, DateTimeOffset ReasonDeadline, AbsenceStatus Status,
    string? Reason, DateTimeOffset? ActualFrom, DateTimeOffset? ActualTo, int LateMinutes, bool AutoDecided, string? DecisionNote);
public sealed record AbsenceReasonRequest(string Reason, DateTimeOffset? From, DateTimeOffset? To, Guid? DocumentFileId);
public sealed record DecisionResultDto(LeaveRequestDto Leave, IReadOnlyList<AffectedWorkDto> WorkToReassign);
public sealed record AffectedWorkDto(Guid Id, string Title, string StageName, DateTimeOffset? Deadline);
public sealed record CalendarDayDto(DateOnly Day, string State, string? Color, string? Label);
public sealed record CalendarRowDto(Guid UserId, string Name, IReadOnlyList<CalendarDayDto> Days);
public sealed record MonthReportRowDto(Guid UserId, string Name, int WorkedMinutes, int LateMinutes, int LateCount, int EarlyLeaveMinutes, int PaidLeaveMinutes,
    int UnpaidLeaveMinutes, int ExcusedAbsences, int UnexcusedAbsences, int Shifts);

/// <summary>Leave, absence and lateness (docs/PROMPT.md §6.7): requests, approvals, automatic detection, balances, team calendar, monthly report.</summary>
public sealed class LeaveService(IAppDbContext db, ICurrentUser me, AttendanceSettingsStore settings, IAuditWriter audit, TimeProvider clock, IChangeFeed changes, IExcelExporter excel)
{
    private Guid Me => me.UserId ?? Guid.Empty;
    private bool CanApprove => me.Has(Permissions.AttendanceApprove, PermissionScope.All);
    private bool SeesAll => me.Has(Permissions.AttendanceView, PermissionScope.All);

    // ---------------- types ----------------
    public async Task<IReadOnlyList<LeaveTypeDto>> TypesAsync(CancellationToken ct) =>
        (await db.LeaveTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<Result<LeaveTypeDto>> SaveTypeAsync(Guid? id, UpsertLeaveTypeRequest r, CancellationToken ct)
    {
        LeaveType t;
        if (id is { } tid)
        {
            var found = await db.LeaveTypes.FirstOrDefaultAsync(x => x.Id == tid, ct);
            if (found is null) return Error.NotFound();
            t = found;
            var u = t.Update(r.Name, r.Kind, r.Paid, r.NeedsDocument, r.AnnualDays, r.Color, r.SortOrder, r.IsActive);
            if (u.IsFailure) return u.Error!;
            db.MarkModified(t);
        }
        else
        {
            var c = LeaveType.Create(r.Name, r.Kind, r.Paid, r.NeedsDocument, r.AnnualDays, r.Color, r.SortOrder);
            if (c.IsFailure) return c.Error!;
            t = c.Value!;
            db.LeaveTypes.Add(t);
        }
        audit.Add("leaveTypes.saved", nameof(LeaveType), t.Id.ToString(), new { r.Name, r.AnnualDays });
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    private static LeaveTypeDto ToDto(LeaveType t) => new(t.Id, t.Name, t.Kind, t.Paid, t.NeedsDocument, t.AnnualDays, t.Color, t.IsActive, t.SortOrder);

    // ---------------- requests ----------------
    public async Task<Result<LeaveRequestDto>> RequestAsync(CreateLeaveRequest r, CancellationToken ct)
    {
        var forUser = r.ForUserId ?? Me;
        var byManager = forUser != Me;
        if (byManager && !CanApprove) return Error.Forbidden("leaves.forOthers", "ثبت مرخصی برای دیگران فقط با مجوز تأیید مرخصی.");
        if (byManager && !await db.Users.AnyAsync(u => u.Id == forUser && u.Kind == UserKind.Staff, ct)) return Error.NotFound("users.notFound", "همکار پیدا نشد.");
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct);
        if (type is null) return Error.Validation("leaves.type", "نوع مرخصی را انتخاب کنید.");
        if (r.DocumentFileId is { } doc && !await db.Files.AnyAsync(f => f.Id == doc && f.UploadedBy == Me, ct)) return Error.Validation("leaves.document", "مدرک پیدا نشد.");
        var overlap = await db.LeaveRequests.AnyAsync(l => l.UserId == forUser && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                                                           && l.From < r.To && r.From < l.To, ct);
        if (overlap) return Error.Conflict("leaves.overlap", "در این بازه درخواست دیگری دارید.");
        var now = clock.GetUtcNow();
        var created = LeaveRequest.Create(forUser, type, r.Hourly, r.From, r.To, r.Reason, r.DocumentFileId, Me, byManager, now);
        if (created.IsFailure) return created.Error!;
        db.LeaveRequests.Add(created.Value!);
        audit.Add(byManager ? "leaves.registered" : "leaves.requested", nameof(LeaveRequest), created.Value!.Id.ToString(), new { forUser, type = type.Name, r.From, r.To });
        // Managers see the request live; the worker sees a manager's registration live.
        changes.Record("presence", forUser, null, forUser, null, "leave");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await DtosAsync([created.Value!], ct))[0];
    }

    public async Task<IReadOnlyList<LeaveRequestDto>> ListAsync(bool pendingOnly, Guid? userId, CancellationToken ct)
    {
        var q = db.LeaveRequests.AsNoTracking();
        if (!(CanApprove || SeesAll)) q = q.Where(l => l.UserId == Me);
        else if (userId is { } uid) q = q.Where(l => l.UserId == uid);
        if (pendingOnly) q = q.Where(l => l.Status == LeaveStatus.Pending);
        return await DtosAsync(await q.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync(ct), ct);
    }

    public async Task<Result<DecisionResultDto>> DecideAsync(Guid id, DecisionRequest r, CancellationToken ct)
    {
        if (!CanApprove) return Error.Forbidden();
        var l = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return Error.NotFound();
        var d = l.Decide(Me, r.Approve, r.Note, clock.GetUtcNow());
        if (d.IsFailure) return d.Error!;
        audit.Add(r.Approve ? "leaves.approved" : "leaves.rejected", nameof(LeaveRequest), id.ToString(), new { l.UserId });
        changes.Record("presence", l.UserId, null, l.UserId, null, "leave");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        // Approved: cards with a deadline inside the leave should probably go to someone else.
        var affected = r.Approve
            ? await db.WorkItems.AsNoTracking().Where(w => w.AssigneeId == l.UserId && w.Status != WorkStatus.Done && w.Status != WorkStatus.Cancelled
                                                           && w.Deadline >= l.From && w.Deadline <= l.To.AddDays(1))
                .Select(w => new { w.Id, w.Title, w.StageName, w.Deadline }).ToListAsync(ct)
            : [];
        return new DecisionResultDto((await DtosAsync([l], ct))[0],
            affected.Select(w => new AffectedWorkDto(w.Id, w.Title, w.StageName.Get(me.LanguageCode), w.Deadline)).ToList());
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var l = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return Error.NotFound();
        var c = l.Cancel(Me, clock.GetUtcNow());
        if (c.IsFailure) return c;
        audit.Add("leaves.cancelled", nameof(LeaveRequest), id.ToString());
        changes.Record("presence", l.UserId, null, l.UserId, null, "leave");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    /// <summary>Allowance − approved use this Persian year (from requests, never a stored number).</summary>
    public async Task<IReadOnlyList<BalanceDto>> BalanceAsync(Guid? userId, CancellationToken ct)
    {
        var uid = SeesAll && userId is { } u ? u : Me;
        var now = clock.GetUtcNow();
        var yearStart = PersianYearStart(now);
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive && (t.Kind == LeaveKind.Annual || t.Kind == LeaveKind.Sick || t.AnnualDays != null)).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var approved = await db.LeaveRequests.AsNoTracking().Where(l => l.UserId == uid && l.Status == LeaveStatus.Approved && l.To > yearStart).ToListAsync(ct);
        var dayMinutes = await WorkdayMinutesAsync(ct);
        return types.Select(t =>
        {
            var used = approved.Where(l => l.LeaveTypeId == t.Id).Sum(l => LeaveMinutes(l, dayMinutes)) / (decimal)dayMinutes;
            used = Math.Round(used, 2);
            return new BalanceDto(t.Id, t.Name, t.Color, t.AnnualDays, used, t.AnnualDays is { } a ? a - used : null);
        }).ToList();
    }

    // ---------------- absences ----------------
    public async Task<IReadOnlyList<AbsenceDto>> AbsencesAsync(bool openOnly, CancellationToken ct)
    {
        var q = db.Absences.AsNoTracking();
        if (!(CanApprove || SeesAll)) q = q.Where(a => a.UserId == Me);
        if (openOnly) q = q.Where(a => a.Status == AbsenceStatus.Detected || a.Status == AbsenceStatus.ReasonSubmitted);
        var rows = await q.OrderByDescending(a => a.Day).Take(200).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.UserId), ct);
        return rows.Select(a => ToDto(a, names)).ToList();
    }

    public async Task<Result<AbsenceDto>> ReasonAsync(Guid id, AbsenceReasonRequest r, CancellationToken ct)
    {
        var a = await db.Absences.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Me, ct);
        if (a is null) return Error.NotFound();
        var res = a.SubmitReason(r.Reason, r.From, r.To, r.DocumentFileId);
        if (res.IsFailure) return res.Error!;
        audit.Add("absences.reason", nameof(Absence), id.ToString());
        changes.Record("presence", a.UserId, null, a.UserId, null, "absence");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return ToDto(a, await NamesAsync([a.UserId], ct));
    }

    public async Task<Result<AbsenceDto>> DecideAbsenceAsync(Guid id, DecisionRequest r, CancellationToken ct)
    {
        if (!CanApprove) return Error.Forbidden();
        var a = await db.Absences.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound();
        var res = a.Decide(Me, r.Approve, r.Note);
        if (res.IsFailure) return res.Error!;
        audit.Add(r.Approve ? "absences.excused" : "absences.unexcused", nameof(Absence), id.ToString(), new { a.UserId, a.Day });
        changes.Record("presence", a.UserId, null, a.UserId, null, "absence");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return ToDto(a, await NamesAsync([a.UserId], ct));
    }

    /// <summary>
    /// Every few minutes: whoever was due (start + grace), has no shift today and no approved leave → "absence detected".
    /// Also: absences past the reason deadline become unexcused. Idempotent (one absence per person per day).
    /// </summary>
    public async Task<(int Detected, int Expired)> DetectAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = Iran.Today(now);
        var s = await settings.GetAsync(ct);
        var detected = 0;
        if (!await db.Holidays.AnyAsync(h => h.Date == today, ct))
        {
            var schedules = await db.WorkSchedules.AsNoTracking().ToListAsync(ct);
            var fallback = schedules.FirstOrDefault(x => x.UserId == null);
            var staff = await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive).Select(u => u.Id).ToListAsync(ct);
            var present = (await db.Shifts.AsNoTracking().Where(x => x.Day == today).Select(x => x.UserId).ToListAsync(ct)).ToHashSet();
            var already = (await db.Absences.AsNoTracking().Where(x => x.Day == today).Select(x => x.UserId).ToListAsync(ct)).ToHashSet();
            var leaves = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.From <= now && l.To > now).Select(l => l.UserId).ToListAsync(ct);
            foreach (var uid in staff.Where(u => !present.Contains(u) && !already.Contains(u) && !leaves.Contains(u)))
            {
                var sched = schedules.FirstOrDefault(x => x.UserId == uid) ?? fallback;
                if (sched is null || sched.Flexible || sched.For(today) is not { } day) continue;
                var expected = Iran.At(today, day.Start);
                if (now < expected.AddMinutes(sched.GraceMinutes) || now > Iran.At(today, day.End)) continue;
                db.Absences.Add(Absence.Detect(uid, today, expected, now, s.AbsenceReasonHours));
                changes.Record("presence", uid, null, uid, null, "absence");
                detected++;
            }
        }
        var expired = 0;
        foreach (var a in await db.Absences.Where(x => x.Status == AbsenceStatus.Detected && x.ReasonDeadline <= now).ToListAsync(ct))
            if (a.ExpireIfNoReason(now)) { expired++; audit.Add("absences.autoUnexcused", nameof(Absence), a.Id.ToString(), actorOverride: null); }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException) { /* another worker detected the same day: unique index; next run is clean */ }
        catch (DbUpdateException) { }
        await changes.PublishAsync(ct);
        return (detected, expired);
    }

    // ---------------- calendar & report ----------------
    public async Task<Result<IReadOnlyList<CalendarRowDto>>> CalendarAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 42) return Error.Validation("calendar.range", "بازه حداکثر ۴۲ روز.");
        var users = SeesAll || CanApprove
            ? await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive && (!u.IsDemo || me.IsDemo)).OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct)
            : await db.Users.AsNoTracking().Where(u => u.Id == Me).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var fromAt = Iran.At(from, TimeOnly.MinValue);
        var toAt = Iran.At(to.AddDays(1), TimeOnly.MinValue);
        var shifts = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.UserId) && s.Day >= from && s.Day <= to).Select(s => new { s.UserId, s.Day, s.LateMinutes }).ToListAsync(ct);
        var leaves = await db.LeaveRequests.AsNoTracking().Where(l => ids.Contains(l.UserId) && l.Status == LeaveStatus.Approved && l.From < toAt && l.To > fromAt).ToListAsync(ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var absences = await db.Absences.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.Day >= from && a.Day <= to).ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).ToDictionaryAsync(h => h.Date, h => h.Name, ct);

        return users.Select(u => new CalendarRowDto(u.Id, u.DisplayName, Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i =>
        {
            var d = from.AddDays(i);
            if (holidays.TryGetValue(d, out var hn)) return new CalendarDayDto(d, "holiday", null, hn);
            var dayStart = Iran.At(d, new TimeOnly(12, 0));
            var leave = leaves.FirstOrDefault(l => l.UserId == u.Id && !l.Hourly && l.From <= dayStart && l.To > dayStart)
                        ?? leaves.FirstOrDefault(l => l.UserId == u.Id && l.Hourly && Iran.Today(l.From) == d);
            if (leave is not null && types.TryGetValue(leave.LeaveTypeId, out var lt)) return new CalendarDayDto(d, leave.Hourly ? "hourlyLeave" : "leave", lt.Color, lt.Name);
            var abs = absences.FirstOrDefault(a => a.UserId == u.Id && a.Day == d);
            if (abs is { Status: not AbsenceStatus.BecameLate }) return new CalendarDayDto(d, abs.Status == AbsenceStatus.Excused ? "excused" : "absent", null, null);
            var sh = shifts.Where(s => s.UserId == u.Id && s.Day == d).ToList();
            if (sh.Count > 0) return new CalendarDayDto(d, sh.Sum(s => s.LateMinutes) > 0 ? "late" : "present", null, null);
            return new CalendarDayDto(d, "none", null, null);
        }).ToList())).ToList();
    }

    /// <summary>Per person: worked hours, lateness, early leave, paid/unpaid leave, absences — for payroll.</summary>
    public async Task<Result<IReadOnlyList<MonthReportRowDto>>> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!SeesAll) return Error.Forbidden();
        if (to < from || to.DayNumber - from.DayNumber > 62) return Error.Validation("report.range", "بازه حداکثر ۶۲ روز.");
        var now = clock.GetUtcNow();
        var users = await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && (!u.IsDemo || me.IsDemo)).OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var shifts = await db.Shifts.AsNoTracking().Where(s => s.Day >= from && s.Day <= to).ToListAsync(ct);
        var fromAt = Iran.At(from, TimeOnly.MinValue);
        var toAt = Iran.At(to.AddDays(1), TimeOnly.MinValue);
        var leaves = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.From < toAt && l.To > fromAt).ToListAsync(ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var absences = await db.Absences.AsNoTracking().Where(a => a.Day >= from && a.Day <= to).ToListAsync(ct);
        var dayMinutes = await WorkdayMinutesAsync(ct);
        return users.Select(u =>
        {
            var sh = shifts.Where(s => s.UserId == u.Id).ToList();
            var lv = leaves.Where(l => l.UserId == u.Id).ToList();
            var ab = absences.Where(a => a.UserId == u.Id).ToList();
            return new MonthReportRowDto(u.Id, u.DisplayName, sh.Sum(s => s.WorkedMinutes(now)), sh.Sum(s => s.LateMinutes) + ab.Sum(a => a.LateMinutes),
                sh.Count(s => s.LateMinutes > 0), sh.Sum(s => s.EarlyLeaveMinutes),
                lv.Where(l => types.GetValueOrDefault(l.LeaveTypeId)?.Paid == true).Sum(l => LeaveMinutes(l, dayMinutes)),
                lv.Where(l => types.GetValueOrDefault(l.LeaveTypeId)?.Paid != true).Sum(l => LeaveMinutes(l, dayMinutes)),
                ab.Count(a => a.Status == AbsenceStatus.Excused), ab.Count(a => a.Status == AbsenceStatus.Unexcused), sh.Count);
        }).ToList();
    }

    public async Task<Result<byte[]>> ReportExcelAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var r = await ReportAsync(from, to, ct);
        if (r.IsFailure) return r.Error!;
        static string H(int m) => $"{m / 60}:{m % 60:00}";
        return excel.Export("Attendance", ["نام", "ساعت کار", "تأخیر (دقیقه)", "دفعات تأخیر", "زودرفتن (دقیقه)", "مرخصی با حقوق", "مرخصی بی‌حقوق", "غیبت موجه", "غیبت غیرموجه", "شیفت‌ها"],
            r.Value!.Select(x => (IReadOnlyList<object?>)[x.Name, H(x.WorkedMinutes), x.LateMinutes, x.LateCount, x.EarlyLeaveMinutes, H(x.PaidLeaveMinutes), H(x.UnpaidLeaveMinutes),
                x.ExcusedAbsences, x.UnexcusedAbsences, x.Shifts]));
    }

    // ---------------- helpers ----------------
    private async Task<int> WorkdayMinutesAsync(CancellationToken ct)
    {
        var s = await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == null, ct);
        var d = s?.Days.FirstOrDefault();
        var minutes = d is null ? 480 : (int)(d.End - d.Start).TotalMinutes - (s!.BreakMinutes);
        return Math.Max(60, minutes);
    }

    /// <summary>Hourly = the hours asked; daily = whole days × one working day.</summary>
    private static int LeaveMinutes(LeaveRequest l, int dayMinutes) =>
        l.Hourly ? (int)(l.To - l.From).TotalMinutes : Math.Max(1, (int)Math.Round((l.To - l.From).TotalDays)) * dayMinutes;

    private static DateTimeOffset PersianYearStart(DateTimeOffset now)
    {
        var cal = new System.Globalization.PersianCalendar();
        var local = Iran.Local(now).DateTime;
        var start = cal.ToDateTime(cal.GetYear(local), 1, 1, 0, 0, 0, 0);
        return Iran.At(DateOnly.FromDateTime(start), TimeOnly.MinValue);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private async Task<IReadOnlyList<LeaveRequestDto>> DtosAsync(IReadOnlyList<LeaveRequest> rows, CancellationToken ct)
    {
        var names = await NamesAsync(rows.Select(r => r.UserId), ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var dayMinutes = await WorkdayMinutesAsync(ct);
        return rows.Select(l =>
        {
            var t = types.GetValueOrDefault(l.LeaveTypeId);
            return new LeaveRequestDto(l.Id, l.UserId, names.GetValueOrDefault(l.UserId) ?? "—", l.LeaveTypeId, t?.Name ?? "—", t?.Color ?? "#888888", l.Hourly, l.From, l.To,
                LeaveMinutes(l, dayMinutes), l.Reason, l.Status, l.DecisionNote, l.CreatedBy != l.UserId, l.CreatedAt, t?.NeedsDocument == true && l.DocumentFileId is null);
        }).ToList();
    }

    private static AbsenceDto ToDto(Absence a, Dictionary<Guid, string> names) => new(a.Id, a.UserId, names.GetValueOrDefault(a.UserId) ?? "—", a.Day, a.ExpectedAt,
        a.ReasonDeadline, a.Status, a.Reason, a.ActualFrom, a.ActualTo, a.LateMinutes, a.AutoDecided, a.DecisionNote);
}
