using System.Text.RegularExpressions;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Karnama.Infrastructure.Storage;

/// <summary>
/// Local disk storage outside the web root (S3-compatible storage in Iran replaces it in M7).
/// Upload rules (docs/SKILLS.md §4.3): magic bytes, size cap, random name, no SVG, path-traversal-proof ids.
/// </summary>
internal sealed partial class LocalMediaStore : IMediaStore
{
    public const long MaxImageBytes = 2 * 1024 * 1024;
    public const long MaxVideoBytes = 18 * 1024 * 1024;
    private readonly string _root;

    public LocalMediaStore(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "..", "..", "storage") : configured);
        Directory.CreateDirectory(_root);
    }

    private static string NameOf(string? stable) =>
        stable is { Length: 32 } && stable.All(Uri.IsHexDigit) ? stable.ToLowerInvariant() : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^(brand|packages|blog|cases|site)/[0-9a-f]{32}\\.(png|jpg|webp|mp4|webm)$")]
    private static partial Regex IdPattern();

    public async Task<Result<string>> SaveImageAsync(Stream content, long length, string folder, CancellationToken ct, string? stableName = null)
    {
        if (length <= 0 || length > MaxImageBytes)
            return Error.Validation("media.tooLarge", "حجم عکس باید کمتر از ۲ مگابایت باشد.");

        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await content.ReadAsync(buffer.AsMemory(read, (int)(length - read)), ct);
            if (n == 0) break;
            read += n;
        }
        if (read != length) return Error.Validation("media.incomplete", "فایل کامل نرسید. دوباره امتحان کنید.");

        var ext = Sniff(buffer);
        if (ext is null)
            return Error.Validation("media.badType", "فقط عکس PNG، JPG یا WebP قبول می‌شود (SVG مجاز نیست).");

        var id = $"{folder}/{NameOf(stableName)}.{ext}";
        var path = Path.Combine(_root, id);
        if (stableName is not null && File.Exists(path)) return id; // the same built-in demo file, already there
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = PartPath(path);
        await File.WriteAllBytesAsync(part, buffer, ct);
        Publish(part, path);
        return id;
    }

    /// <summary>Files are written next to their final name first, so a half-written file is never served.</summary>
    private static string PartPath(string path) => $"{path}.{Guid.NewGuid():N}.part";

    /// <summary>
    /// Moves a finished file into place in one step. Two starts of a demo install (or two test hosts) may write the same
    /// built-in file at the same moment: whoever comes second finds it already there and drops its copy (same bytes).
    /// </summary>
    private static void Publish(string part, string path)
    {
        try
        {
            File.Move(part, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(part);
        }
    }

    public async Task<Result<string>> SaveVideoAsync(Stream content, long length, string folder, CancellationToken ct, string? stableName = null)
    {
        if (length <= 0 || length > MaxVideoBytes)
            return Error.Validation("media.videoTooLarge", "حجم ویدیو باید کمتر از ۱۸ مگابایت باشد.");
        var head = new byte[16];
        var got = 0;
        while (got < head.Length)
        {
            var n = await content.ReadAsync(head.AsMemory(got), ct);
            if (n == 0) break;
            got += n;
        }
        var ext = SniffVideo(head.AsSpan(0, got));
        if (ext is null) return Error.Validation("media.badVideo", "فقط ویدیوی MP4 یا WebM قبول می‌شود.");

        var id = $"{folder}/{NameOf(stableName)}.{ext}";
        var path = Path.Combine(_root, id);
        if (stableName is not null && File.Exists(path)) return id; // the same built-in demo file, already there
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        long total = got;
        var part = PartPath(path);
        await using (var file = File.Create(part))
        {
            await file.WriteAsync(head.AsMemory(0, got), ct);
            var buffer = new byte[81920];
            int n;
            while ((n = await content.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > MaxVideoBytes) break;
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        if (total != length)
        {
            File.Delete(part);
            return Error.Validation("media.incomplete", "فایل کامل نرسید. دوباره امتحان کنید.");
        }
        Publish(part, path);
        return id;
    }

    internal static string? SniffVideo(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 12 && b.Slice(4, 4).SequenceEqual("ftyp"u8)) return "mp4";
        if (b.Length >= 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return "webm";
        return null;
    }

    private static string TypeOf(string path) => Path.GetExtension(path) switch
    {
        ".png" => "image/png", ".jpg" => "image/jpeg", ".mp4" => "video/mp4", ".webm" => "video/webm", _ => "image/webp",
    };

    public string PublicUrl(string id) => $"/media/public/{id}";

    public (Stream Stream, string ContentType)? OpenPublic(string id)
    {
        if (!IdPattern().IsMatch(id)) return null;
        var path = Path.GetFullPath(Path.Combine(_root, id));
        if (!path.StartsWith(_root, StringComparison.Ordinal) || !File.Exists(path)) return null;
        return (File.OpenRead(path), TypeOf(path));
    }

    [GeneratedRegex("^(consent|media)/[0-9a-f]{32}\\.(png|jpg|webp)$")]
    private static partial Regex PrivateIdPattern();

    public (Stream Stream, string ContentType)? OpenPrivate(string id)
    {
        if (!PrivateIdPattern().IsMatch(id)) return null;
        var path = Path.GetFullPath(Path.Combine(_root, id));
        if (!path.StartsWith(_root, StringComparison.Ordinal) || !File.Exists(path)) return null;
        var type = Path.GetExtension(path) switch { ".png" => "image/png", ".jpg" => "image/jpeg", _ => "image/webp" };
        return (File.OpenRead(path), type);
    }

    internal static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }
}
