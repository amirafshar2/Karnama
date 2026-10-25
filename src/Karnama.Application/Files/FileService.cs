using Karnama.Application.Abstractions;
using Karnama.Application.Work;
using Karnama.Domain.Common;
using Karnama.Domain.Files;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Files;

public sealed class FileOptions
{
    public const string Section = "Files";
    /// <summary>Largest single file (a 4K reel can be big). Default 2 GB.</summary>
    public long MaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    /// <summary>Largest chunk per request; small enough to retry cheaply on a weak mobile connection.</summary>
    public int MaxChunkBytes { get; set; } = 8 * 1024 * 1024;
    /// <summary>Unfinished uploads one person may have at once (stops anyone reserving the disk).</summary>
    public int MaxOpenUploadsPerUser { get; set; } = 5;
    /// <summary>Unfinished uploads older than this are deleted by the worker.</summary>
    public int StaleUploadHours { get; set; } = 24;
}

public sealed record FileDto(Guid Id, string FileName, string ContentType, long Size, string? Kind, FilePurpose Purpose, bool ClientVisible, bool FromClient, Guid? WorkItemId, DateTimeOffset CreatedAt);
public sealed record StartUploadRequest(Guid OrderId, Guid? WorkItemId, FilePurpose Purpose, string FileName, string? ContentType, long Size);
public sealed record UploadStatusDto(Guid Id, long Size, long Received, bool Complete, int ChunkSize);

/// <summary>
/// Resumable uploads (tus-like: start → PATCH chunks at an offset → HEAD to resume) and checked downloads.
/// The file type is decided from its first bytes, never from the name the client sent.
/// </summary>
public sealed class FileService(IAppDbContext db, WorkAccess access, IFileStore store, IOptions<FileOptions> options, TimeProvider clock, IAuditWriter audit, IChangeFeed changes, ICurrentUser me)
{
    private readonly FileOptions _o = options.Value;

    public static FileDto ToDto(FileAsset f) => new(f.Id, f.FileName, f.ContentType, f.Size, f.Kind, f.Purpose, f.ClientVisible, f.UploadedByClient, f.WorkItemId, f.CreatedAt);

    public async Task<Result<UploadStatusDto>> StartAsync(StartUploadRequest r, CancellationToken ct)
    {
        var order = await (await access.VisibleOrdersAsync(ct)).AsNoTracking().FirstOrDefaultAsync(o => o.Id == r.OrderId, ct);
        if (order is null) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var portal = await access.PortalClientIdAsync(ct) is not null;
        if (portal)
        {
            if (r.WorkItemId is not null || r.Purpose != FilePurpose.Brief) return Error.Forbidden();
        }
        else if (r.WorkItemId is { } wid)
        {
            var w = await access.VisibleItems().AsNoTracking().FirstOrDefaultAsync(x => x.Id == wid && x.OrderId == order.Id, ct);
            if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
            if (!access.IsManager && !(access.CanRun && w.AssigneeId == access.Me)) return Error.Forbidden("work.notYours", "این کار به شما سپرده نشده است.");
        }
        else if (!access.IsManager && !me.Has(Domain.Identity.Permissions.OrdersManage, Domain.Identity.PermissionScope.All)) return Error.Forbidden();
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected) return Error.Conflict("orders.closed", "این سفارش بسته شده است.");

        var uid = access.Me;
        if (await db.Files.CountAsync(x => x.UploadedBy == uid && x.Status == FileStatus.Uploading, ct) >= _o.MaxOpenUploadsPerUser)
            return Error.TooMany("files.tooManyOpen", "چند آپلود نیمه‌کاره دارید؛ اول آن‌ها را تمام کنید.");
        var f = FileAsset.Start(order.Id, order.ClientId, r.WorkItemId, r.Purpose, r.FileName, r.ContentType ?? "", r.Size, _o.MaxBytes, me.UserId, portal, clock.GetUtcNow());
        if (f.IsFailure) return f.Error!;
        db.Files.Add(f.Value!);
        await db.SaveChangesAsync(ct);
        return Status(f.Value!);
    }

    private UploadStatusDto Status(FileAsset f) => new(f.Id, f.Size, f.Received, f.IsComplete, _o.MaxChunkBytes);

    private async Task<FileAsset?> OwnUploadAsync(Guid id, CancellationToken ct)
    {
        var uid = access.Me;
        // Only the person who started an upload can continue it.
        return await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.UploadedBy == uid, ct);
    }

    public async Task<Result<UploadStatusDto>> StatusAsync(Guid id, CancellationToken ct) =>
        await OwnUploadAsync(id, ct) is { } f ? Status(f) : Error.NotFound("files.notFound", "فایل پیدا نشد.");

    public async Task<Result<UploadStatusDto>> AppendAsync(Guid id, long offset, long? length, Stream body, CancellationToken ct)
    {
        var f = await OwnUploadAsync(id, ct);
        if (f is null) return Error.NotFound("files.notFound", "فایل پیدا نشد.");
        if (length is null or <= 0 || length > _o.MaxChunkBytes) return Error.Validation("files.chunk", "اندازه‌ی تکه معتبر نیست.");
        var adv = f.Advance(offset, length.Value);
        if (adv.IsFailure) return adv.Error!;
        await store.AppendAsync(f.StorageKey, offset, body, length.Value, ct);

        if (f.IsComplete)
        {
            var kind = Sniff(await store.ReadHeadAsync(f.StorageKey, 32, ct));
            if (kind is null)
            {
                await store.DeleteAsync(f.StorageKey, ct);
                f.Delete();
                await db.SaveChangesAsync(ct);
                return Error.Validation("files.type", "این نوع فایل پذیرفته نمی‌شود (عکس، ویدیو، PDF، ZIP، PSD یا صدا).");
            }
            f.Complete(kind, clock.GetUtcNow());
            audit.Add("files.uploaded", nameof(FileAsset), f.Id.ToString(), new { f.OrderId, f.WorkItemId, f.Size, kind });
            if (f.WorkItemId is { } wid) changes.Record("work", wid, f.ClientVisible ? f.ClientId : null, null, null, "files");
            changes.Record("order", f.OrderId, f.ClientVisible ? f.ClientId : null, null, null, "files");
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Status(f);
    }

    public async Task<Result<(FileAsset File, Stream Content)>> OpenAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == FileStatus.Ready, ct);
        if (f is null) return Error.NotFound("files.notFound", "فایل پیدا نشد.");
        if (!await CanReadAsync(f, ct)) return Error.NotFound("files.notFound", "فایل پیدا نشد.");
        return (f, store.OpenRead(f.StorageKey));
    }

    private async Task<bool> CanReadAsync(FileAsset f, CancellationToken ct)
    {
        if (await access.PortalClientIdAsync(ct) is { } clientId) return f.ClientId == clientId && f.ClientVisible;
        if (access.SeesAllWork || access.SeesAllOrders) return true;
        var uid = access.Me;
        // Workers: files of the orders they work on (the brief and earlier stages' output).
        return await db.WorkItems.AnyAsync(w => w.OrderId == f.OrderId && w.AssigneeId == uid, ct);
    }

    public async Task<Result<FileDto>> ShareAsync(Guid id, bool visible, CancellationToken ct)
    {
        if (!access.CanTalkToClient) return Error.Forbidden("comments.clientOnlyAdmins", "فقط ادمین فایل را برای مشتری می‌فرستد.");
        var f = await db.Files.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Error.NotFound("files.notFound", "فایل پیدا نشد.");
        f.ShareWithClient(visible);
        audit.Add(visible ? "files.shared" : "files.unshared", nameof(FileAsset), id.ToString());
        changes.Record("order", f.OrderId, f.ClientId, null, null, "files");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return ToDto(f);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Files.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Error.NotFound("files.notFound", "فایل پیدا نشد.");
        if (!access.IsManager && f.UploadedBy != access.Me) return Error.Forbidden();
        f.Delete();
        audit.Add("files.deleted", nameof(FileAsset), id.ToString(), new { f.FileName });
        if (f.WorkItemId is { } wid) changes.Record("work", wid, null, null, null, "files");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    /// <summary>Deletes unfinished uploads nobody continued (worker job).</summary>
    public static async Task<int> PurgeStaleAsync(IAppDbContext db, IFileStore store, FileOptions o, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-o.StaleUploadHours);
        var stale = await db.Files.Where(f => f.Status == FileStatus.Uploading && f.CreatedAt < cutoff).Take(500).ToListAsync(ct);
        foreach (var f in stale)
        {
            await store.DeleteAsync(f.StorageKey, ct);
            f.Delete();
        }
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    /// <summary>Allowed kinds by magic bytes. Anything else (HTML, SVG, scripts, executables) is refused.</summary>
    private static bool At(ReadOnlySpan<byte> h, int i, ReadOnlySpan<byte> sig) => h.Length >= i + sig.Length && h.Slice(i, sig.Length).SequenceEqual(sig);

    public static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (At(h, 0, [0x89, 0x50, 0x4E, 0x47])) return "png";
        if (At(h, 0, [0xFF, 0xD8, 0xFF])) return "jpg";
        if (At(h, 0, "GIF8"u8)) return "gif";
        if (At(h, 0, "RIFF"u8) && At(h, 8, "WEBP"u8)) return "webp";
        if (At(h, 0, "RIFF"u8) && At(h, 8, "WAVE"u8)) return "wav";
        if (At(h, 4, "ftyp"u8))
        {
            if (At(h, 8, "heic"u8) || At(h, 8, "heix"u8) || At(h, 8, "mif1"u8)) return "heic";
            if (At(h, 8, "qt  "u8)) return "mov";
            if (At(h, 8, "M4A "u8)) return "m4a";
            return "mp4";
        }
        if (At(h, 0, [0x1A, 0x45, 0xDF, 0xA3])) return "webm";
        if (At(h, 0, "%PDF-"u8)) return "pdf";
        if (At(h, 0, [0x50, 0x4B, 0x03, 0x04])) return "zip";
        if (At(h, 0, "8BPS"u8)) return "psd";
        if (At(h, 0, "ID3"u8) || At(h, 0, [0xFF, 0xFB]) || At(h, 0, [0xFF, 0xF3])) return "mp3";
        return null;
    }
}
