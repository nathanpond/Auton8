using System.Globalization;

namespace AutoNate.Web.Services.DataStores.Sql;

// Shared CSV column-name + type heuristics used by both the SqlType
// ingest path (CsvIngestor) and the Files-datastore parser
// (Datasets.Files.CsvFileParser). One home so both stay in lockstep on
// what counts as a bigint vs a double vs a text column and on how a raw
// header becomes a safe Postgres identifier.
internal static class CsvSchemaHeuristics
{
    public static string InferType(IReadOnlyList<string?[]> sample, int columnIndex)
    {
        if (sample.Count == 0) return "text";
        var allInt = true;
        var allDouble = true;
        var allBool = true;
        var allDateTime = true;
        var anyValue = false;
        foreach (var row in sample)
        {
            if (columnIndex >= row.Length) continue;
            var v = row[columnIndex];
            if (v is null || v.Length == 0) continue;
            anyValue = true;
            if (allInt && !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                allInt = false;
            if (allDouble && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                allDouble = false;
            if (allBool && !(string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)))
                allBool = false;
            if (allDateTime && !DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
                allDateTime = false;
        }
        if (!anyValue) return "text";
        if (allInt) return "bigint";
        if (allDouble) return "double precision";
        if (allBool) return "boolean";
        if (allDateTime) return "timestamptz";
        return "text";
    }

    public static string SanitizeColumnName(string raw, int index)
    {
        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) trimmed = $"col_{index + 1}";
        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(char.ToLowerInvariant(ch));
            else sb.Append('_');
        }
        var name = sb.ToString();
        if (name.Length == 0 || !char.IsLetter(name[0])) name = "c_" + name;
        if (name.Length > 63) name = name[..63];
        return name;
    }

    public static object? Coerce(string? raw, string postgresType)
    {
        if (raw is null || raw.Length == 0) return null;
        switch (postgresType)
        {
            case "bigint":
                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? l : null;
            case "double precision":
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d : null;
            case "boolean":
                return bool.TryParse(raw, out var b) ? b : null;
            case "timestamptz":
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt : null;
            default:
                return raw;
        }
    }
}
