using Karnama.Domain.Common;

namespace Karnama.Domain.Chat;

/// <summary>Direct = two people; Group = a named team chat; Work = the chat about one order (§7 "about this work").</summary>
public enum ConversationKind { Direct = 1, Group = 2, Work = 3 }

public enum MessageKind { Text = 1, Image = 2, Voice = 3, File = 4 }

public sealed class Conversation : Entity
{
    private readonly List<ConversationMember> _members = [];
    private Conversation() { }

    public ConversationKind Kind { get; private set; }
    public string? Title { get; private set; }
    /// <summary>Sorted "a:b" user pair of a direct chat: one direct chat per pair (unique index).</summary>
    public string? DirectKey { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }
    public long LastMessageId { get; private set; }
    public string? LastPreview { get; private set; }
    public IReadOnlyCollection<ConversationMember> Members => _members;

    public static Conversation Direct(Guid a, Guid b, DateTimeOffset now)
    {
        var c = new Conversation { Kind = ConversationKind.Direct, DirectKey = Key(a, b), CreatedBy = a, CreatedAt = now, LastMessageAt = now };
        c._members.Add(new ConversationMember(c.Id, a, now));
        c._members.Add(new ConversationMember(c.Id, b, now));
        return c;
    }

    public static string Key(Guid a, Guid b) => string.CompareOrdinal(a.ToString("N"), b.ToString("N")) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";

    public static Result<Conversation> Group(string title, Guid by, IEnumerable<Guid> members, DateTimeOffset now)
    {
        var c = new Conversation { Kind = ConversationKind.Group, CreatedBy = by, CreatedAt = now, LastMessageAt = now };
        var r = c.Rename(title);
        if (r.IsFailure) return r.Error!;
        foreach (var m in members.Append(by).Distinct()) c._members.Add(new ConversationMember(c.Id, m, now));
        return c;
    }

    public static Conversation ForOrder(Guid orderId, string title, DateTimeOffset now) =>
        new() { Kind = ConversationKind.Work, OrderId = orderId, Title = Cut(title, 120), CreatedAt = now, LastMessageAt = now };

    public Result Rename(string title)
    {
        var t = (title ?? "").Trim();
        if (t.Length is 0 or > 80) return Error.Validation("chat.title", "نام گروه الزامی است (حداکثر ۸۰ کاراکتر).");
        Title = t;
        return Result.Success();
    }

    public ConversationMember AddMember(Guid userId, DateTimeOffset now)
    {
        if (_members.FirstOrDefault(m => m.UserId == userId) is { } existing) return existing;
        var m = new ConversationMember(Id, userId, now);
        _members.Add(m);
        return m;
    }

    public void RemoveMember(Guid userId) => _members.RemoveAll(m => m.UserId == userId);

    public void Touch(ChatMessage m)
    {
        LastMessageAt = m.CreatedAt;
        LastMessageId = m.Id;
        LastPreview = m.Preview();
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class ConversationMember
{
    private ConversationMember() { }
    public ConversationMember(Guid conversationId, Guid userId, DateTimeOffset now) { ConversationId = conversationId; UserId = userId; JoinedAt = now; }

    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    /// <summary>Messages up to this id are read.</summary>
    public long LastReadId { get; private set; }
    public bool Muted { get; private set; }

    public void Read(long upTo)
    {
        if (upTo > LastReadId) LastReadId = upTo;
    }

    public void Mute(bool muted) => Muted = muted;
}

public sealed class ChatMessage
{
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);
    private ChatMessage() { }

    public long Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid AuthorId { get; private set; }
    public MessageKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? FileKey { get; private set; }
    public string? FileName { get; private set; }
    public string? ContentType { get; private set; }
    public long? FileSize { get; private set; }
    public int? DurationSeconds { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    /// <summary>Client-made id: a message re-sent after a network drop is stored once.</summary>
    public Guid ClientKey { get; private set; }

    public static Result<ChatMessage> Text(Guid conversationId, Guid author, string body, Guid clientKey, DateTimeOffset now)
    {
        var b = (body ?? "").Trim();
        if (b.Length is 0 or > 4000) return Error.Validation("chat.body", "پیام خالی است یا بیش از ۴۰۰۰ کاراکتر است.");
        return new ChatMessage { ConversationId = conversationId, AuthorId = author, Kind = MessageKind.Text, Body = b, CreatedAt = now, ClientKey = clientKey };
    }

    public static ChatMessage Attachment(Guid conversationId, Guid author, MessageKind kind, string fileKey, string fileName, string contentType, long size, int? duration,
        string? caption, Guid clientKey, DateTimeOffset now) => new()
    {
        ConversationId = conversationId, AuthorId = author, Kind = kind, FileKey = fileKey, FileName = fileName.Length > 200 ? fileName[..200] : fileName,
        ContentType = contentType, FileSize = size, DurationSeconds = duration, Body = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim()[..Math.Min(caption.Trim().Length, 1000)],
        CreatedAt = now, ClientKey = clientKey,
    };

    public Result Edit(Guid by, string body, DateTimeOffset now)
    {
        if (by != AuthorId) return Error.Forbidden();
        if (DeletedAt is not null) return Error.Conflict("chat.deleted", "این پیام حذف شده است.");
        if (now - CreatedAt > EditWindow) return Error.Conflict("chat.editWindow", "فقط تا ۱۵ دقیقه بعد از فرستادن می‌شود پیام را ویرایش کرد.");
        var b = (body ?? "").Trim();
        if (b.Length is 0 or > 4000) return Error.Validation("chat.body", "پیام خالی است یا بیش از ۴۰۰۰ کاراکتر است.");
        Body = b;
        EditedAt = now;
        return Result.Success();
    }

    /// <summary>Soft delete: the row stays (order of the chat), its content is no longer shown.</summary>
    public Result Delete(Guid by, bool moderator, DateTimeOffset now)
    {
        if (by != AuthorId && !moderator) return Error.Forbidden();
        DeletedAt ??= now;
        return Result.Success();
    }

    public string Preview() => DeletedAt is not null ? "پیام حذف شد" : Kind switch
    {
        MessageKind.Image => "🖼 عکس",
        MessageKind.Voice => "🎤 پیام صوتی",
        MessageKind.File => $"📎 {FileName}",
        _ => Body!.Length > 80 ? Body[..80] + "…" : Body,
    };
}
