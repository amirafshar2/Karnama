using Karnama.Domain.Common;

namespace Karnama.Domain.Crm;

/// <summary>Where a pipeline column sits: still open, or one of the two endings.</summary>
public enum StageKind { Open = 1, Won = 2, Lost = 3 }

/// <summary>A pipeline column, editable in the panel (PROMPT2 §4).</summary>
public sealed class LeadStage : Entity
{
    private LeadStage() { Name = string.Empty; }

    public string Name { get; private set; }
    public StageKind Kind { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<LeadStage> Create(string name, StageKind kind, int sortOrder, Guid? id = null)
    {
        var s = new LeadStage { Kind = kind, SortOrder = sortOrder };
        if (id is { } given) s.Id = given;
        var r = s.Update(name, sortOrder, true);
        return r.IsSuccess ? s : r.Error!;
    }

    public Result Update(string name, int sortOrder, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 40) return Error.Validation("crm.stageName", "نام مرحله الزامی است (حداکثر ۴۰ کاراکتر).");
        Name = n;
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }
}

/// <summary>Where leads come from (referral, Instagram, website form, bot, …), editable in the panel.</summary>
public sealed class LeadSource : Entity
{
    private LeadSource() { Name = string.Empty; }

    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public static Result<LeadSource> Create(string name, int sortOrder, Guid? id = null)
    {
        var s = new LeadSource { SortOrder = sortOrder };
        if (id is { } given) s.Id = given;
        var r = s.Update(name, sortOrder, true);
        return r.IsSuccess ? s : r.Error!;
    }

    public Result Update(string name, int sortOrder, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 40) return Error.Validation("crm.sourceName", "نام منبع الزامی است (حداکثر ۴۰ کاراکتر).");
        Name = n;
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }
}

public sealed record LeadSpec(string Name, string? Phone, string? Business, string? Industry, string? City, string? Instagram, Guid? SourceId,
    IReadOnlyList<string>? Tags, long? Budget, string? Note);

/// <summary>A sales lead (PROMPT2 §4). Marketers see only their own; the phone number finds duplicates.</summary>
public sealed class Lead : Entity
{
    private Lead() { Name = string.Empty; Tags = []; }

    /// <summary>Contact person.</summary>
    public string Name { get; private set; }
    /// <summary>Normalized mobile, or null for a lead that came from the bot without one yet.</summary>
    public string? Phone { get; private set; }
    public string? Business { get; private set; }
    public string? Industry { get; private set; }
    public string? City { get; private set; }
    public string? Instagram { get; private set; }
    public Guid? SourceId { get; private set; }
    /// <summary>Free text the visitor wrote (website form, bot).</summary>
    public string? Message { get; private set; }
    /// <summary>Page or channel it came from (e.g. "/plans/gold", "telegram").</summary>
    public string? Origin { get; private set; }
    public List<string> Tags { get; private set; }
    /// <summary>Approximate monthly budget in rials.</summary>
    public long? Budget { get; private set; }
    public string? Note { get; private set; }

    public Guid StageId { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string? LostReason { get; private set; }
    public DateTimeOffset? NextFollowUpAt { get; private set; }
    /// <summary>The follow-up time already reminded (each reminder once).</summary>
    public DateTimeOffset? FollowUpRemindedFor { get; private set; }
    public long? MessengerChatId { get; private set; }
    public Guid? ClientId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public uint Version { get; private set; }

    public static Result<Lead> Create(LeadSpec s, Guid stageId, DateTimeOffset now, string? message = null, string? origin = null, long? chatId = null)
    {
        var l = new Lead { StageId = stageId, CreatedAt = now, LastActivityAt = now, Message = Cut(message, 1000), Origin = Cut(origin, 200), MessengerChatId = chatId };
        var r = l.Update(s, now, allowNoPhone: chatId is not null);
        return r.IsSuccess ? l : r.Error!;
    }

    public Result Update(LeadSpec s, DateTimeOffset now, bool allowNoPhone = false)
    {
        var name = (s.Name ?? "").Trim();
        if (name.Length is < 2 or > 80) return Error.Validation("leads.name", "نام را بنویسید.");
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(s.Phone))
        {
            if (!PhoneNumber.TryNormalize(s.Phone, out var normalized)) return Error.Validation("leads.phone", "شماره‌ی موبایل درست نیست (مثلاً ۰۹۱۲۳۴۵۶۷۸۹).");
            phone = normalized;
        }
        else if (!allowNoPhone && MessengerChatId is null) return Error.Validation("leads.phone", "شماره‌ی موبایل الزامی است.");
        if (s.Budget is < 0) return Error.Validation("leads.budget", "بودجه نمی‌تواند منفی باشد.");
        var tags = (s.Tags ?? []).Select(t => t.Trim().TrimStart('#')).Where(t => t.Length is > 0 and <= 30).Distinct().Take(10).ToList();
        Name = name;
        Phone = phone;
        Business = Cut(s.Business, 80);
        Industry = Cut(s.Industry, 60);
        City = Cut(s.City, 40);
        Instagram = Cut(s.Instagram?.Trim().TrimStart('@'), 60);
        SourceId = s.SourceId;
        Tags = tags;
        Budget = s.Budget;
        Note = Cut(s.Note, 2000);
        UpdatedAt = now;
        return Result.Success();
    }

    public void SetPhone(string normalized, DateTimeOffset now) { Phone = normalized; UpdatedAt = now; }

    public void Assign(Guid? ownerId, DateTimeOffset now) { OwnerId = ownerId; UpdatedAt = now; }

    public Result Move(LeadStage stage, string? lostReason, DateTimeOffset now)
    {
        if (stage.Kind == StageKind.Lost && string.IsNullOrWhiteSpace(lostReason))
            return Error.Validation("crm.lostReason", "دلیل از دست رفتن را بنویسید.");
        StageId = stage.Id;
        LostReason = stage.Kind == StageKind.Lost ? Cut(lostReason, 300) : null;
        ClosedAt = stage.Kind == StageKind.Open ? null : now;
        if (stage.Kind != StageKind.Open) NextFollowUpAt = null;
        UpdatedAt = now;
        return Result.Success();
    }

    public void FollowUp(DateTimeOffset? at, DateTimeOffset now)
    {
        NextFollowUpAt = at;
        UpdatedAt = now;
    }

    public bool NeedsFollowUpReminder(DateTimeOffset now)
    {
        if (NextFollowUpAt is not { } at || at > now || FollowUpRemindedFor == at) return false;
        FollowUpRemindedFor = at;
        return true;
    }

    public void Touched(DateTimeOffset now) => LastActivityAt = now;

    public void ConvertedTo(Guid clientId, DateTimeOffset now) { ClientId = clientId; UpdatedAt = now; }

    public void AppendMessage(string text, DateTimeOffset now)
    {
        Message = Cut(string.IsNullOrEmpty(Message) ? text : $"{Message}\n—\n{text}", 1000);
        LastActivityAt = now;
    }

    private static string? Cut(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];
}

public enum ActivityKind { Created = 1, Call = 2, Message = 3, Meeting = 4, Email = 5, Note = 6, StageChanged = 7, Assigned = 8, Converted = 9, Inbound = 10 }
public enum CallDirection { Outgoing = 1, Incoming = 2 }
public enum CallOutcome { NoAnswer = 1, Interested = 2, Later = 3, Rejected = 4, Done = 5 }
public enum MessageChannel { Telegram = 1, WhatsApp = 2, Sms = 3, Bale = 4, Instagram = 5 }

/// <summary>Everything that happened with a lead, newest first: calls, messages, meetings, stage moves, hand-overs.</summary>
public sealed class LeadActivity
{
    private LeadActivity() { }

    public long Id { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid? ActorId { get; private set; }
    public ActivityKind Kind { get; private set; }
    public CallDirection? Direction { get; private set; }
    public int? DurationSeconds { get; private set; }
    public CallOutcome? Outcome { get; private set; }
    public MessageChannel? Channel { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Stage moves: from → to (stage ids), hand-overs: previous → new owner.</summary>
    public Guid? FromId { get; private set; }
    public Guid? ToId { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static LeadActivity Log(Guid leadId, Guid? actor, ActivityKind kind, DateTimeOffset at, string? note = null, Guid? from = null, Guid? to = null) => new()
    {
        LeadId = leadId, ActorId = actor, Kind = kind, At = at, Note = Cut(note), FromId = from, ToId = to,
    };

    public static Result<LeadActivity> Call(Guid leadId, Guid actor, CallDirection direction, int? seconds, CallOutcome outcome, string? note, DateTimeOffset at)
    {
        if (seconds is < 0 or > 6 * 3600) return Error.Validation("crm.duration", "مدت تماس درست نیست.");
        return new LeadActivity { LeadId = leadId, ActorId = actor, Kind = ActivityKind.Call, Direction = direction, DurationSeconds = seconds, Outcome = outcome, Note = Cut(note), At = at };
    }

    public static LeadActivity Contact(Guid leadId, Guid actor, ActivityKind kind, MessageChannel? channel, string? note, DateTimeOffset at) => new()
    {
        LeadId = leadId, ActorId = actor, Kind = kind, Channel = kind == ActivityKind.Message ? channel : null, Note = Cut(note), At = at,
    };

    private static string? Cut(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(1000, s.Trim().Length)];
}
