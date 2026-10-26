using Karnama.Domain.Common;

namespace Karnama.Application.Abstractions;

/// <summary>Stores uploaded images outside the web root with random names (magic-byte checked, no SVG).</summary>
public interface IMediaStore
{
    /// <summary>Returns the stored file id, e.g. "brand/3f2a….png".</summary>
    Task<Result<string>> SaveImageAsync(Stream content, long length, string folder, CancellationToken ct);
    /// <summary>Stores a short MP4/WebM clip (magic-byte checked, size-capped) and returns its id, e.g. "site/3f2a….mp4".</summary>
    Task<Result<string>> SaveVideoAsync(Stream content, long length, string folder, CancellationToken ct);
    string PublicUrl(string id);
    /// <summary>Opens a stored public file, or null if the id is invalid or missing.</summary>
    (Stream Stream, string ContentType)? OpenPublic(string id);
    /// <summary>Private images (e.g. "consent/…"): never served by the public media route; callers check permissions.</summary>
    (Stream Stream, string ContentType)? OpenPrivate(string id);
}
