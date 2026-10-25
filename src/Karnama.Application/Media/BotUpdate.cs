using System.Text.Json;
using Karnama.Domain.Media;

namespace Karnama.Application.Media;

public sealed record ParsedMedia(MediaKind Kind, string FileId, string FileUniqueId, long? Size, int? Duration, string? FileName, string? MimeType, string? ThumbFileId);
public sealed record ParsedCallback(string Id, string Data, long MessageId);

/// <summary>The parts of a Bot API update we use. Only private chats; everything else is ignored.</summary>
public sealed record ParsedUpdate(long UpdateId, long ChatId, long MessageId, DateTimeOffset Date, string? Text, string? Caption, ParsedMedia? Media, ParsedCallback? Callback)
{
    /// <summary>Telegram and Bale share this update shape. Returns null for anything we don't handle.</summary>
    public static ParsedUpdate? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("update_id", out var uid) || !uid.TryGetInt64(out var updateId)) return null;

            if (root.TryGetProperty("callback_query", out var cb))
            {
                if (!cb.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromId)) return null;
                var data = Str(cb, "data");
                var id = Str(cb, "id");
                if (data is null || id is null) return null;
                long messageId = 0;
                if (cb.TryGetProperty("message", out var cm) && cm.TryGetProperty("message_id", out var mid)) messageId = mid.GetInt64();
                return new ParsedUpdate(updateId, fromId.GetInt64(), messageId, DateTimeOffset.UtcNow, null, null, null, new ParsedCallback(id, data, messageId));
            }

            if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("chat", out var chat)) return null;
            if (Str(chat, "type") is { } type && type != "private") return null;
            if (!chat.TryGetProperty("id", out var chatId)) return null;
            var msgId = msg.TryGetProperty("message_id", out var messageIdEl) && messageIdEl.TryGetInt64(out var mv) ? mv : 0;
            var date = msg.TryGetProperty("date", out var d) && d.TryGetInt64(out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : DateTimeOffset.UtcNow;
            return new ParsedUpdate(updateId, chatId.GetInt64(), msgId, date, Str(msg, "text"), Str(msg, "caption"), MediaOf(msg), null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static ParsedMedia? MediaOf(JsonElement msg)
    {
        (string Prop, MediaKind Kind)[] kinds =
        [
            ("video", MediaKind.Video), ("animation", MediaKind.Animation), ("video_note", MediaKind.VideoNote), ("audio", MediaKind.Audio),
            ("voice", MediaKind.Voice), ("document", MediaKind.Document),
        ];
        foreach (var (prop, kind) in kinds)
            if (msg.TryGetProperty(prop, out var f) && f.ValueKind == JsonValueKind.Object && FileOf(f, kind) is { } m) return m;
        if (msg.TryGetProperty("photo", out var photos) && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0)
        {
            // Sizes come smallest first: keep the largest as the file, the smallest as the preview.
            var largest = photos[photos.GetArrayLength() - 1];
            var smallest = photos[0];
            if (FileOf(largest, MediaKind.Photo) is { } p) return p with { ThumbFileId = Str(smallest, "file_id") };
        }
        return null;
    }

    private static ParsedMedia? FileOf(JsonElement f, MediaKind kind)
    {
        var id = Str(f, "file_id");
        var unique = Str(f, "file_unique_id");
        if (id is null || unique is null) return null;
        long? size = f.TryGetProperty("file_size", out var s) && s.TryGetInt64(out var sv) ? sv : null;
        int? duration = f.TryGetProperty("duration", out var du) && du.TryGetInt32(out var dv) ? dv : null;
        string? thumb = null;
        foreach (var t in new[] { "thumbnail", "thumb" })
            if (f.TryGetProperty(t, out var th) && th.ValueKind == JsonValueKind.Object) { thumb = Str(th, "file_id"); break; }
        return new ParsedMedia(kind, id, unique, size, duration, Str(f, "file_name"), Str(f, "mime_type"), thumb);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
