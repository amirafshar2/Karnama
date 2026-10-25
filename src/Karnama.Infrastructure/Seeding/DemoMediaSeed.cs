using Karnama.Domain.Media;
using Karnama.Domain.Notify;
using Karnama.Domain.Work;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo files inbox: a few files "sent to the bot" by the demo team (made-up file ids, no real Telegram data).</summary>
internal static class DemoMediaSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.MediaAssets.AnyAsync(ct)) return;
        var users = await db.Users.Where(u => u.IsDemo).ToDictionaryAsync(u => u.Phone, u => u.Id, ct);
        if (!users.TryGetValue("09000000002", out var editor) || !users.TryGetValue("09000000003", out var writer) || !users.TryGetValue("09000000001", out var admin)) return;
        var now = DateTimeOffset.UtcNow;

        // Demo staff "linked" the bot (fake chat ids) so assigning can be shown.
        foreach (var (user, chat) in new[] { (editor, 1_000_000_002L), (writer, 1_000_000_003L), (admin, 1_000_000_001L) })
            if (!await db.MessengerLinks.AnyAsync(l => l.UserId == user && l.Messenger == Messenger.Telegram, ct))
                db.MessengerLinks.Add(new MessengerLink(user, Messenger.Telegram, chat, now));

        var editorWork = await db.WorkItems.AsNoTracking().Where(w => w.AssigneeId == editor && w.Status != WorkStatus.Done && w.Status != WorkStatus.Cancelled).FirstOrDefaultAsync(ct);
        var writerWork = await db.WorkItems.AsNoTracking().Where(w => w.AssigneeId == writer && w.Status != WorkStatus.Done && w.Status != WorkStatus.Cancelled).FirstOrDefaultAsync(ct);

        MediaAsset Make(Guid sender, MediaKind kind, string name, long size, int? duration, string caption, int minutesAgo, long chat, long message)
        {
            var a = MediaAsset.Receive(Messenger.Telegram, sender, kind, $"DEMO-{name}", $"demo-{name}", size, duration, kind == MediaKind.Document ? $"{name}.pdf" : null,
                null, caption, null, now.AddMinutes(-minutesAgo - 1), now.AddMinutes(-minutesAgo), chat, message);
            a.Archived(-1_000_000_000_001, message + 5000);
            return a;
        }

        var rush = Make(editor, MediaKind.Video, "rush-cafe", 186_000_000, 214, "راش‌های فیلم‌برداری منوی پاییزی کافه", 25, 1_000_000_002, 11);
        if (editorWork is not null) rush.LinkWork(editorWork.Id, editorWork.ClientId);
        rush.SentToAdmins();

        var photos = Make(writer, MediaKind.Photo, "photo-shop", 3_400_000, null, "عکس‌های محصول پوشاک — انتخاب‌شده", 90, 1_000_000_003, 21);
        if (writerWork is not null) photos.LinkWork(writerWork.Id, writerWork.ClientId);
        photos.SentToAdmins();
        photos.Assign(editor, admin, now.AddMinutes(-80));
        photos.Delivered(now.AddMinutes(-79));

        var brief = Make(writer, MediaKind.Document, "brief-clinic", 820_000, null, "بریف کمپین کلینیک", 180, 1_000_000_003, 22);
        brief.SentToAdmins();

        var voice = Make(editor, MediaKind.Voice, "voice-note", 240_000, 38, "توضیح صوتی درباره‌ی تدوین", 5, 1_000_000_002, 12);

        db.MediaAssets.AddRange(rush, photos, brief, voice);
        await db.SaveChangesAsync(ct);
    }
}
