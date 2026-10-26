using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Karnama.Application.Abstractions;

namespace Karnama.Infrastructure.Content;

/// <summary>
/// Reads what attendance software exports (Kasra and others): .xlsx through ClosedXML, .csv/.txt with comma, semicolon
/// or tab, UTF-8 or Windows-1256 (Persian Windows). Dates in Excel cells come back as "yyyy-MM-dd HH:mm:ss".
/// </summary>
internal sealed class SpreadsheetReader : ISpreadsheetReader
{
    static SpreadsheetReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public IReadOnlyList<IReadOnlyList<string>>? Read(byte[] content, string fileName, int maxRows, int maxColumns)
    {
        if (content.Length == 0) return null;
        var isZip = content.Length > 4 && content[0] == 0x50 && content[1] == 0x4B;
        try
        {
            return isZip ? Xlsx(content, maxRows, maxColumns) : Csv(content, maxRows, maxColumns);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static List<IReadOnlyList<string>> Xlsx(byte[] content, int maxRows, int maxColumns)
    {
        using var ms = new MemoryStream(content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        var rows = new List<IReadOnlyList<string>>();
        var used = ws.RangeUsed();
        if (used is null) return rows;
        var cols = Math.Min(used.ColumnCount(), maxColumns);
        foreach (var row in used.Rows().Take(maxRows))
        {
            var cells = new List<string>(cols);
            for (var c = 1; c <= cols; c++)
            {
                var cell = row.Cell(c);
                cells.Add(cell.DataType switch
                {
                    XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    XLDataType.TimeSpan => cell.GetTimeSpan().ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
                    _ => cell.GetFormattedString().Trim(),
                });
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static List<IReadOnlyList<string>> Csv(byte[] content, int maxRows, int maxColumns)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.GetEncoding(1256).GetString(content);
        }
        text = text.TrimStart('﻿');
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).Take(maxRows).ToList();
        if (lines.Count == 0) return [];
        var sep = new[] { '\t', ';', ',' }.OrderByDescending(c => lines[0].Count(x => x == c)).First();
        return lines.Select(l => (IReadOnlyList<string>)Split(l, sep).Take(maxColumns).Select(x => x.Trim()).ToList()).ToList();
    }

    private static IEnumerable<string> Split(string line, char sep)
    {
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (c == sep && !quoted) { yield return sb.ToString(); sb.Clear(); }
            else sb.Append(c);
        }
        yield return sb.ToString();
    }
}
