using Karnama.Domain.Common;
using Karnama.Domain.Notify;

namespace Karnama.Domain.Media;

public enum MediaKind { Video = 1, Photo = 2, Document = 3, Audio = 4, Voice = 5, Animation = 6, VideoNote = 7 }

/// <summary>Received → (archived) → sent to admins → assigned → delivered; Failed when the messenger refused a step.</summary>
public enum MediaStatus { Received = 1, Archived = 2, SentToAdmins = 3, Assigned = 4, Delivered = 5, Failed = 9 }

/// <summary>
/// A file someone sent to the bot (PROMPT2 §8). The bytes never come to our server: only the messenger's file_id and
/// details are kept; copies are made inside the messenger (copyMessage) from a private archive channel.
/// </summary>
public sealed class MediaAsset : Entity
{
    private MediaAsset() { FileId = string.Empty; FileUniqueId = string.Empty; }

    public Messenger Messenger { get; private set; }
    public Guid SenderId { get; private set; }
    public MediaKind Kind { get; private set; }
    public string FileId { get; private set; }
    public string FileUniqueId { get; private set; }
    public long? FileSize { get; private set; }
    public int? DurationSeconds { get; private set; }
    public string? FileName { get; private set; }
    public string? MimeType { get; private set; }
    public string? Caption { get; private set; }
    public string? ThumbFileId { get; private set; }
    /// <summary>A small preview fetched once through the gateway (private media store id), or null.</summary>
    public string? ThumbPath { get; private set; }

    /// <summary>Telegram's message.date (when it was sent) and when our server got it (the "upload time").</summary>
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    public long SourceChatId { get; private set; }
    public long SourceMessageId { get; private set; }
    public long? ArchiveChatId { get; private set; }
    public long? ArchiveMessageId { get; private set; }

    public Guid? WorkItemId { get; private set; }
    public Guid? ClientId { get; private set; }

    public MediaStatus Status { get; private set; }
    public Guid? AssignedToId { get; private set; }
    public Guid? AssignedById { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? Error { get; private set; }
    public uint Version { get; private set; }

    public static MediaAsset Receive(Messenger messenger, Guid senderId, MediaKind kind, string fileId, string fileUniqueId, long? size, int? duration,
        string? fileName, string? mimeType, string? caption, string? thumbFileId, DateTimeOffset sentAt, DateTimeOffset receivedAt, long chatId, long messageId) => new()
    {
        Messenger = messenger,
        SenderId = senderId,
        Kind = kind,
        FileId = Cut(fileId, 200)!,
        FileUniqueId = Cut(fileUniqueId, 100)!,
        FileSize = size,
        DurationSeconds = duration,
        FileName = Cut(fileName, 200),
        MimeType = Cut(mimeType, 100),
        Caption = Cut(caption, 1000),
        ThumbFileId = Cut(thumbFileId, 200),
        SentAt = sentAt,
        ReceivedAt = receivedAt,
        SourceChatId = chatId,
        SourceMessageId = messageId,
        Status = MediaStatus.Received,
    };

    /// <summary>Where copies are made from: the archive channel once archived, else the sender's chat.</summary>
    public (long ChatId, long MessageId) CopySource => ArchiveMessageId is { } m ? (ArchiveChatId!.Value, m) : (SourceChatId, SourceMessageId);

    public void Archived(long chatId, long messageId)
    {
        ArchiveChatId = chatId;
        ArchiveMessageId = messageId;
        if (Status == MediaStatus.Received) Status = MediaStatus.Archived;
    }

    public void SentToAdmins()
    {
        if (Status is MediaStatus.Received or MediaStatus.Archived) Status = MediaStatus.SentToAdmins;
    }

    public void LinkWork(Guid workItemId, Guid clientId)
    {
        WorkItemId = workItemId;
        ClientId = clientId;
    }

    public void SetThumb(string path) => ThumbPath = path;

    public Result Assign(Guid to, Guid by, DateTimeOffset now)
    {
        if (to == Guid.Empty) return Karnama.Domain.Common.Error.Validation("media.noTarget", "گیرنده را انتخاب کنید.");
        AssignedToId = to;
        AssignedById = by;
        AssignedAt = now;
        DeliveredAt = null;
        Error = null;
        Status = MediaStatus.Assigned;
        return Result.Success();
    }

    public void Delivered(DateTimeOffset now)
    {
        if (Status != MediaStatus.Assigned) return;
        Status = MediaStatus.Delivered;
        DeliveredAt = now;
    }

    public void Failed(string error)
    {
        Status = MediaStatus.Failed;
        Error = Cut(error, 300);
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}
