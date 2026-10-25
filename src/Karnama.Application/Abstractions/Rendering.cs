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
