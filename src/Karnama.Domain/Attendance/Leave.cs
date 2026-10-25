using Karnama.Domain.Common;

namespace Karnama.Domain.Attendance;

/// <summary>What a leave type means for payroll and the calendar (docs/PROMPT.md §6.7).</summary>
public enum LeaveKind { Annual, Sick, Mission, ExcusedAbsence, UnexcusedAbsence, Other }

/// <summary>A kind of leave, defined in the panel: paid or not, needs a document, yearly allowance, colour.</summary>
public sealed class LeaveType : Entity
{
    private LeaveType() { Name = string.Empty; Color = "#888888"; }

    public string Name { get; private set; }
    public LeaveKind Kind { get; private set; }
    public bool Paid { get; private set; }
    public bool NeedsDocument { get; private set; }
    /// <summary>Days per year (null = no limit). Balance = allowance − approved use, computed from requests, never edited.</summary>
    public decimal? AnnualDays { get; private set; }
    public string Color { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }
    public uint Version { get; private set; }

    public static Result<LeaveType> Create(string name, LeaveKind kind, bool paid, bool needsDocument, decimal? annualDays, string color, int sortOrder)
    {
        var t = new LeaveType();
        var r = t.Update(name, kind, paid, needsDocument, annualDays, color, sortOrder, true);
        return r.IsSuccess ? t : r.Error!;
    }

    public Result Update(string name, LeaveKind kind, bool paid, bool needsDocument, decimal? annualDays, string color, int sortOrder, bool isActive)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) return Error.Validation("leaveTypes.name", "نام نوع مرخصی الزامی است.");
        if (annualDays is < 0 or > 365) return Error.Validation("leaveTypes.days", "سقف سالانه باید بین ۰ تا ۳۶۵ روز باشد.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$")) return Error.Validation("leaveTypes.color", "رنگ معتبر نیست.");
        Name = name; Kind = kind; Paid = paid; NeedsDocument = needsDocument; AnnualDays = annualDays; Color = color!; SortOrder = sortOrder; IsActive = isActive;
        return Result.Success();
    }
}

public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>Leave asked in advance (daily or hourly), or registered by a manager (e.g. told by phone) — then approved at once.</summary>
public sealed class LeaveRequest : Entity
{
    public const int MaxReason = 1000;
    private LeaveRequest() { Reason = string.Empty; }

    public Guid UserId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public bool Hourly { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public string Reason { get; private set; }
    public Guid? DocumentFileId { get; private set; }
    public LeaveStatus Status { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public uint Version { get; private set; }

    public static Result<LeaveRequest> Create(Guid userId, LeaveType type, bool hourly, DateTimeOffset from, DateTimeOffset to, string reason, Guid? documentId,
        Guid createdBy, bool byManager, DateTimeOffset now)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 3 or > MaxReason) return Error.Validation("leaves.reason", "دلیل را بنویسید (حداقل ۳ حرف).");
        if (to <= from) return Error.Validation("leaves.range", "پایان باید بعد از شروع باشد.");
        if (hourly && (to - from).TotalHours > 12) return Error.Validation("leaves.hourly", "مرخصی ساعتی حداکثر ۱۲ ساعت است؛ برای بیشتر، روزانه بگیرید.");
        if ((to - from).TotalDays > 90) return Error.Validation("leaves.long", "حداکثر ۹۰ روز در یک درخواست.");
        if (!type.IsActive) return Error.Validation("leaves.type", "این نوع مرخصی فعال نیست.");
        // A missing document (e.g. a doctor's note) doesn't block the request: the manager sees it is missing and decides.
        return new LeaveRequest
        {
            UserId = userId, LeaveTypeId = type.Id, Hourly = hourly, From = from, To = to, Reason = reason, DocumentFileId = documentId,
            Status = byManager ? LeaveStatus.Approved : LeaveStatus.Pending, CreatedBy = createdBy, CreatedAt = now,
            DecidedBy = byManager ? createdBy : null, DecidedAt = byManager ? now : null,
        };
    }

    public Result Decide(Guid by, bool approve, string? note, DateTimeOffset now)
    {
        if (Status != LeaveStatus.Pending) return Error.Conflict("leaves.decided", "این درخواست قبلاً بررسی شده است.");
        if (by == UserId) return Error.Forbidden("leaves.self", "درخواست خودتان را مدیر دیگری باید تأیید کند.");
        Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        DecidedBy = by; DecidedAt = now;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
        return Result.Success();
    }

    public Result Cancel(Guid by, DateTimeOffset now)
    {
        if (by != UserId) return Error.Forbidden();
        if (Status is LeaveStatus.Rejected or LeaveStatus.Cancelled) return Error.Conflict("leaves.closed", "این درخواست بسته شده است.");
        if (Status == LeaveStatus.Approved && From <= now) return Error.Conflict("leaves.started", "مرخصی شروع شده؛ برای لغو با مدیر هماهنگ کنید.");
        Status = LeaveStatus.Cancelled;
        return Result.Success();
    }

    public bool Covers(DateTimeOffset at) => Status == LeaveStatus.Approved && From <= at && at < To;
}

public enum AbsenceStatus { Detected, ReasonSubmitted, Excused, Unexcused, BecameLate }

/// <summary>
/// "Didn't arrive and didn't say": created by the detection job; the worker writes the reason later, a manager decides.
/// No reason by the deadline → automatically unexcused (a manager can still change it, with audit).
/// </summary>
public sealed class Absence : Entity
{
    private Absence() { }

    public Guid UserId { get; private set; }
    public DateOnly Day { get; private set; }
    public DateTimeOffset ExpectedAt { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset ReasonDeadline { get; private set; }
    public AbsenceStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset? ActualFrom { get; private set; }
    public DateTimeOffset? ActualTo { get; private set; }
    public Guid? DocumentFileId { get; private set; }
    public int LateMinutes { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? DecisionNote { get; private set; }
    public bool AutoDecided { get; private set; }
    public uint Version { get; private set; }

    public static Absence Detect(Guid userId, DateOnly day, DateTimeOffset expectedAt, DateTimeOffset now, int reasonHours) => new()
    {
        UserId = userId, Day = day, ExpectedAt = expectedAt, DetectedAt = now, ReasonDeadline = now.AddHours(reasonHours), Status = AbsenceStatus.Detected,
    };

    /// <summary>Clocked in after all: it was a late arrival of exactly this many minutes.</summary>
    public void BecomeLate(DateTimeOffset clockIn)
    {
        if (Status is not (AbsenceStatus.Detected or AbsenceStatus.ReasonSubmitted)) return;
        Status = AbsenceStatus.BecameLate;
        LateMinutes = Math.Max(0, (int)(clockIn - ExpectedAt).TotalMinutes);
    }

    public Result SubmitReason(string reason, DateTimeOffset? from, DateTimeOffset? to, Guid? documentId)
    {
        if (Status is not (AbsenceStatus.Detected or AbsenceStatus.ReasonSubmitted)) return Error.Conflict("absences.closed", "این مورد بسته شده است.");
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 3 or > 1000) return Error.Validation("absences.reason", "دلیل را بنویسید (حداقل ۳ حرف).");
        if (from is { } f && to is { } t && t <= f) return Error.Validation("absences.range", "پایان باید بعد از شروع باشد.");
        Reason = reason; ActualFrom = from; ActualTo = to; DocumentFileId = documentId ?? DocumentFileId;
        Status = AbsenceStatus.ReasonSubmitted;
        return Result.Success();
    }

    public Result Decide(Guid by, bool excused, string? note)
    {
        if (Status == AbsenceStatus.BecameLate) return Error.Conflict("absences.late", "این مورد به تأخیر تبدیل شده است.");
        if (by == UserId) return Error.Forbidden("absences.self", "غیبت خودتان را مدیر دیگری باید بررسی کند.");
        Status = excused ? AbsenceStatus.Excused : AbsenceStatus.Unexcused;
        DecidedBy = by; DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); AutoDecided = false;
        return Result.Success();
    }

    public bool ExpireIfNoReason(DateTimeOffset now)
    {
        if (Status != AbsenceStatus.Detected || now < ReasonDeadline) return false;
        Status = AbsenceStatus.Unexcused;
        AutoDecided = true;
        return true;
    }
}
