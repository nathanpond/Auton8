using System.Globalization;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers;

// Shared helpers used by every built-in transformer + analyzer. Pure
// functions over the abstractions-package DataFrame so they don't drag
// host types into individual implementations.
internal static class DataFrameOps
{
    // Case-insensitive lookup mirroring DataFrame.FindColumn but returning
    // a Dictionary so callers can avoid repeated linear scans inside a hot
    // per-row loop.
    public static Dictionary<string, DataColumn> ColumnIndex(DataFrame frame)
    {
        var dict = new Dictionary<string, DataColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in frame.Columns)
        {
            dict[c.Name] = c;
        }
        return dict;
    }

    public static object? RowValue(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (row.TryGetValue(column, out var v)) return v;
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    public static bool TryAsDouble(object? value, out double result)
    {
        result = 0;
        switch (value)
        {
            case null: return false;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short s: result = s; return true;
            case byte bt: result = bt; return true;
            case bool b: result = b ? 1.0 : 0.0; return true;
            default:
                var raw = value.ToString();
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    public static bool TryAsDateTime(object? value, out DateTime result)
    {
        result = default;
        switch (value)
        {
            case null: return false;
            case DateTime dt: result = dt; return true;
            case DateTimeOffset dto: result = dto.UtcDateTime; return true;
            default:
                return DateTime.TryParse(
                    value.ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out result);
        }
    }

    public static string AsString(object? value) =>
        value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    // Builds a row dictionary preserving the column-order iteration semantics
    // most consumers expect — they're free to use it as IReadOnlyDictionary,
    // but the original column order is captured via the Columns array on the
    // owning DataFrame.
    public static IReadOnlyDictionary<string, object?> NewRow() =>
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public static string ConfigValue(IReadOnlyDictionary<string, string> config, string key, string? defaultValue = null)
    {
        if (config.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return defaultValue ?? throw new ArgumentException($"Missing required config '{key}'.");
    }

    public static string? OptionalConfig(IReadOnlyDictionary<string, string> config, string key)
    {
        if (config.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return null;
    }

    public static IReadOnlyList<string> SplitColumnList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static DataColumnType InferColumnType(IEnumerable<object?> values)
    {
        bool anyValue = false;
        bool allBool = true, allInt = true, allDouble = true, allDate = true;
        foreach (var v in values)
        {
            if (v is null) continue;
            anyValue = true;
            if (allBool && v is not bool && (v.ToString() is var s) &&
                !(string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)))
            {
                allBool = false;
            }
            if (allInt && !(v is long || v is int || v is short || v is byte))
            {
                if (!long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    allInt = false;
            }
            if (allDouble && !TryAsDouble(v, out _)) allDouble = false;
            if (allDate && !TryAsDateTime(v, out _)) allDate = false;
        }
        if (!anyValue) return DataColumnType.Text;
        if (allBool) return DataColumnType.Boolean;
        if (allInt) return DataColumnType.Integer;
        if (allDouble) return DataColumnType.Number;
        if (allDate) return DataColumnType.Date;
        return DataColumnType.Text;
    }
}
