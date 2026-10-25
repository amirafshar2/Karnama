using Karnama.Domain.Common;

namespace Karnama.Domain.Files;

public enum FilePurpose { Brief, Deliverable, Attachment }

public enum FileStatus { Uploading, Ready }

/// <summary>
/// A private file (client brief, finished reel, …), uploaded in resumable chunks. Never public: every download is
/// checked against the order it belongs to.
/// </summary>
public sealed class FileAsset : Entity
{
    public const int MaxName = 200;
    private FileAsset() { FileName = string.Empty; ContentType = string.Empty; StorageKey = string.Empty; }

    public Guid OrderId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? WorkItemId { get; private set; }
    public FilePurpose Purpose { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long Size { get; private set; }
    public long Received { get; private set; }
    public string StorageKey { get; private set; }
    public string? Kind { get; private set; }
    public FileStatus Status { get; private set; }
    /// <summary>Clients see their own briefs and deliverables an admin has shared; never internal attachments.</summary>
    public bool ClientVisible { get; private set; }
    public Guid? UploadedBy { get; private set; }
    public bool UploadedByClient { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public static Result<FileAsset> Start(Guid orderId, Guid clientId, Guid? workItemId, FilePurpose purpose, string fileName, string contentType,
        long size, long maxSize, Guid? uploadedBy, bool byClient, DateTimeOffset now)
    {
        fileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (fileName.Length is 0 or > MaxName || fileName.IndexOfAny(['\0', '/', '\\']) >= 0)
            return Error.Validation("files.name", "نام فایل معتبر نیست.");
        if (size <= 0 || size > maxSize)
            return Error.Validation("files.size", $"حجم فایل باید کمتر از {maxSize / 1024 / 1024} مگابایت باشد.");
        var id = Guid.CreateVersion7();
        return new FileAsset
        {
            Id = id, OrderId = orderId, ClientId = clientId, WorkItemId = workItemId, Purpose = purpose, FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim()[..Math.Min(contentType.Trim().Length, 100)],
            Size = size, StorageKey = $"{now:yyyy}/{now:MM}/{id:N}", Status = FileStatus.Uploading,
            // Staff files reach the client only when an admin shares them.
            ClientVisible = byClient, UploadedBy = uploadedBy, UploadedByClient = byClient, CreatedAt = now,
        };
    }

    public Result Advance(long offset, long length)
    {
        if (Status != FileStatus.Uploading) return Error.Conflict("files.complete", "این فایل کامل شده است.");
        if (offset != Received) return Error.Conflict("files.offset", "ادامه‌ی آپلود از جای اشتباه؛ دوباره تلاش می‌شود.");
        if (Received + length > Size) return Error.Validation("files.tooLong", "حجم ارسالی بیشتر از حجم اعلام‌شده است.");
        Received += length;
        return Result.Success();
    }

    public bool IsComplete => Received == Size;

    public void Complete(string kind, DateTimeOffset now)
    {
        Kind = kind;
        Status = FileStatus.Ready;
        CompletedAt = now;
    }

    public void ShareWithClient(bool visible) => ClientVisible = visible || UploadedByClient;

    public void Delete() => IsDeleted = true;
}
