using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Application.Files;
using Karnama.Application.Notify;
using Karnama.Application.Work;
using Karnama.Domain.Chat;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Chat;

public sealed record ChatSettingsDto(bool WorkerToWorker, bool ClientChat)
{
    public static ChatSettingsDto Default => new(false, true);
}

public sealed record ConversationDto(Guid Id, ConversationKind Kind, string Title, Guid? OrderId, Guid? OtherUserId, bool OtherIsClient, string? LastPreview,
    DateTimeOffset LastMessageAt, int Unread, bool Muted, IReadOnlyList<MemberDto> Members);
public sealed record MemberDto(Guid Id, string Name, bool IsClient);
public sealed record MessageDto(long Id, Guid ConversationId, Guid AuthorId, string AuthorName, bool Mine, MessageKind Kind, string? Body, string? FileName, string? ContentType,
    long? FileSize, int? DurationSeconds, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, bool Deleted, bool CanEdit, Guid ClientKey);
public sealed record MessagesPage(IReadOnlyList<MessageDto> Items, long OthersReadUpTo, bool HasOlder);
public sealed record ContactDto(Guid Id, string Name, bool IsClient, string? Note);
public sealed record SendTextRequest(string Body, Guid ClientKey);
public sealed record EditMessageRequest(string Body);
public sealed record GroupRequest(string Title, List<Guid> Members);
public sealed record SearchHitDto(long Id, Guid ConversationId, string ConversationTitle, string AuthorName, string Snippet, DateTimeOffset CreatedAt);

/// <summary>
/// In-app chat (PROMPT2 §7). Managers (chat.groups.manage) talk to anyone; workers (chat.use) to managers, and to each
/// other only when the panel allows it; a client only to the managers — never to workers. Every read checks membership
/// (someone else's chat → 404). Live updates go only to the members' own channels.
/// </summary>
public sealed class ChatService(IAppDbContext db, ICurrentUser me, WorkAccess access, IFileStore files, TimeProvider clock, IChangeFeed changes,
    Notifier notifier, IAuditWriter audit)
{
    public const string SettingsKey = "chat";
    public const long MaxImageBytes = 3 * 1024 * 1024;
    public const long MaxVoiceBytes = 3 * 1024 * 1024;
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxVoiceSeconds = 120;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ImageKinds = ["png", "jpg", "webp", "gif"];
    private static readonly HashSet<string> VoiceKinds = ["webm", "ogg", "m4a", "mp4"];
    private static readonly HashSet<string> FileKinds = ["png", "jpg", "webp", "gif", "heic", "pdf", "zip", "mp4", "mov", "m4a", "mp3", "wav", "webm", "ogg", "psd"];
    private static Error NotFound => Error.NotFound("chat.notFound", "گفتگو پیدا نشد.");

    private Guid Me => me.UserId ?? Guid.Empty;
    private bool IsClient => me.Kind == UserKind.Client;
    private bool IsManager => me.Kind == UserKind.Staff && me.Has(Permissions.ChatGroupsManage, PermissionScope.All);
    private bool CanUse => IsManager || (me.Kind == UserKind.Staff && me.Has(Permissions.ChatUse, PermissionScope.Own)) || IsClient;

    // ---------------- settings ----------------

    public async Task<ChatSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingsKey, ct);
        if (row is null) return ChatSettingsDto.Default;
        try { return JsonSerializer.Deserialize<ChatSettingsDto>(row.Json, Json) ?? ChatSettingsDto.Default; }
        catch (JsonException) { return ChatSettingsDto.Default; }
    }

    public async Task<Result<ChatSettingsDto>> SaveSettingsAsync(ChatSettingsDto s, CancellationToken ct)
    {
        if (!me.Has(Permissions.SettingsManage, PermissionScope.All)) return Error.Forbidden();
        var json = JsonSerializer.Serialize(s, Json);
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == SettingsKey, ct);
        if (row is null) db.SiteSettings.Add(new SiteSetting(SettingsKey, json));
        else row.Replace(json, me.UserId, clock.GetUtcNow());
        audit.Add("chat.settingsUpdated", nameof(SiteSetting), SettingsKey, s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    // ---------------- who can talk to whom ----------------

    private async Task<List<Guid>> ManagersAsync(CancellationToken ct) => await notifier.StaffWithAsync(Permissions.ChatGroupsManage, PermissionScope.All, ct);

    /// <summary>People I may start a direct chat with.</summary>
    public async Task<Result<IReadOnlyList<ContactDto>>> ContactsAsync(CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var settings = await SettingsAsync(ct);
        var managers = await ManagersAsync(ct);
        IQueryable<User> q = db.Users.AsNoTracking().Where(u => u.IsActive && u.Id != Me);
        if (IsClient) q = q.Where(u => managers.Contains(u.Id));
        else if (!IsManager)
        {
            q = settings.WorkerToWorker
                ? q.Where(u => u.Kind == UserKind.Staff)
                : q.Where(u => managers.Contains(u.Id));
        }
        else if (!settings.ClientChat) q = q.Where(u => u.Kind == UserKind.Staff);
        var users = await q.OrderBy(u => u.Kind).ThenBy(u => u.DisplayName).Take(300).Select(u => new { u.Id, u.DisplayName, u.Kind }).ToListAsync(ct);
        var clientNames = await db.Clients.AsNoTracking().Where(c => c.PortalUserId != null).ToDictionaryAsync(c => c.PortalUserId!.Value, c => c.BusinessName, ct);
        return users.Where(u => u.Kind == UserKind.Staff || u.Kind == UserKind.Client)
            .Select(u => new ContactDto(u.Id, u.DisplayName, u.Kind == UserKind.Client, u.Kind == UserKind.Client ? clientNames.GetValueOrDefault(u.Id) : null)).ToList();
    }

    public async Task<Result<ConversationDto>> DirectAsync(Guid otherId, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var contacts = await ContactsAsync(ct);
        if (contacts.IsFailure || contacts.Value!.All(c => c.Id != otherId)) return Error.Forbidden();
        var key = Conversation.Key(Me, otherId);
        var conv = await db.Conversations.Include(c => c.Members).FirstOrDefaultAsync(c => c.DirectKey == key, ct);
        if (conv is null)
        {
            conv = Conversation.Direct(Me, otherId, clock.GetUtcNow());
            db.Conversations.Add(conv);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Both people opened the chat at the same moment: use the one that won.
                db.Conversations.Remove(conv);
                conv = await db.Conversations.Include(c => c.Members).FirstAsync(c => c.DirectKey == key, ct);
            }
        }
        return await ToDtoAsync(conv, ct);
    }

    public async Task<Result<ConversationDto>> CreateGroupAsync(GroupRequest r, CancellationToken ct)
    {
        if (!IsManager) return Error.Forbidden();
        var members = await StaffOnlyAsync(r.Members, ct);
        var g = Conversation.Group(r.Title, Me, members, clock.GetUtcNow());
        if (g.IsFailure) return g.Error!;
        db.Conversations.Add(g.Value!);
        audit.Add("chat.groupCreated", nameof(Conversation), g.Value!.Id.ToString(), new { r.Title, count = members.Count });
        await db.SaveChangesAsync(ct);
        await PingAsync(g.Value!, "members", ct);
        return await ToDtoAsync(g.Value!, ct);
    }

    public async Task<Result<ConversationDto>> UpdateGroupAsync(Guid id, GroupRequest r, CancellationToken ct)
    {
        if (!IsManager) return Error.Forbidden();
        var g = await db.Conversations.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == id && c.Kind == ConversationKind.Group, ct);
        if (g is null) return NotFound;
        var renamed = g.Rename(r.Title);
        if (renamed.IsFailure) return renamed.Error!;
        var wanted = (await StaffOnlyAsync(r.Members, ct)).Append(Me).ToHashSet();
        var removed = g.Members.Where(m => !wanted.Contains(m.UserId)).Select(m => m.UserId).ToList();
        foreach (var u in removed) g.RemoveMember(u);
        var now = clock.GetUtcNow();
        foreach (var u in wanted) g.AddMember(u, now);
        await db.SaveChangesAsync(ct);
        await PingAsync(g, "members", ct, removed);
        return await ToDtoAsync(g, ct);
    }

    /// <summary>Clients are never put in groups (they talk to the agency only through their own direct chat).</summary>
    private async Task<List<Guid>> StaffOnlyAsync(IEnumerable<Guid>? ids, CancellationToken ct)
    {
        var list = (ids ?? []).Distinct().Take(200).ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id) && u.IsActive && u.Kind == UserKind.Staff).Select(u => u.Id).ToListAsync(ct);
    }

    /// <summary>"About this work": the chat of an order, for staff who can see that order. Joining happens on open.</summary>
    public async Task<Result<ConversationDto>> ForOrderAsync(Guid orderId, CancellationToken ct)
    {
        if (!CanUse || IsClient) return Error.Forbidden();
        var order = await (await access.VisibleOrdersAsync(ct)).AsNoTracking().Where(o => o.Id == orderId).Select(o => new { o.Id, o.Number, o.Title }).FirstOrDefaultAsync(ct);
        if (order is null) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var conv = await db.Conversations.Include(c => c.Members).FirstOrDefaultAsync(c => c.OrderId == orderId, ct);
        var now = clock.GetUtcNow();
        if (conv is null)
        {
            conv = Conversation.ForOrder(orderId, $"#{order.Number} {order.Title}", now);
            db.Conversations.Add(conv);
            // Everyone already working on it, plus the managers, can find it in their list.
            var assignees = await db.WorkItems.AsNoTracking().Where(w => w.OrderId == orderId && w.AssigneeId != null).Select(w => w.AssigneeId!.Value).Distinct().ToListAsync(ct);
            foreach (var u in assignees.Append(Me)) conv.AddMember(u, now);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.Conversations.Remove(conv);
                conv = await db.Conversations.Include(c => c.Members).FirstAsync(c => c.OrderId == orderId, ct);
            }
        }
        if (conv.Members.All(m => m.UserId != Me))
        {
            conv.AddMember(Me, now);
            await db.SaveChangesAsync(ct);
        }
        return await ToDtoAsync(conv, ct);
    }

    // ---------------- reading ----------------

    public async Task<Result<IReadOnlyList<ConversationDto>>> ListAsync(CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var convs = await db.Conversations.AsNoTracking().Include(c => c.Members)
            .Where(c => c.Members.Any(m => m.UserId == Me) && (c.LastMessageId > 0 || c.Kind != ConversationKind.Direct || c.CreatedBy == Me))
            .OrderByDescending(c => c.LastMessageAt).Take(100).ToListAsync(ct);
        var result = new List<ConversationDto>();
        var names = await NameKindsAsync(convs.SelectMany(c => c.Members.Select(m => m.UserId)), ct);
        var reads = convs.ToDictionary(c => c.Id, c => c.Members.First(m => m.UserId == Me).LastReadId);
        var ids = convs.Select(c => c.Id).ToList();
        var unread = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId) && m.AuthorId != Me && m.DeletedAt == null)
            .Select(m => new { m.ConversationId, m.Id }).ToListAsync(ct);
        foreach (var c in convs)
            result.Add(Dto(c, names, unread.Count(u => u.ConversationId == c.Id && u.Id > reads[c.Id])));
        return result;
    }

    public async Task<int> UnreadTotalAsync(CancellationToken ct)
    {
        if (!CanUse) return 0;
        return await (from m in db.ChatMessages
                      join cm in db.ConversationMembers on m.ConversationId equals cm.ConversationId
                      where cm.UserId == Me && m.AuthorId != Me && m.DeletedAt == null && m.Id > cm.LastReadId
                      select m.Id).CountAsync(ct);
    }

    public async Task<Result<MessagesPage>> MessagesAsync(Guid conversationId, long? after, long? before, int limit, CancellationToken ct)
    {
        var conv = await MineAsync(conversationId, ct);
        if (conv is null) return NotFound;
        limit = Math.Clamp(limit, 1, 100);
        var q = db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        List<ChatMessage> rows;
        bool hasOlder;
        if (after is { } a)
        {
            // Bandwidth (§7): only what is new since the last message the device has.
            rows = await q.Where(m => m.Id > a).OrderBy(m => m.Id).Take(limit).ToListAsync(ct);
            hasOlder = false;
        }
        else
        {
            var older = before is { } b ? q.Where(m => m.Id < b) : q;
            rows = await older.OrderByDescending(m => m.Id).Take(limit + 1).ToListAsync(ct);
            hasOlder = rows.Count > limit;
            rows = rows.Take(limit).OrderBy(m => m.Id).ToList();
        }
        var names = await NamesAsync(rows.Select(r => r.AuthorId), ct);
        var othersRead = conv.Members.Where(m => m.UserId != Me).Select(m => m.LastReadId).DefaultIfEmpty(0).Max();
        var now = clock.GetUtcNow();
        return new MessagesPage(rows.Select(m => ToDto(m, names, now)).ToList(), othersRead, hasOlder);
    }

    public async Task<Result> ReadAsync(Guid conversationId, long upTo, CancellationToken ct)
    {
        var conv = await db.Conversations.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == conversationId && c.Members.Any(m => m.UserId == Me), ct);
        if (conv is null) return NotFound;
        var mine = conv.Members.First(m => m.UserId == Me);
        var before = mine.LastReadId;
        mine.Read(Math.Min(upTo, conv.LastMessageId));
        if (mine.LastReadId == before) return Result.Success();
        await db.SaveChangesAsync(ct);
        // "Seen" ticks for the others.
        await PingAsync(conv, "read", ct, exclude: Me);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SearchHitDto>>> SearchAsync(string q, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var text = (q ?? "").Trim();
        if (text.Length < 2) return Result.Success<IReadOnlyList<SearchHitDto>>([]);
        var like = Keyset.Like(text);
        var hits = await (from m in db.ChatMessages.AsNoTracking()
                          join cm in db.ConversationMembers on m.ConversationId equals cm.ConversationId
                          where cm.UserId == Me && m.DeletedAt == null && m.Body != null && EF.Functions.ILike(m.Body, like)
                          orderby m.Id descending
                          select m).Take(30).ToListAsync(ct);
        var convIds = hits.Select(h => h.ConversationId).Distinct().ToList();
        var convs = await db.Conversations.AsNoTracking().Include(c => c.Members).Where(c => convIds.Contains(c.Id)).ToListAsync(ct);
        var names = await NamesAsync(hits.Select(h => h.AuthorId).Concat(convs.SelectMany(c => c.Members.Select(m => m.UserId))), ct);
        return hits.Select(h =>
        {
            var c = convs.First(x => x.Id == h.ConversationId);
            return new SearchHitDto(h.Id, h.ConversationId, TitleOf(c, names), names.GetValueOrDefault(h.AuthorId, "—"), Snippet(h.Body!, text), h.CreatedAt);
        }).ToList();
    }

    // ---------------- writing ----------------

    public async Task<Result<MessageDto>> SendTextAsync(Guid conversationId, SendTextRequest r, CancellationToken ct)
    {
        var conv = await MineTrackedAsync(conversationId, ct);
        if (conv is null) return NotFound;
        if (await ExistingAsync(conversationId, r.ClientKey, ct) is { } again) return again;
        var m = ChatMessage.Text(conversationId, Me, r.Body, r.ClientKey == Guid.Empty ? Guid.CreateVersion7() : r.ClientKey, clock.GetUtcNow());
        if (m.IsFailure) return m.Error!;
        return await StoreAsync(conv, m.Value!, ct);
    }

    public async Task<Result<MessageDto>> UploadAsync(Guid conversationId, Stream content, long length, string fileName, bool voice, int? duration, string? caption,
        Guid clientKey, CancellationToken ct)
    {
        var conv = await MineTrackedAsync(conversationId, ct);
        if (conv is null) return NotFound;
        if (await ExistingAsync(conversationId, clientKey, ct) is { } again) return again;
        if (length <= 0 || length > MaxFileBytes)
            return Error.Validation("chat.fileTooLarge", "فایل تا ۲۰ مگابایت؛ فایل بزرگ‌تر را از ربات تلگرام بفرستید.");

        var at = clock.GetUtcNow();
        var key = $"{at:yyyy}/{at:MM}/{Guid.NewGuid():N}";   // same private store as order files (never under the web root)
        await files.AppendAsync(key, 0, content, length, ct);
        var kind = FileService.Sniff(await files.ReadHeadAsync(key, 32, ct));
        MessageKind messageKind;
        string? error = null;
        if (voice)
        {
            messageKind = MessageKind.Voice;
            if (kind is null || !VoiceKinds.Contains(kind)) error = "فقط پیام صوتی ضبط‌شده در اپ قبول می‌شود.";
            else if (length > MaxVoiceBytes || duration is null or <= 0 or > MaxVoiceSeconds) error = "پیام صوتی حداکثر ۲ دقیقه است.";
        }
        else if (kind is not null && ImageKinds.Contains(kind))
        {
            messageKind = MessageKind.Image;
            if (length > MaxImageBytes) error = "عکس حداکثر ۳ مگابایت است (اپ خودش عکس را کوچک می‌کند).";
        }
        else
        {
            messageKind = MessageKind.File;
            if (kind is null || !FileKinds.Contains(kind)) error = "این نوع فایل قبول نمی‌شود (عکس، ویدیو، صدا، PDF، فایل‌های آفیس و ZIP).";
        }
        if (error is not null)
        {
            await files.DeleteAsync(key, ct);
            return Error.Validation("chat.fileType", error);
        }
        var name = Path.GetFileName(fileName ?? "file");
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var contentType = kind switch
        {
            "png" => "image/png", "jpg" => "image/jpeg", "webp" => "image/webp", "gif" => "image/gif", "pdf" => "application/pdf",
            "webm" => "audio/webm", "ogg" => "audio/ogg", "m4a" => "audio/mp4", "mp3" => "audio/mpeg", "wav" => "audio/wav",
            "mp4" => voice ? "audio/mp4" : "video/mp4", "mov" => "video/quicktime",
            _ => "application/octet-stream",
        };
        var m = ChatMessage.Attachment(conversationId, Me, messageKind, key, name, contentType, length, voice ? duration : null, caption,
            clientKey == Guid.Empty ? Guid.CreateVersion7() : clientKey, clock.GetUtcNow());
        return await StoreAsync(conv, m, ct);
    }

    public async Task<Result<(ChatMessage Message, Stream Content)>> OpenFileAsync(long messageId, CancellationToken ct)
    {
        var m = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (m is null || m.FileKey is null || m.DeletedAt is not null || await MineAsync(m.ConversationId, ct) is null) return NotFound;
        return (m, files.OpenRead(m.FileKey));
    }

    public async Task<Result<MessageDto>> EditAsync(long messageId, EditMessageRequest r, CancellationToken ct)
    {
        var m = await db.ChatMessages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (m is null || await MineAsync(m.ConversationId, ct) is not { } conv) return NotFound;
        var now = clock.GetUtcNow();
        var edited = m.Edit(Me, r.Body, now);
        if (edited.IsFailure) return edited.Error!;
        if (conv.LastMessageId == m.Id)
            await db.Conversations.Where(c => c.Id == conv.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastPreview, m.Preview()), ct);
        await db.SaveChangesAsync(ct);
        await PingAsync(conv, "edit", ct);
        return ToDto(m, await NamesAsync([m.AuthorId], ct), now);
    }

    public async Task<Result> DeleteAsync(long messageId, CancellationToken ct)
    {
        var m = await db.ChatMessages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (m is null || await MineAsync(m.ConversationId, ct) is not { } conv) return NotFound;
        var deleted = m.Delete(Me, IsManager && conv.Kind != ConversationKind.Direct, clock.GetUtcNow());
        if (deleted.IsFailure) return deleted;
        if (conv.LastMessageId == m.Id)
            await db.Conversations.Where(c => c.Id == conv.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastPreview, m.Preview()), ct);
        audit.Add("chat.messageDeleted", nameof(ChatMessage), m.Id.ToString(), new { m.ConversationId, byAuthor = m.AuthorId == Me });
        await db.SaveChangesAsync(ct);
        await PingAsync(conv, "delete", ct);
        return Result.Success();
    }

    public async Task<Result> MuteAsync(Guid conversationId, bool muted, CancellationToken ct)
    {
        var member = await db.ConversationMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == Me, ct);
        if (member is null) return NotFound;
        member.Mute(muted);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>"Typing…": who should hear it (the other members), or null if I'm not a member.</summary>
    public async Task<IReadOnlyList<Guid>?> TypingTargetsAsync(Guid conversationId, CancellationToken ct)
    {
        var conv = await MineAsync(conversationId, ct);
        return conv?.Members.Where(m => m.UserId != Me).Select(m => m.UserId).ToList();
    }

    // ---------------- helpers ----------------

    private async Task<Conversation?> MineAsync(Guid id, CancellationToken ct) =>
        CanUse ? await db.Conversations.AsNoTracking().Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == id && c.Members.Any(m => m.UserId == Me), ct) : null;

    private async Task<Conversation?> MineTrackedAsync(Guid id, CancellationToken ct) =>
        CanUse ? await db.Conversations.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == id && c.Members.Any(m => m.UserId == Me), ct) : null;

    private async Task<MessageDto?> ExistingAsync(Guid conversationId, Guid clientKey, CancellationToken ct)
    {
        if (clientKey == Guid.Empty) return null;
        var m = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.ClientKey == clientKey && x.AuthorId == Me, ct);
        return m is null ? null : ToDto(m, await NamesAsync([m.AuthorId], ct), clock.GetUtcNow());
    }

    private async Task<Result<MessageDto>> StoreAsync(Conversation conv, ChatMessage m, CancellationToken ct)
    {
        db.ChatMessages.Add(m);
        await db.SaveChangesAsync(ct);   // the id is needed for "last message" and read marks
        conv.Touch(m);
        conv.Members.First(x => x.UserId == Me).Read(m.Id);
        var names = await NamesAsync(conv.Members.Select(x => x.UserId), ct);
        var author = names.GetValueOrDefault(Me, "");
        var bucket = m.CreatedAt.ToUnixTimeSeconds() / 3600;
        // One bell/bot notice per chat per hour per person at most; muted chats stay quiet.
        foreach (var member in conv.Members.Where(x => x.UserId != Me && !x.Muted))
            await notifier.NotifyAsync([member.UserId], "chat.message", conv.Kind == ConversationKind.Direct ? $"پیام از {author}" : $"{author} در «{TitleOf(conv, names)}»",
                m.Preview(), $"/chat?c={conv.Id}", false, $"chat:{conv.Id:N}:{member.UserId:N}:{bucket}", ct);
        await db.SaveChangesAsync(ct);
        await PingAsync(conv, "message", ct);
        return ToDto(m, names, clock.GetUtcNow());
    }

    /// <summary>Live: one private change per member (ids only), so nobody else's screen learns about this chat.</summary>
    private async Task PingAsync(Conversation conv, string field, CancellationToken ct, IEnumerable<Guid>? alsoNotify = null, Guid? exclude = null)
    {
        foreach (var u in conv.Members.Select(m => m.UserId).Concat(alsoNotify ?? []).Distinct().Where(u => u != exclude))
            changes.Record("chat", conv.Id, null, u, null, field);
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
    }

    private async Task<Dictionary<Guid, (string Name, bool Client)>> NameKindsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName, u.Kind }).ToListAsync(ct);
        var clientIds = users.Where(u => u.Kind == UserKind.Client).Select(u => (Guid?)u.Id).ToList();
        var businesses = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.PortalUserId)).ToDictionaryAsync(c => c.PortalUserId!.Value, c => c.BusinessName, ct);
        return users.ToDictionary(u => u.Id, u => (u.Kind == UserKind.Client && businesses.TryGetValue(u.Id, out var b) ? $"{u.DisplayName} ({b})" : u.DisplayName, u.Kind == UserKind.Client));
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await NameKindsAsync(ids, ct)).ToDictionary(k => k.Key, k => k.Value.Name);

    private string TitleOf(Conversation c, Dictionary<Guid, string> names) => c.Kind switch
    {
        ConversationKind.Direct => names.GetValueOrDefault(c.Members.FirstOrDefault(m => m.UserId != Me)?.UserId ?? Guid.Empty, "—"),
        _ => c.Title ?? "—",
    };

    private async Task<ConversationDto> ToDtoAsync(Conversation c, CancellationToken ct)
    {
        var names = await NameKindsAsync(c.Members.Select(m => m.UserId), ct);
        var read = c.Members.FirstOrDefault(m => m.UserId == Me)?.LastReadId ?? 0;
        var unread = await db.ChatMessages.CountAsync(m => m.ConversationId == c.Id && m.AuthorId != Me && m.DeletedAt == null && m.Id > read, ct);
        return Dto(c, names, unread);
    }

    private ConversationDto Dto(Conversation c, Dictionary<Guid, (string Name, bool Client)> kinds, int unread)
    {
        var names = kinds.ToDictionary(k => k.Key, k => k.Value.Name);
        var other = c.Kind == ConversationKind.Direct ? c.Members.FirstOrDefault(m => m.UserId != Me)?.UserId : null;
        var mine = c.Members.FirstOrDefault(m => m.UserId == Me);
        return new ConversationDto(c.Id, c.Kind, TitleOf(c, names), c.OrderId, other, other is { } o && kinds.TryGetValue(o, out var ok) && ok.Client, c.LastPreview,
            c.LastMessageAt, unread, mine?.Muted ?? false,
            c.Members.Select(m => new MemberDto(m.UserId, names.GetValueOrDefault(m.UserId, "—"), kinds.TryGetValue(m.UserId, out var k) && k.Client)).ToList());
    }

    private MessageDto ToDto(ChatMessage m, Dictionary<Guid, string> names, DateTimeOffset now)
    {
        var deleted = m.DeletedAt is not null;
        return new MessageDto(m.Id, m.ConversationId, m.AuthorId, names.GetValueOrDefault(m.AuthorId, "—"), m.AuthorId == Me, m.Kind,
            deleted ? null : m.Body, deleted ? null : m.FileName, deleted ? null : m.ContentType, deleted ? null : m.FileSize, m.DurationSeconds,
            m.CreatedAt, m.EditedAt, deleted, !deleted && m.AuthorId == Me && m.Kind == MessageKind.Text && now - m.CreatedAt <= ChatMessage.EditWindow, m.ClientKey);
    }

    private static string Snippet(string body, string q)
    {
        var i = body.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || body.Length <= 120) return body.Length <= 120 ? body : body[..120] + "…";
        var start = Math.Max(0, i - 40);
        var len = Math.Min(body.Length - start, 120);
        return (start > 0 ? "…" : "") + body.Substring(start, len) + (start + len < body.Length ? "…" : "");
    }
}
