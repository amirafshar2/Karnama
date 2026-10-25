using System.Text.RegularExpressions;
using Karnama.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Karnama.Infrastructure.Storage;

/// <summary>Private files on local disk under storage/private (S3-compatible storage in Iran replaces it in M7).</summary>
internal sealed partial class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"];
        var baseDir = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "..", "..", "storage") : configured);
        _root = Path.Combine(baseDir, "private");
        Directory.CreateDirectory(_root);
    }

    [GeneratedRegex("^[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();

    private string PathOf(string key)
    {
        // Keys come from the server, but check anyway: no traversal, ever.
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("invalid storage key", nameof(key));
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task AppendAsync(string key, long offset, Stream data, long length, CancellationToken ct)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        if (fs.Length < offset) throw new IOException("gap in upload");
        fs.SetLength(offset);   // drop any partial tail of a failed earlier attempt
        fs.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[81920];
        long left = length;
        while (left > 0)
        {
            var n = await data.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
            if (n == 0) throw new IOException("upload chunk ended early");
            await fs.WriteAsync(buffer.AsMemory(0, n), ct);
            left -= n;
        }
    }

    public async Task<byte[]> ReadHeadAsync(string key, int count, CancellationToken ct)
    {
        await using var fs = new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[Math.Min(count, (int)fs.Length)];
        await fs.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    public Stream OpenRead(string key) => new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
