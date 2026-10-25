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
