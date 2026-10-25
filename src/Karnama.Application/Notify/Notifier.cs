using System.Security.Cryptography;
using System.Text;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Notify;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Notify;

public sealed class NotifyOptions
{
    public const string Section = "Notify";
    /// <summary>Public address of the panel, for links in SMS and bot messages (e.g. https://panel.example.ir).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:3000";
    public string? BaleBotUsername { get; set; }
    public string? TelegramBotUsername { get; set; }
    /// <summary>Private archive channel (the bot is its admin): every received file is copied there first (PROMPT2 §8.3).</summary>
    public long? TelegramArchiveChatId { get; set; }
    public long? BaleArchiveChatId { get; set; }
    /// <summary>Files through Bale only after checking its Bot API supports copyMessage for your bot (official docs).</summary>
    public bool BaleFiles { get; set; }
}

/// <summary>
/// Creates the in-app notification and queues the other channels in the transactional outbox (same SaveChanges as
/// the change that caused it). Respects muted kinds and quiet hours; SMS only for important messages (it costs money).
/// </summary>
public sealed class Notifier(IAppDbContext db, TimeProvider clock, IChangeFeed changes)
{
    public async Task NotifyAsync(IEnumerable<Guid> userIds, string kind, string title, string? body, string? link, bool important, string eventKey, CancellationToken ct)
    {
        var ids = userIds.Where(u => u != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return;
        var now = clock.GetUtcNow();
        var prefs = await db.NotificationPrefs.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var links = await db.MessengerLinks.AsNoTracking().Where(l => ids.Contains(l.UserId)).Select(l => new { l.UserId, l.Messenger }).ToListAsync(ct);
        var key = eventKey.Length <= 160 ? eventKey : eventKey[..160];
        var already = (await db.Notifications.AsNoTracking().Where(n => n.EventKey == key && ids.Contains(n.UserId)).Select(n => n.UserId).ToListAsync(ct))
            .Concat(db.Notifications.Local.Where(n => n.EventKey == key).Select(n => n.UserId)).ToHashSet();
        foreach (var uid in ids)
        {
            if (already.Contains(uid)) continue;
            var p = prefs.GetValueOrDefault(uid) ?? new NotificationPrefs(uid);
            if (p.Muted.Contains(kind) && !important) continue;
            var n = new Notification(uid, kind, title, body, link, important, now, key);
            db.Notifications.Add(n);
            changes.Record("notification", n.Id, null, uid, null, "new");

            var text = body is null ? title : $"{title}\n{body}";
            var due = important ? now : p.SendAt(now, Iran.Local, Iran.At);
            void Queue(Channel c) => db.Outbox.Add(new OutboxMessage(c, uid, text, link, $"{key}:{uid:N}:{c}", due, now));
            if (p.Channels.Contains(Channel.Bale) && links.Any(l => l.UserId == uid && l.Messenger == Messenger.Bale)) Queue(Channel.Bale);
            if (p.Channels.Contains(Channel.Telegram) && links.Any(l => l.UserId == uid && l.Messenger == Messenger.Telegram)) Queue(Channel.Telegram);
            if (important && p.Channels.Contains(Channel.Sms)) Queue(Channel.Sms);
        }
    }

    /// <summary>Active staff who hold a permission at a scope (e.g. everyone who reviews work).</summary>
    public async Task<List<Guid>> StaffWithAsync(string permission, PermissionScope scope, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToListAsync(ct);
        var roleIds = roles.Where(r => r.Permissions.Any(g => g.Code == permission && g.Scope >= scope)).Select(r => r.Id).ToList();
        return await db.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId))
            .Join(db.Users.Where(u => u.IsActive && u.Kind == UserKind.Staff), ur => ur.UserId, u => u.Id, (ur, u) => u.Id).Distinct().ToListAsync(ct);
    }

    public async Task<Guid?> PortalUserAsync(Guid clientId, CancellationToken ct) =>
        await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => c.PortalUserId).FirstOrDefaultAsync(ct);
}

public sealed record NotificationDto(Guid Id, string Kind, string Title, string? Body, string? Link, bool Important, DateTimeOffset CreatedAt, bool Read);
public sealed record InboxDto(int Unread, IReadOnlyList<NotificationDto> Items);
public sealed record PrefsDto(IReadOnlyList<Channel> Channels, IReadOnlyList<string> Muted, TimeOnly? QuietFrom, TimeOnly? QuietTo, IReadOnlyList<Messenger> Linked,
    bool BaleAvailable, bool TelegramAvailable);
public sealed record UpdatePrefsRequest(List<Channel> Channels, List<string>? Muted, TimeOnly? QuietFrom, TimeOnly? QuietTo);
public sealed record LinkCodeDto(string Url, string Code, DateTimeOffset ExpiresAt);

/// <summary>The bell: my notifications, read state, channel preferences, linking Bale/Telegram with a one-time code.</summary>
public sealed class InboxService(IAppDbContext db, ICurrentUser me, TimeProvider clock, Microsoft.Extensions.Options.IOptions<NotifyOptions> options)
{
    private Guid Me => me.UserId ?? Guid.Empty;

    public async Task<InboxDto> ListAsync(CancellationToken ct)
    {
        var uid = Me;
        var unread = await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null, ct);
        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == uid).OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync(ct);
        return new InboxDto(unread, items.Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.Important, n.CreatedAt, n.ReadAt != null)).ToList());
    }

    public async Task ReadAsync(Guid? id, CancellationToken ct)
    {
        var uid = Me;
        var now = clock.GetUtcNow();
        var q = db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null);
        if (id is { } nid) q = q.Where(n => n.Id == nid);
        await q.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    public async Task<PrefsDto> PrefsAsync(CancellationToken ct)
    {
        var uid = Me;
        var p = await db.NotificationPrefs.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct) ?? new NotificationPrefs(uid);
        var linked = await db.MessengerLinks.AsNoTracking().Where(l => l.UserId == uid).Select(l => l.Messenger).ToListAsync(ct);
        var o = options.Value;
        return new PrefsDto(p.Channels, p.Muted, p.QuietFrom, p.QuietTo, linked, !string.IsNullOrEmpty(o.BaleBotUsername), !string.IsNullOrEmpty(o.TelegramBotUsername));
    }

    public async Task<PrefsDto> UpdatePrefsAsync(UpdatePrefsRequest r, CancellationToken ct)
    {
        var uid = Me;
        var p = await db.NotificationPrefs.FirstOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) { p = new NotificationPrefs(uid); db.NotificationPrefs.Add(p); }
        p.Update(r.Channels, r.Muted ?? [], r.QuietFrom, r.QuietTo);
        await db.SaveChangesAsync(ct);
        return await PrefsAsync(ct);
    }

    /// <summary>Deep link with a one-time 10-minute code: the bot links the chat that sends "/start CODE".</summary>
    public async Task<Result<LinkCodeDto>> LinkAsync(Messenger m, CancellationToken ct)
    {
        var o = options.Value;
        var bot = m == Messenger.Bale ? o.BaleBotUsername : o.TelegramBotUsername;
        if (string.IsNullOrEmpty(bot)) return Error.Validation("messenger.off", "این ربات هنوز راه‌اندازی نشده است.");
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        var expires = clock.GetUtcNow().AddMinutes(10);
        db.MessengerLinkCodes.Add(new MessengerLinkCode(Me, m, Hash(code), expires));
        await db.SaveChangesAsync(ct);
        var url = m == Messenger.Bale ? $"https://ble.ir/{bot}?start={code}" : $"https://t.me/{bot}?start={code}";
        return new LinkCodeDto(url, code, expires);
    }

    public async Task UnlinkAsync(Messenger m, CancellationToken ct)
    {
        var uid = Me;
        await db.MessengerLinks.Where(l => l.UserId == uid && l.Messenger == m).ExecuteDeleteAsync(ct);
    }

    public static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}

/// <summary>Updates from the bots (Bale webhook, Telegram via the gateway): "/start CODE" links, "/stop" unlinks. Nothing else is trusted.</summary>
public sealed class BotUpdateService(IAppDbContext db, TimeProvider clock)
{
    public sealed record Incoming(long UpdateId, long ChatId, string? Text);

    /// <returns>The reply to send back to that chat (or null).</returns>
    public async Task<string?> HandleAsync(Messenger m, Incoming u, CancellationToken ct) =>
        await ClaimAsync(m, u.UpdateId, ct) ? await HandleTextAsync(m, u.ChatId, u.Text, ct) : null;

    /// <summary>Each update once, even if the platform (or the gateway's queue) delivers it again.</summary>
    public async Task<bool> ClaimAsync(Messenger m, long updateId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO bot_updates (\"Messenger\", \"UpdateId\", \"At\") VALUES ({(int)m}, {updateId}, {clock.GetUtcNow()}) ON CONFLICT DO NOTHING", ct) > 0;

    public async Task<string?> HandleTextAsync(Messenger m, long chatId, string? rawText, CancellationToken ct)
    {
        var u = new Incoming(0, chatId, rawText);
        var text = (u.Text ?? "").Trim();
        if (text.StartsWith("/start", StringComparison.Ordinal))
        {
            var code = text.Length > 6 ? text[6..].Trim() : "";
            if (code.Length is 0 or > 64) return "سلام! برای اتصال، از پروفایل خودتان در کارنما دکمه‌ی «اتصال» را بزنید.";
            var now = clock.GetUtcNow();
            var hash = InboxService.Hash(code);
            var c = await db.MessengerLinkCodes.FirstOrDefaultAsync(x => x.CodeHash == hash && x.Messenger == m && x.UsedAt == null && x.ExpiresAt > now, ct);
            if (c is null) return "این کد معتبر نیست یا منقضی شده. از پروفایل دوباره «اتصال» را بزنید.";
            c.Use(now);
            await db.MessengerLinks.Where(l => (l.UserId == c.UserId && l.Messenger == m) || (l.ChatId == u.ChatId && l.Messenger == m)).ExecuteDeleteAsync(ct);
            db.MessengerLinks.Add(new MessengerLink(c.UserId, m, u.ChatId, now));
            await db.SaveChangesAsync(ct);
            return "✅ اتصال انجام شد. از این به بعد اعلان‌های کارنما را اینجا می‌گیرید. برای قطع: /stop";
        }
        if (text == "/stop")
        {
            await db.MessengerLinks.Where(l => l.ChatId == u.ChatId && l.Messenger == m).ExecuteDeleteAsync(ct);
            return "اتصال قطع شد.";
        }
        return null;
    }
}
