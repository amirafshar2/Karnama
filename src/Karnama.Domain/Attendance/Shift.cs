using Karnama.Domain.Common;

namespace Karnama.Domain.Attendance;

public enum ShiftStatus { Open, Closed }

/// <summary>Green: every check passed. Yellow: one failed (needs a look). Red: fake location or a reused QR.</summary>
public enum Trust { Green, Yellow, Red }

public enum AttendanceEventKind { ClockIn, ClockOut, BreakStart, BreakEnd, Correction }

/// <summary>Result of one verification method at clock-in/out ("office IP ✓", "42 m from the office").</summary>
public sealed record MethodResult(string Method, bool Passed, string Evidence, bool Fraud = false);

/// <summary>
/// One working shift (clock-in → clock-out). The reference time is always the server clock; offline events are
/// placed by the elapsed time the device measured, and flagged when the device clock disagrees too much.
/// </summary>
public sealed class Shift : Entity
{
    private Shift() { Methods = []; }

    public Guid UserId { get; private set; }
    public DateOnly Day { get; private set; }
    public DateTimeOffset ClockInAt { get; private set; }
    public DateTimeOffset? ClockOutAt { get; private set; }
    public DateTimeOffset? BreakStartedAt { get; private set; }
    public int BreakMinutes { get; private set; }
    public ShiftStatus Status { get; private set; }
    public Trust Trust { get; private set; }
    public List<MethodResult> Methods { get; private set; }
    public bool NeedsReview { get; private set; }
    public string? ReviewReason { get; private set; }
    public bool Offline { get; private set; }
    /// <summary>Minutes late against the schedule (0 = on time or no schedule).</summary>
    public int LateMinutes { get; private set; }
    public int EarlyLeaveMinutes { get; private set; }
    public string? EarlyLeaveReason { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public uint Version { get; private set; }

    public bool OnBreak => BreakStartedAt is not null;

    public static Shift Open(Guid userId, DateOnly day, DateTimeOffset at, IReadOnlyList<MethodResult> methods, Trust trust, bool needsReview, string? reason, bool offline, int lateMinutes) => new()
    {
        UserId = userId, Day = day, ClockInAt = at, Status = ShiftStatus.Open, Methods = methods.ToList(), Trust = trust,
        NeedsReview = needsReview, ReviewReason = reason, Offline = offline, LateMinutes = Math.Max(0, lateMinutes),
    };

    public Result StartBreak(DateTimeOffset at)
    {
        if (Status != ShiftStatus.Open) return Error.Conflict("attendance.notOpen", "اول ورود بزنید.");
        if (OnBreak) return Error.Conflict("attendance.onBreak", "استراحت از قبل شروع شده است.");
        BreakStartedAt = at;
        return Result.Success();
    }

    public Result EndBreak(DateTimeOffset at)
    {
        if (BreakStartedAt is not { } started) return Error.Conflict("attendance.noBreak", "استراحتی شروع نشده است.");
        BreakMinutes += Math.Max(0, (int)Math.Round((at - started).TotalMinutes));
        BreakStartedAt = null;
        return Result.Success();
    }

    public Result Close(DateTimeOffset at, IReadOnlyList<MethodResult> methods, Trust trust, bool needsReview, string? reason, bool offline, int earlyLeaveMinutes, string? earlyReason)
    {
        if (Status != ShiftStatus.Open) return Error.Conflict("attendance.notOpen", "ورودی باز نیست.");
        if (at < ClockInAt) return Error.Validation("attendance.beforeIn", "زمان خروج قبل از ورود است.");
        if (OnBreak) EndBreak(at);
        ClockOutAt = at;
        Status = ShiftStatus.Closed;
        Methods.AddRange(methods);
        if (trust > Trust) Trust = trust;
        if (needsReview) Flag(reason);
        Offline |= offline;
        EarlyLeaveMinutes = Math.Max(0, earlyLeaveMinutes);
        EarlyLeaveReason = string.IsNullOrWhiteSpace(earlyReason) ? null : earlyReason.Trim()[..Math.Min(earlyReason.Trim().Length, 500)];
        return Result.Success();
    }

    /// <summary>Forgotten clock-out: closed at the end of the scheduled day and marked for review — never guessed.</summary>
    public void AutoClose(DateTimeOffset at)
    {
        if (Status != ShiftStatus.Open) return;
        if (OnBreak) EndBreak(at);
        ClockOutAt = at < ClockInAt ? ClockInAt : at;
        Status = ShiftStatus.Closed;
        Flag("خروج ثبت نشد؛ سیستم شیفت را بست. ساعت واقعی را با «درخواست اصلاح» ثبت کنید.");
    }

    public void Flag(string? reason)
    {
        NeedsReview = true;
        ReviewReason = string.Join(" | ", new[] { ReviewReason, reason }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public void Review(Guid by, bool accept)
    {
        NeedsReview = false;
        ReviewedBy = by;
        if (!accept) Trust = Trust.Red;
    }

    /// <summary>Applies an approved correction (the original times stay in the event log).</summary>
    public Result Correct(DateTimeOffset? clockIn, DateTimeOffset? clockOut)
    {
        var inAt = clockIn ?? ClockInAt;
        var outAt = clockOut ?? ClockOutAt;
        if (outAt is { } o && o <= inAt) return Error.Validation("attendance.order", "زمان خروج باید بعد از ورود باشد.");
        ClockInAt = inAt;
        if (outAt is not null) { ClockOutAt = outAt; Status = ShiftStatus.Closed; }
        NeedsReview = false;
        return Result.Success();
    }

    public int WorkedMinutes(DateTimeOffset now)
    {
        var end = ClockOutAt ?? now;
        var breaks = BreakMinutes + (BreakStartedAt is { } b ? (int)(now - b).TotalMinutes : 0);
        return Math.Max(0, (int)(end - ClockInAt).TotalMinutes - breaks);
    }
}

/// <summary>Append-only log of every attendance action (the shift row is the summary; this is the evidence).</summary>
public sealed class AttendanceEvent
{
    private AttendanceEvent() { IdempotencyKey = string.Empty; MethodsJson = "[]"; }

    public AttendanceEvent(Guid userId, Guid? shiftId, AttendanceEventKind kind, DateTimeOffset at, DateTimeOffset receivedAt, DateTimeOffset? deviceAt,
        string idempotencyKey, string methodsJson, Trust trust, string? note, Guid? actorId)
    {
        UserId = userId; ShiftId = shiftId; Kind = kind; At = at; ReceivedAt = receivedAt; DeviceAt = deviceAt;
        IdempotencyKey = idempotencyKey; MethodsJson = methodsJson; Trust = trust; Note = note; ActorId = actorId;
    }

    public long Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? ShiftId { get; private set; }
    public AttendanceEventKind Kind { get; private set; }
    /// <summary>When it happened (server clock, adjusted for offline delay).</summary>
    public DateTimeOffset At { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? DeviceAt { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string MethodsJson { get; private set; }
    public Trust Trust { get; private set; }
    public string? Note { get; private set; }
    public Guid? ActorId { get; private set; }

    public void AttachShift(Guid shiftId) => ShiftId ??= shiftId;
}

/// <summary>Work timer on one work item. Only one may run per person (a unique index enforces it).</summary>
public sealed class WorkTimer : Entity
{
    private WorkTimer() { }
    public WorkTimer(Guid userId, Guid workItemId, Guid? shiftId, DateTimeOffset at) { UserId = userId; WorkItemId = workItemId; ShiftId = shiftId; StartedAt = at; }

    public Guid UserId { get; private set; }
    public Guid WorkItemId { get; private set; }
    public Guid? ShiftId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? StoppedAt { get; private set; }
    public int Minutes { get; private set; }
    public bool IsRunning => StoppedAt is null;

    public void Stop(DateTimeOffset at)
    {
        if (StoppedAt is not null) return;
        StoppedAt = at < StartedAt ? StartedAt : at;
        Minutes = (int)Math.Round((StoppedAt.Value - StartedAt).TotalMinutes);
    }
}

public enum CorrectionStatus { Pending, Approved, Rejected }

/// <summary>"I forgot to clock out at 18:00" — the worker asks, a manager with attendance.correct decides.</summary>
public sealed class AttendanceCorrection : Entity
{
    private AttendanceCorrection() { Reason = string.Empty; }

    public Guid UserId { get; private set; }
    public Guid? ShiftId { get; private set; }
    public DateOnly Day { get; private set; }
    public DateTimeOffset? ClockIn { get; private set; }
    public DateTimeOffset? ClockOut { get; private set; }
    public string Reason { get; private set; }
    public CorrectionStatus Status { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    public static Result<AttendanceCorrection> Request(Guid userId, Guid? shiftId, DateOnly day, DateTimeOffset? clockIn, DateTimeOffset? clockOut, string reason, DateTimeOffset now)
    {
        if (clockIn is null && clockOut is null) return Error.Validation("corrections.empty", "ساعت ورود یا خروج را وارد کنید.");
        if (clockIn is { } i && clockOut is { } o && o <= i) return Error.Validation("corrections.order", "ساعت خروج باید بعد از ورود باشد.");
        if (clockIn > now || clockOut > now) return Error.Validation("corrections.future", "زمان آینده را نمی‌شود ثبت کرد.");
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 3 or > 1000) return Error.Validation("corrections.reason", "دلیل را بنویسید (حداقل ۳ حرف).");
        return new AttendanceCorrection { UserId = userId, ShiftId = shiftId, Day = day, ClockIn = clockIn, ClockOut = clockOut, Reason = reason, Status = CorrectionStatus.Pending, CreatedAt = now };
    }

    public Result Decide(Guid by, bool approve, string? note, DateTimeOffset now)
    {
        if (Status != CorrectionStatus.Pending) return Error.Conflict("corrections.decided", "این درخواست قبلاً بررسی شده است.");
        Status = approve ? CorrectionStatus.Approved : CorrectionStatus.Rejected;
        DecidedBy = by;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DecidedAt = now;
        return Result.Success();
    }
}
