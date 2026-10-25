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
    private readonly string _root;

    public LocalMediaStore(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "..", "..", "storage") : configured);
        Directory.CreateDirectory(_root);
    }

    [GeneratedRegex("^(brand|packages)/[0-9a-f]{32}\\.(png|jpg|webp)$")]
    private static partial Regex IdPattern();

    public async Task<Result<string>> SaveImageAsync(Stream content, long length, string folder, CancellationToken ct)
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

        var id = $"{folder}/{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, buffer, ct);
        return id;
    }

    public string PublicUrl(string id) => $"/media/public/{id}";

    public (Stream Stream, string ContentType)? OpenPublic(string id)
    {
        if (!IdPattern().IsMatch(id)) return null;
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
