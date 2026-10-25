using Karnama.Domain.Common;

namespace Karnama.Domain.Notify;

public enum Channel { InApp, Sms, Bale, Telegram, Push }

/// <summary>One message in a person's in-app inbox (always created; other channels are copies via the outbox).</summary>
public sealed class Notification : Entity
{
    private Notification() { Kind = string.Empty; Title = string.Empty; EventKey = string.Empty; }

    public Notification(Guid userId, string kind, string title, string? body, string? link, bool important, DateTimeOffset now, string eventKey)
    {
        UserId = userId; Kind = kind; Title = Cut(title, 200); Body = body is null ? null : Cut(body, 1000); Link = link; Important = important; CreatedAt = now;
        EventKey = Cut(eventKey, 160);
    }

    /// <summary>What caused it; one notification per person per event (a retried job never double-notifies).</summary>
    public string EventKey { get; private set; }

    public Guid UserId { get; private set; }
    public string Kind { get; private set; }
    public string Title { get; private set; }
    public string? Body { get; private set; }
    /// <summary>App-relative path only ("/work?open=…"), never an external URL.</summary>
    public string? Link { get; private set; }
    public bool Important { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}

public enum OutboxStatus { Pending, Sent, Failed, Skipped }

/// <summary>
/// Transactional outbox: written in the same transaction as the change that caused it, sent later by the worker with
/// retries. The idempotency key makes a re-run of the same event never send twice.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { IdempotencyKey = string.Empty; Text = string.Empty; }

    public OutboxMessage(Channel channel, Guid userId, string text, string? link, string idempotencyKey, DateTimeOffset dueAt, DateTimeOffset now)
    {
        Channel = channel; UserId = userId; Text = text.Length > 1500 ? text[..1500] : text; Link = link; IdempotencyKey = idempotencyKey;
        DueAt = dueAt; CreatedAt = now; Status = OutboxStatus.Pending;
    }

    public long Id { get; private set; }
    public Channel Channel { get; private set; }
    public Guid UserId { get; private set; }
    public string Text { get; private set; }
    public string? Link { get; private set; }
    public string IdempotencyKey { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    public void Sent(DateTimeOffset now) { Status = OutboxStatus.Sent; SentAt = now; Attempts++; LastError = null; }
    public void Skip(string why) { Status = OutboxStatus.Skipped; LastError = why; }

    /// <summary>Backoff: 1, 2, 4 … minutes; gives up after 8 tries.</summary>
    public void Fail(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= 8) { Status = OutboxStatus.Failed; return; }
        DueAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }
}

/// <summary>Which channels a person wants, quiet hours (Iran time), and muted kinds.</summary>
public sealed class NotificationPrefs
{
    private NotificationPrefs() { Channels = []; Muted = []; }
    public NotificationPrefs(Guid userId) { UserId = userId; Channels = [Channel.InApp, Channel.Bale, Channel.Telegram, Channel.Sms]; Muted = []; QuietFrom = new TimeOnly(22, 0); QuietTo = new TimeOnly(8, 0); }

    public Guid UserId { get; private set; }
    public List<Channel> Channels { get; private set; }
    public List<string> Muted { get; private set; }
    public TimeOnly? QuietFrom { get; private set; }
    public TimeOnly? QuietTo { get; private set; }
    public uint Version { get; private set; }

    public void Update(IEnumerable<Channel> channels, IEnumerable<string> muted, TimeOnly? quietFrom, TimeOnly? quietTo)
    {
        Channels = channels.Append(Channel.InApp).Distinct().ToList();   // the in-app inbox can't be turned off
        Muted = muted.Where(m => m.Length <= 60).Distinct().Take(50).ToList();
        QuietFrom = quietFrom; QuietTo = quietTo;
    }

    /// <summary>When a non-urgent message may be sent: now, or at the end of quiet hours (local time).</summary>
    public DateTimeOffset SendAt(DateTimeOffset utcNow, Func<DateTimeOffset, DateTimeOffset> toLocal, Func<DateOnly, TimeOnly, DateTimeOffset> atLocal)
    {
        if (QuietFrom is not { } from || QuietTo is not { } to || from == to) return utcNow;
        var local = toLocal(utcNow);
        var t = TimeOnly.FromDateTime(local.DateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        var inQuiet = from < to ? t >= from && t < to : t >= from || t < to;
        if (!inQuiet) return utcNow;
        var endDay = from < to || t < to ? day : day.AddDays(1);
        return atLocal(endDay, to);
    }
}

public enum Messenger { Bale, Telegram }

/// <summary>A person's chat with our bot (linked with a one-time code from the app — never by phone number).</summary>
public sealed class MessengerLink
{
    private MessengerLink() { }
    public MessengerLink(Guid userId, Messenger messenger, long chatId, DateTimeOffset now) { UserId = userId; Messenger = messenger; ChatId = chatId; LinkedAt = now; }
    public Guid UserId { get; private set; }
    public Messenger Messenger { get; private set; }
    public long ChatId { get; private set; }
    public DateTimeOffset LinkedAt { get; private set; }
}

public sealed class MessengerLinkCode
{
    private MessengerLinkCode() { CodeHash = string.Empty; }
    public MessengerLinkCode(Guid userId, Messenger messenger, string codeHash, DateTimeOffset expiresAt) { UserId = userId; Messenger = messenger; CodeHash = codeHash; ExpiresAt = expiresAt; }
    public long Id { get; private set; }
    public Guid UserId { get; private set; }
    public Messenger Messenger { get; private set; }
    public string CodeHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public void Use(DateTimeOffset now) => UsedAt = now;
}

/// <summary>Processed bot updates (each update id once, even when the platform retries).</summary>
public sealed class BotUpdate
{
    private BotUpdate() { }
    public Messenger Messenger { get; private set; }
    public long UpdateId { get; private set; }
    public DateTimeOffset At { get; private set; }
}
