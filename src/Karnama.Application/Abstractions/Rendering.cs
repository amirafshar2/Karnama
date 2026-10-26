namespace Karnama.Application.Abstractions;

/// <summary>Markdown → whitelist-sanitized HTML (package descriptions now; blog and help articles later).</summary>
public interface IContentRenderer
{
    string MarkdownToSafeHtml(string markdown);
    string SanitizeHtml(string html);
}

/// <summary>Excel export of any list (header row + values).</summary>
public interface IExcelExporter
{
    byte[] Export(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows);
}

/// <summary>Drops the API's short cache of public site reads after content changes (so a fix shows up at once).</summary>
public interface IPublicCache
{
    Task EvictAsync(CancellationToken ct);
}

/// <summary>Short-lived signed tokens for links that must work without an Authorization header (file downloads).</summary>
public interface ILinkSigner
{
    string Sign(string payload, TimeSpan lifetime);
    /// <returns>The payload, or null if the token is forged, altered or expired.</returns>
    string? Verify(string token);
}

/// <summary>
/// Envelope encryption for the credential vault (PROMPT2 §10.2): every record gets its own random AES-256-GCM data key,
/// wrapped by a master key that never touches the database. <paramref name="context"/> is bound as associated data
/// (the record id), so a ciphertext copied onto another row does not open.
/// </summary>
public interface IVault
{
    bool IsConfigured { get; }
    SealedSecret Seal(byte[] plain, string context);
    /// <returns>The plain bytes, or null if the data was altered, belongs to another record or its key is gone.</returns>
    byte[]? Open(SealedSecret sealedSecret, string context);
}

public sealed record SealedSecret(byte[] Cipher, byte[] WrappedKey, int KeyVersion);

/// <summary>
/// Bot API calls through the messenger (Telegram: the signed gateway outside Iran; Bale: its own API).
/// <see cref="BotCallResult.Permanent"/> = the messenger refused for good (blocked bot, deleted message): don't retry.
/// </summary>
public interface IBotGateway
{
    bool IsConfigured(Domain.Notify.Messenger messenger);
    Task<BotCallResult> CallAsync(Domain.Notify.Messenger messenger, string method, System.Text.Json.Nodes.JsonObject args, CancellationToken ct);
    /// <summary>A small preview (≤ 1 MB) by file id, or null.</summary>
    Task<byte[]?> ThumbnailAsync(Domain.Notify.Messenger messenger, string fileId, CancellationToken ct);
}

public sealed record BotCallResult(bool Ok, bool Permanent, string? ResultJson, string? Error);

/// <summary>First sheet of an .xlsx, or a .csv file, as rows of text cells (for imports with a preview and column mapping).</summary>
public interface ISpreadsheetReader
{
    /// <returns>Rows (header included), or null when the file isn't a readable sheet.</returns>
    IReadOnlyList<IReadOnlyList<string>>? Read(byte[] content, string fileName, int maxRows, int maxColumns);
}

/// <summary>
/// The media box in the office (MediaMTX, reached through the WireGuard tunnel): it pulls each camera's RTSP stream,
/// serves WebRTC to viewers through our reverse proxy, and gives single frames for snapshots.
/// </summary>
public interface IMediaServer
{
    bool IsConfigured { get; }
    /// <summary>Creates or replaces a stream; <paramref name="onDemand"/> = pulled from the camera only while someone watches.</summary>
    Task<bool> UpsertPathAsync(string path, string source, bool onDemand, CancellationToken ct);
    Task<bool> RemovePathAsync(string path, CancellationToken ct);
    /// <summary>Stream name → receiving from its camera. Null when the box can't be reached.</summary>
    Task<IReadOnlyDictionary<string, bool>?> ReadyPathsAsync(CancellationToken ct);
    /// <summary>One JPEG frame of a stream, or null.</summary>
    Task<byte[]?> SnapshotAsync(string path, CancellationToken ct);
    /// <summary>Public WebRTC (WHEP) address of a stream, via our reverse proxy.</summary>
    string WhepUrl(string path);
    /// <summary>The internal account our own server uses towards the box (snapshots, API).</summary>
    bool IsInternalAccount(string? user, string? password);
}
