using AutoNate.Plugins.Abstractions;
using ClosedXML.Excel;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Parses an XLSX blob (base64-encoded in the upstream frame's first row's
// `content` column) into rows. Config:
//   contentColumn = <input column holding base64 bytes>  (default "content")
//   sheet         = <sheet name>                          (default first)
//   hasHeader     = true | false                          (default true)
public sealed class XlsxToCsvTransformer : ITransformer
{
    // 64 MB encoded — comfortably above any real spreadsheet, well below the
    // point where ClosedXML's in-memory model threatens the process.
    private const int MaxWorkbookBytes = 64 * 1024 * 1024;

    public string Key => "xlsx-to-csv";
    public string DisplayName => "XLSX → rows";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0 || inputs[0].Rows.Count == 0)
            return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var contentColumn = DataFrameOps.OptionalConfig(config, "contentColumn") ?? "content";
        var sheetName = DataFrameOps.OptionalConfig(config, "sheet");
        var hasHeader = !string.Equals(DataFrameOps.OptionalConfig(config, "hasHeader"), "false",
            StringComparison.OrdinalIgnoreCase);

        var raw = DataFrameOps.RowValue(input.Rows[0], contentColumn);
        var bytes = ResolveBytes(raw);
        if (bytes is null) return Task.FromResult(DataFrame.Empty);

        // A workbook materialises to several times its encoded size in managed
        // memory, on the request or worker thread, so a bloated XLSX submitted
        // through a pipeline is an OOM lever for any pipeline author. Refuse
        // oversized input cleanly instead of discovering it as an
        // OutOfMemoryException mid-run (archived-67).
        if (bytes.Length > MaxWorkbookBytes)
        {
            throw new InvalidOperationException(
                $"XLSX input is {bytes.Length} bytes, above the {MaxWorkbookBytes}-byte limit for this transformer.");
        }

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = sheetName is null
            ? workbook.Worksheets.FirstOrDefault()
            : workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null) return Task.FromResult(DataFrame.Empty);

        var used = sheet.RangeUsed();
        if (used is null) return Task.FromResult(DataFrame.Empty);

        var rowsRaw = used.RowsUsed().ToList();
        if (rowsRaw.Count == 0) return Task.FromResult(DataFrame.Empty);

        var columnCount = used.ColumnCount();
        var columnNames = new List<string>(columnCount);
        if (hasHeader)
        {
            for (var i = 1; i <= columnCount; i++)
            {
                columnNames.Add(rowsRaw[0].Cell(i).GetString());
            }
        }
        else
        {
            for (var i = 1; i <= columnCount; i++) columnNames.Add("col_" + i);
        }

        var startRowIndex = hasHeader ? 1 : 0;
        var outRows = new List<IReadOnlyDictionary<string, object?>>();
        for (var r = startRowIndex; r < rowsRaw.Count; r++)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var c = 1; c <= columnCount; c++)
            {
                row[columnNames[c - 1]] = rowsRaw[r].Cell(c).GetString();
            }
            outRows.Add(row);
        }

        var columns = columnNames.Select(n => new DataColumn(n, DataColumnType.Text)).ToList();
        return Task.FromResult(new DataFrame(columns, outRows));
    }

    private static byte[]? ResolveBytes(object? raw) => raw switch
    {
        byte[] arr => arr,
        string s => TryFromBase64(s),
        _ => null,
    };

    private static byte[]? TryFromBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}
