using ClosedXML.Excel;
using Karnama.Application.Abstractions;

namespace Karnama.Infrastructure.Content;

/// <summary>RTL Excel sheets with a bold header row and auto widths. Cells starting with = + - @ are escaped (no formula injection).</summary>
internal sealed class ExcelExporter : IExcelExporter
{
    public byte[] Export(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        ws.RightToLeft = true;
        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case long l: cell.Value = l; cell.Style.NumberFormat.Format = "#,##0"; break;
                    case int i: cell.Value = i; break;
                    case DateTimeOffset d: cell.Value = d.UtcDateTime; break;
                    case var v:
                        var s = v.ToString() ?? string.Empty;
                        cell.Value = s.Length > 0 && "=+-@\t\r".Contains(s[0]) ? "'" + s : s;
                        break;
                }
            }
            r++;
        }
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
