using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Application.Common;
using Karnama.Application.Notify;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Media;
using Karnama.Domain.Notify;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Media;

public sealed record MediaDto(Guid Id, Messenger Messenger, MediaKind Kind, Guid SenderId, string SenderName, long? FileSize, int? DurationSeconds, string? FileName,
    string? Caption, DateTimeOffset SentAt, DateTimeOffset ReceivedAt, Guid? WorkItemId, string? WorkTitle, long? OrderNumber, string? ClientName, MediaStatus Status,
    Guid? AssignedToId, string? AssignedToName, DateTimeOffset? AssignedAt, DateTimeOffset? DeliveredAt, string? Error, bool HasThumb, bool Archived, uint Version);

public sealed record AssignMediaRequest(Guid? WorkItemId, Guid? ToUserId, bool ToClient);
public sealed record MediaTargetDto(Guid Id, string Name, bool Linked);
public sealed record MediaWorkDto(Guid Id, string Title, long OrderNumber, string ClientName, Guid? AssigneeId, DateTimeOffset? Deadline, bool ClientLinked);
public sealed record MediaTargetsDto(IReadOnlyList<MediaTargetDto> Staff, IReadOnlyList<MediaWorkDto> Work);

/// <summary>A queued Bot API call (outbox row payload) and what to do with its answer.</summary>
public sealed record BotCallPayload(Messenger Messenger, string Method, JsonObject Args, string Purpose, Guid? MediaId);

/// <summary>
/// The files bot (PROMPT2 §8): staff send a file to the bot → it is recorded (file_id only; the bytes stay in the
/// messenger), copied to the private archive channel, copied to the admins with "assign" buttons, and the sender is asked
/// which work it belongs to. An admin assigns it (panel or the bot's buttons) → the bot copies it to that worker or the
/// client. Unlinked senders get one line back and nothing is stored.
/// </summary>
public sealed class MediaService(
    IAppDbContext db, ICurrentUser me, TimeProvider clock, Notifier notifier, IChangeFeed changes, IAuditWriter audit,
    IOptions<NotifyOptions> options, IMediaStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Error NotFound => Error.NotFound("media.notFound", "فایل پیدا نشد.");

    // ===================== bot side =====================

    public bool FilesEnabled(Messenger m) => m == Messenger.Telegram || options.Value.BaleFiles;

    /// <summary>Handles a file or a button press. Returns false for anything else (text commands go to <see cref="BotUpdateService"/>).</summary>
    public async Task<bool> HandleAsync(Messenger m, ParsedUpdate u, CancellationToken ct)
    {
        if (u.Callback is { } cb) { await CallbackAsync(m, u.ChatId, cb, ct); return true; }
        if (u.Media is not { } media) return false;

        var link = await db.MessengerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Messenger == m && l.ChatId == u.ChatId, ct);
        var now = clock.GetUtcNow();
        if (link is null)
        {
            // §8.3 step 2: unknown sender → one line back, nothing recorded.
            Reply(m, u.ChatId, "اول حساب کارنمای خودتان را وصل کنید: پروفایل ← «اتصال تلگرام». بعد فایل را دوباره بفرستید.", $"unlinked:{u.UpdateId}");
            await db.SaveChangesAsync(ct);
            return true;
        }
        if (!FilesEnabled(m))
        {
            Reply(m, u.ChatId, "فعلاً فایل را از تلگرام یا داخل اپ کارنما بفرستید.", $"nofiles:{u.UpdateId}");
            await db.SaveChangesAsync(ct);
            return true;
        }

        var duplicate = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Messenger == m && x.FileUniqueId == media.FileUniqueId, ct);
        if (duplicate is not null)
        {
            Reply(m, u.ChatId, $"این فایل قبلاً در {Clock(duplicate.ReceivedAt)} ثبت شده است؛ دوباره ثبت نشد.", $"dup:{u.UpdateId}");
            await db.SaveChangesAsync(ct);
            return true;
        }

        var sender = await db.Users.AsNoTracking().Where(x => x.Id == link.UserId && x.IsActive).Select(x => new { x.Id, x.DisplayName, x.Kind }).FirstOrDefaultAsync(ct);
        if (sender is null) return true;
        var asset = MediaAsset.Receive(m, sender.Id, media.Kind, media.FileId, media.FileUniqueId, media.Size, media.Duration, media.FileName, media.MimeType,
            u.Caption, media.ThumbFileId, u.Date, now, u.ChatId, u.MessageId);

        // §8.3 step 4: a running timer ties the file to that work.
        var timer = await db.WorkTimers.AsNoTracking().Where(t => t.UserId == sender.Id && t.StoppedAt == null).OrderByDescending(t => t.StartedAt).FirstOrDefaultAsync(ct);
        WorkItem? timerWork = null;
        if (timer is not null)
        {
            timerWork = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == timer.WorkItemId, ct);
            if (timerWork is not null) asset.LinkWork(timerWork.Id, timerWork.ClientId);
        }
        db.MediaAssets.Add(asset);
        audit.Add("media.received", nameof(MediaAsset), asset.Id.ToString(), new { kind = media.Kind.ToString(), size = media.Size }, actorOverride: sender.Id);

        if (ArchiveChat(m) is { } archive)
            Queue(m, "copyMessage", new JsonObject { ["chat_id"] = archive, ["from_chat_id"] = u.ChatId, ["message_id"] = u.MessageId }, "archive", asset.Id, $"archive:{asset.Id:N}");
        else
            await QueueAdminCopiesAsync(asset, sender.DisplayName, timerWork?.Title, ct);

        // §8.3 step 5: "which work is this for?" with the sender's open work as buttons.
        var open = await db.WorkItems.AsNoTracking()
            .Where(w => w.AssigneeId == sender.Id && w.Status != WorkStatus.Done && w.Status != WorkStatus.Cancelled && w.Status != WorkStatus.Waiting)
            .OrderBy(w => w.Deadline ?? DateTimeOffset.MaxValue).Take(8).Select(w => new { w.Id, w.Title, w.OrderNumber }).ToListAsync(ct);
        var rows = new JsonArray();
        foreach (var w in open)
            rows.Add(new JsonArray(Button($"{Short(w.Title, 40)} (#{Fa(w.OrderNumber)})", $"w|{ShortId(asset.Id)}|{ShortId(w.Id)}")));
        rows.Add(new JsonArray(Button("بعداً در کارنما", $"l|{ShortId(asset.Id)}")));
        var text = timerWork is null
            ? "✅ فایل دریافت شد. این فایل برای کدام کار است؟"
            : $"✅ فایل دریافت شد و به کار «{timerWork.Title}» (تایمر روشن) وصل شد. اگر برای کار دیگری است، انتخاب کنید:";
        Queue(m, "sendMessage", new JsonObject { ["chat_id"] = u.ChatId, ["text"] = text, ["reply_to_message_id"] = u.MessageId, ["reply_markup"] = new JsonObject { ["inline_keyboard"] = rows } },
            "reply", asset.Id, $"ask:{asset.Id:N}");

        var inbox = await notifier.StaffWithAsync(Permissions.MediaInbox, PermissionScope.All, ct);
        await notifier.NotifyAsync(inbox.Where(x => x != sender.Id), "media.received", $"فایل تازه از {sender.DisplayName}", asset.Caption, "/media", false, $"media:{asset.Id:N}", ct);
        changes.Record("media", asset.Id, null, sender.Id, null, "new");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return true;
    }

    private async Task CallbackAsync(Messenger m, long chatId, ParsedCallback cb, CancellationToken ct)
    {
        var link = await db.MessengerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Messenger == m && l.ChatId == chatId, ct);
        var parts = cb.Data.Split('|');
        string answer;
        if (link is null || parts.Length < 2 || FromShort(parts[1]) is not { } mediaId) answer = "این دکمه دیگر معتبر نیست.";
        else answer = parts[0] switch
        {
            "w" when parts.Length == 3 && FromShort(parts[2]) is { } workId => await SenderPicksWorkAsync(link.UserId, mediaId, workId, ct),
            "l" => "باشد؛ بعداً در کارنما کار را انتخاب کنید.",
            "a" => await OfferTargetsAsync(m, chatId, link.UserId, mediaId, ct),
            "g" when parts.Length == 3 && FromShort(parts[2]) is { } to => await AssignFromBotAsync(link.UserId, mediaId, to, ct),
            _ => "این دکمه دیگر معتبر نیست.",
        };
        Queue(m, "answerCallbackQuery", new JsonObject { ["callback_query_id"] = cb.Id, ["text"] = answer }, "reply", null, $"cb:{cb.Id}");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
    }

    private async Task<string> SenderPicksWorkAsync(Guid userId, Guid mediaId, Guid workId, CancellationToken ct)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == mediaId && x.SenderId == userId, ct);
        var work = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId && w.AssigneeId == userId, ct);
        if (asset is null || work is null) return "این دکمه دیگر معتبر نیست.";
        asset.LinkWork(work.Id, work.ClientId);
        changes.Record("media", asset.Id, null, userId, null, "work");
        return $"ثبت شد: «{Short(work.Title, 60)}»";
    }

    private async Task<string> OfferTargetsAsync(Messenger m, long chatId, Guid userId, Guid mediaId, CancellationToken ct)
    {
        if (!await HasAsync(userId, Permissions.MediaAssign, ct)) return "اجازه‌ی سپردن فایل را ندارید.";
        if (!await db.MediaAssets.AnyAsync(x => x.Id == mediaId, ct)) return "فایل پیدا نشد.";
        var linked = await (from l in db.MessengerLinks
                            join u in db.Users on l.UserId equals u.Id
                            where l.Messenger == m && u.IsActive && u.Kind == UserKind.Staff
                            orderby u.DisplayName
                            select new { u.Id, u.DisplayName }).Take(12).ToListAsync(ct);
        var rows = new JsonArray();
        foreach (var u in linked) rows.Add(new JsonArray(Button(u.DisplayName, $"g|{ShortId(mediaId)}|{ShortId(u.Id)}")));
        Queue(m, "sendMessage", new JsonObject { ["chat_id"] = chatId, ["text"] = linked.Count == 0 ? "هیچ کارگری ربات را وصل نکرده است." : "به چه کسی سپرده شود؟", ["reply_markup"] = new JsonObject { ["inline_keyboard"] = rows } },
            "reply", mediaId, $"targets:{mediaId:N}:{userId:N}:{clock.GetUtcNow().ToUnixTimeSeconds()}");
        return "یک نفر را انتخاب کنید.";
    }

    private async Task<string> AssignFromBotAsync(Guid adminId, Guid mediaId, Guid to, CancellationToken ct)
    {
        if (!await HasAsync(adminId, Permissions.MediaAssign, ct)) return "اجازه‌ی سپردن فایل را ندارید.";
        var asset = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == mediaId, ct);
        if (asset is null) return "فایل پیدا نشد.";
        var r = await AssignCoreAsync(asset, adminId, to, ct);
        return r.IsSuccess ? "سپرده شد؛ فایل برایش فرستاده می‌شود." : r.Error!.Message;
    }

    // ===================== panel =====================

    public async Task<Result<KeysetPage<MediaDto>>> ListAsync(KeysetQuery q, MediaStatus? status, bool openOnly, CancellationToken ct)
    {
        if (!me.Has(Permissions.MediaInbox, PermissionScope.All)) return Error.Forbidden();
        var query = db.MediaAssets.AsNoTracking();
        if (status is { } s) query = query.Where(x => x.Status == s);
        else if (openOnly) query = query.Where(x => x.Status != MediaStatus.Delivered);
        var page = await Keyset.PageAsync(query, q, x => x, x => x.Id, ct);
        return new KeysetPage<MediaDto>(await ToDtosAsync(page.Items, ct), page.NextCursor);
    }

    public async Task<Result<MediaDto>> GetAsync(Guid id, CancellationToken ct)
    {
        if (!me.Has(Permissions.MediaInbox, PermissionScope.All)) return Error.Forbidden();
        var a = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? NotFound : (await ToDtosAsync([a], ct))[0];
    }

    /// <summary>
    /// Receivers for the assign sheet. With <paramref name="mediaId"/>, "linked" means linked on that file's messenger
    /// (the bot can only copy a file inside the messenger it came from); without it, linked on any messenger.
    /// </summary>
    public async Task<Result<MediaTargetsDto>> TargetsAsync(Guid? mediaId, CancellationToken ct)
    {
        if (!me.Has(Permissions.MediaAssign, PermissionScope.All)) return Error.Forbidden();
        Messenger? messenger = null;
        if (mediaId is { } mid)
        {
            messenger = await db.MediaAssets.AsNoTracking().Where(x => x.Id == mid).Select(x => (Messenger?)x.Messenger).FirstOrDefaultAsync(ct);
            if (messenger is null) return NotFound;
        }
        var links = db.MessengerLinks.AsNoTracking();
        if (messenger is { } m) links = links.Where(l => l.Messenger == m);
        var linked = await links.Select(l => l.UserId).Distinct().ToListAsync(ct);
        var staff = await db.Users.AsNoTracking().Where(u => u.IsActive && u.Kind == UserKind.Staff).OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName }).Take(200).ToListAsync(ct);
        var work = await db.WorkItems.AsNoTracking().Where(w => w.Status != WorkStatus.Done && w.Status != WorkStatus.Cancelled)
            .OrderByDescending(w => w.Id).Take(100).Select(w => new { w.Id, w.Title, w.OrderNumber, w.ClientId, w.AssigneeId, w.Deadline }).ToListAsync(ct);
        var clientIds = work.Select(w => w.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).Select(c => new { c.Id, c.BusinessName, c.PortalUserId }).ToDictionaryAsync(c => c.Id, ct);
        return new MediaTargetsDto(
            staff.Select(u => new MediaTargetDto(u.Id, u.DisplayName, linked.Contains(u.Id))).ToList(),
            work.Select(w =>
            {
                var c = clients.GetValueOrDefault(w.ClientId);
                return new MediaWorkDto(w.Id, w.Title, w.OrderNumber, c?.BusinessName ?? "—", w.AssigneeId, w.Deadline, c?.PortalUserId is { } p && linked.Contains(p));
            }).ToList());
    }

    /// <summary>§8.3 step 7: pick the work and the person; the bot copies the file to them inside the messenger.</summary>
    public async Task<Result<MediaDto>> AssignAsync(Guid id, AssignMediaRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.MediaAssign, PermissionScope.All) || me.UserId is not { } uid) return Error.Forbidden();
        var asset = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null) return NotFound;
        if (r.WorkItemId is { } wid)
        {
            var work = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid, ct);
            if (work is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
            asset.LinkWork(work.Id, work.ClientId);
        }
        Guid target;
        if (r.ToClient)
        {
            if (asset.ClientId is not { } clientId) return Error.Validation("media.noWork", "اول کار را انتخاب کنید تا مشتری معلوم شود.");
            var portal = await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => c.PortalUserId).FirstOrDefaultAsync(ct);
            if (portal is not { } p) return Error.Validation("media.clientNoPortal", "این مشتری پنل ندارد.");
            target = p;
        }
        else if (r.ToUserId is { } to)
        {
            // A person is staff only: a client receives through toClient, which resolves the client of this file's work
            // (never someone else's client by a mis-click).
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == to && u.Kind == UserKind.Staff, ct))
                return Error.Validation("media.staffOnly", "فایل را فقط به همکاران یا به مشتری همان کار می‌شود فرستاد.");
            target = to;
        }
        else return Error.Validation("media.noTarget", "گیرنده را انتخاب کنید.");

        var result = await AssignCoreAsync(asset, uid, target, ct);
        if (result.IsFailure) return result.Error!;
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await ToDtosAsync([asset], ct))[0];
    }

    private async Task<Result> AssignCoreAsync(MediaAsset asset, Guid by, Guid to, CancellationToken ct)
    {
        var target = await db.Users.AsNoTracking().Where(u => u.Id == to && u.IsActive).Select(u => new { u.Id, u.DisplayName, u.Kind }).FirstOrDefaultAsync(ct);
        if (target is null) return Error.NotFound("users.notFound", "کاربر پیدا نشد.");
        var link = await db.MessengerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == to && l.Messenger == asset.Messenger, ct);
        if (link is null) return Error.Validation("media.notLinked", $"{target.DisplayName} ربات {(asset.Messenger == Messenger.Telegram ? "تلگرام" : "بله")} را وصل نکرده است.");
        var now = clock.GetUtcNow();
        var assigned = asset.Assign(to, by, now);
        if (assigned.IsFailure) return assigned;

        WorkItem? work = asset.WorkItemId is { } wid ? await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid, ct) : null;
        var assigner = await db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "";
        // Captions never carry the client's phone number (§8.4).
        var caption = work is null ? $"📎 فایل از طرف {assigner}" : $"📎 برای کار «{Short(work.Title, 80)}» (سفارش #{Fa(work.OrderNumber)})"
            + (work.Deadline is { } dl ? $"، ددلاین {JalaliDate(dl)}" : "") + $"\nاز طرف {assigner}";
        var (fromChat, fromMessage) = asset.CopySource;
        Queue(asset.Messenger, "copyMessage", new JsonObject { ["chat_id"] = link.ChatId, ["from_chat_id"] = fromChat, ["message_id"] = fromMessage, ["caption"] = caption },
            "deliver", asset.Id, $"deliver:{asset.Id:N}:{to:N}:{now.ToUnixTimeSeconds()}");
        audit.Add("media.assigned", nameof(MediaAsset), asset.Id.ToString(), new { to, work = asset.WorkItemId }, actorOverride: by);
        if (target.Kind == UserKind.Staff)
            await notifier.NotifyAsync([to], "media.assigned", work is null ? "یک فایل برایتان فرستاده شد" : $"فایل کار «{work.Title}» برایتان فرستاده شد",
                "در ربات پیام‌رسان ببینید.", "/work", false, $"media-assigned:{asset.Id:N}:{to:N}:{now.ToUnixTimeSeconds()}", ct);
        changes.Record("media", asset.Id, null, asset.SenderId, to, "status");
        return Result.Success();
    }

    public async Task<Result<(Stream Stream, string ContentType)>> ThumbnailAsync(Guid id, CancellationToken ct)
    {
        if (!me.Has(Permissions.MediaInbox, PermissionScope.All)) return Error.Forbidden();
        var path = await db.MediaAssets.AsNoTracking().Where(x => x.Id == id).Select(x => x.ThumbPath).FirstOrDefaultAsync(ct);
        if (path is null || store.OpenPrivate(path) is not { } file) return NotFound;
        return file;
    }

    // ===================== outbox results =====================

    /// <summary>Called by the outbox dispatcher when a bot call finished for good (success, or no more retries).</summary>
    public async Task ApplyResultAsync(BotCallPayload p, bool ok, string? resultJson, string? error, CancellationToken ct)
    {
        if (p.MediaId is not { } id) return;
        var asset = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null) return;
        var now = clock.GetUtcNow();
        switch (p.Purpose)
        {
            case "archive":
                if (ok && MessageId(resultJson) is { } archived && ArchiveChat(p.Messenger) is { } chat)
                {
                    asset.Archived(chat, archived);
                    if (asset.ThumbFileId is not null)
                        Queue(p.Messenger, "thumb", new JsonObject { ["file_id"] = asset.ThumbFileId }, "thumb", asset.Id, $"thumb:{asset.Id:N}");
                }
                var sender = await db.Users.AsNoTracking().Where(u => u.Id == asset.SenderId).Select(u => u.DisplayName).FirstAsync(ct);
                var title = asset.WorkItemId is { } w ? await db.WorkItems.AsNoTracking().Where(x => x.Id == w).Select(x => x.Title).FirstOrDefaultAsync(ct) : null;
                // Admins get their copy even when archiving failed (from the sender's chat instead).
                await QueueAdminCopiesAsync(asset, sender, title, ct);
                break;
            case "admin":
                if (ok) asset.SentToAdmins();
                break;
            case "deliver":
                if (ok) asset.Delivered(now);
                else
                {
                    asset.Failed(error ?? "send failed");
                    if (asset.AssignedById is { } by)
                        await notifier.NotifyAsync([by], "media.failed", "فایل به گیرنده نرسید", "احتمالاً ربات را بلاک کرده یا Start نکرده است. از صندوق فایل‌ها دوباره بسپارید.",
                            "/media", true, $"media-failed:{asset.Id:N}:{now.ToUnixTimeSeconds()}", ct);
                }
                break;
            default:
                return;
        }
        changes.Record("media", asset.Id, null, asset.SenderId, asset.AssignedToId, "status");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
    }

    /// <summary>Stores the small preview fetched by the dispatcher (private: only media.inbox can open it).</summary>
    public async Task StoreThumbAsync(Guid id, byte[] bytes, CancellationToken ct)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null || bytes.Length == 0) return;
        using var s = new MemoryStream(bytes);
        var saved = await store.SaveImageAsync(s, bytes.Length, "media", ct);
        if (saved.IsFailure) return;
        asset.SetThumb(saved.Value!);
        changes.Record("media", asset.Id, null, asset.SenderId, null, "thumb");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
    }

    // ===================== helpers =====================

    private async Task QueueAdminCopiesAsync(MediaAsset asset, string senderName, string? workTitle, CancellationToken ct)
    {
        var admins = await notifier.StaffWithAsync(Permissions.MediaInbox, PermissionScope.All, ct);
        var links = await db.MessengerLinks.AsNoTracking().Where(l => l.Messenger == asset.Messenger && admins.Contains(l.UserId) && l.UserId != asset.SenderId).ToListAsync(ct);
        var (fromChat, fromMessage) = asset.CopySource;
        var caption = $"📥 از {senderName} — {KindName(asset.Kind)}{(asset.FileSize is { } size ? $"، {Size(size)}" : "")}{(asset.DurationSeconds is { } d ? $"، {Fa(d / 60)}:{(d % 60).ToString("00", CultureInfo.InvariantCulture)}" : "")}\n"
                      + $"ساعت ارسال: {Clock(asset.SentAt)}\nکار: {workTitle ?? "هنوز انتخاب نشده"}";
        var rows = new JsonArray { new JsonArray(Button("سپردن به…", $"a|{ShortId(asset.Id)}")) };
        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        // Telegram accepts only public https links on buttons.
        if (baseUrl.StartsWith("https://", StringComparison.Ordinal))
            rows.Add(new JsonArray(new JsonObject { ["text"] = "باز کردن در کارنما", ["url"] = $"{baseUrl}/media?id={asset.Id}" }));
        foreach (var l in links)
            Queue(asset.Messenger, "copyMessage", new JsonObject
            {
                ["chat_id"] = l.ChatId, ["from_chat_id"] = fromChat, ["message_id"] = fromMessage, ["caption"] = caption,
                ["reply_markup"] = new JsonObject { ["inline_keyboard"] = rows.DeepClone() },
            }, "admin", asset.Id, $"admin:{asset.Id:N}:{l.UserId:N}", l.UserId);
        if (links.Count == 0) asset.SentToAdmins();
    }

    private long? ArchiveChat(Messenger m) => m == Messenger.Telegram ? options.Value.TelegramArchiveChatId : options.Value.BaleArchiveChatId;

    private void Reply(Messenger m, long chatId, string text, string key) =>
        Queue(m, "sendMessage", new JsonObject { ["chat_id"] = chatId, ["text"] = text }, "reply", null, key);

    private void Queue(Messenger m, string method, JsonObject args, string purpose, Guid? mediaId, string key, Guid? userId = null)
    {
        var payload = JsonSerializer.Serialize(new BotCallPayload(m, method, args, purpose, mediaId), Json);
        key = $"{m}:{key}";
        db.Outbox.Add(OutboxMessage.BotCall(userId ?? Guid.Empty, payload, key.Length <= 200 ? key : key[..200], clock.GetUtcNow()));
    }

    private async Task<bool> HasAsync(Guid userId, string permission, CancellationToken ct) =>
        (await notifier.StaffWithAsync(permission, PermissionScope.All, ct)).Contains(userId);

    private async Task<List<MediaDto>> ToDtosAsync(IReadOnlyList<MediaAsset> items, CancellationToken ct)
    {
        var userIds = items.Select(i => i.SenderId).Concat(items.Where(i => i.AssignedToId is not null).Select(i => i.AssignedToId!.Value)).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var workIds = items.Where(i => i.WorkItemId is not null).Select(i => i.WorkItemId!.Value).Distinct().ToList();
        var works = await db.WorkItems.AsNoTracking().Where(w => workIds.Contains(w.Id)).Select(w => new { w.Id, w.Title, w.OrderNumber, w.ClientId }).ToDictionaryAsync(w => w.Id, ct);
        var clientIds = works.Values.Select(w => w.ClientId).Distinct().ToList();
        var clients = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.BusinessName, ct);
        return items.Select(a =>
        {
            var w = a.WorkItemId is { } wid ? works.GetValueOrDefault(wid) : null;
            return new MediaDto(a.Id, a.Messenger, a.Kind, a.SenderId, names.GetValueOrDefault(a.SenderId, "—"), a.FileSize, a.DurationSeconds, a.FileName, a.Caption,
                a.SentAt, a.ReceivedAt, a.WorkItemId, w?.Title, w?.OrderNumber, w is null ? null : clients.GetValueOrDefault(w.ClientId),
                a.Status, a.AssignedToId, a.AssignedToId is { } t ? names.GetValueOrDefault(t) : null, a.AssignedAt, a.DeliveredAt, a.Error,
                a.ThumbPath is not null, a.ArchiveMessageId is not null, a.Version);
        }).ToList();
    }

    private static long? MessageId(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message_id", out var id) && id.TryGetInt64(out var v) ? v : null;
        }
        catch (JsonException) { return null; }
    }

    private static JsonObject Button(string text, string data) => new() { ["text"] = text, ["callback_data"] = data };

    public static string ShortId(Guid id) => Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static Guid? FromShort(string s)
    {
        if (s.Length != 22) return null;
        try { return new Guid(Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/') + "==")); }
        catch (FormatException) { return null; }
    }

    private static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Fa(long n) => string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c));

    private static string Size(long bytes) => bytes >= 1024 * 1024
        ? $"{Fa((long)Math.Round(bytes / 1048576.0))} مگابایت"
        : $"{Fa(Math.Max(1, bytes / 1024))} کیلوبایت";

    private static string Clock(DateTimeOffset at)
    {
        var local = Iran.Local(at);
        return $"{Fa(local.Hour)}:{Fa(local.Minute / 10)}{Fa(local.Minute % 10)} {JalaliDate(at)}";
    }

    private static string JalaliDate(DateTimeOffset at)
    {
        var cal = new PersianCalendar();
        var local = Iran.Local(at).DateTime;
        return $"{Fa(cal.GetYear(local))}/{Fa(cal.GetMonth(local))}/{Fa(cal.GetDayOfMonth(local))}";
    }

    private static string KindName(MediaKind k) => k switch
    {
        MediaKind.Video => "ویدیو",
        MediaKind.Photo => "عکس",
        MediaKind.Audio => "صدا",
        MediaKind.Voice => "پیام صوتی",
        MediaKind.Animation => "گیف",
        MediaKind.VideoNote => "ویدیوی دایره‌ای",
        _ => "فایل",
    };
}
