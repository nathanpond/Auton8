using AutoNate.Web.Services.Query;

namespace AutoNate.Web.Services.Datasets;

// Tiny in-memory WHERE evaluator for the v1 row-set sources that don't push
// down to SQL (Virtual + File datasets). Supports the same predicate set
// the SQL builder pushes down: AqlBinary AND, AqlCompare with =/!=/<,
// <=/>/>=. Anything else evaluates to true (preserve the user's row set
// rather than silently dropping rows on an un-translated branch).
internal static class InMemoryWhere
{
    public static bool Match(IReadOnlyDictionary<string, object?> row, AqlWhere? where)
    {
        if (where is null) return true;
        switch (where)
        {
            case AqlBinary { Op: var op, Left: var l, Right: var r }:
                if (string.Equals(op, "AND", StringComparison.OrdinalIgnoreCase))
                    return Match(row, l) && Match(row, r);
                if (string.Equals(op, "OR", StringComparison.OrdinalIgnoreCase))
                    return Match(row, l) || Match(row, r);
                return true;

            case AqlCompare cmp:
                return Compare(LookupRowValue(row, cmp.Field), cmp.Op, ResolveValue(cmp.Value));

            default:
                return true;
        }
    }

    private static object? LookupRowValue(IReadOnlyDictionary<string, object?> row, string field)
    {
        // Row keys are produced with Ordinal comparison upstream; do a
        // case-insensitive fallback for resilience against schema-vs-row
        // casing drift.
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    private static object? ResolveValue(AqlValue v) => v switch
    {
        AqlString s => s.Value,
        AqlNumber n => n.Value,
        AqlBool b => b.Value,
        AqlNull => null,
        AqlRelativeDate rd => rd.Resolve(DateTime.UtcNow),
        _ => null,
    };

    private static bool Compare(object? a, string op, object? b)
    {
        if (a is null || b is null)
        {
            return op switch
            {
                "=" or "==" => a is null && b is null,
                "!=" or "<>" => !(a is null && b is null),
                _ => false,
            };
        }
        var cmp = CompareTo(a, b);
        return op switch
        {
            "=" or "==" => cmp == 0,
            "!=" or "<>" => cmp != 0,
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            _ => true,
        };
    }

    private static int CompareTo(object a, object b)
    {
        // Same-type fast path; otherwise coerce to double or string.
        if (a is IComparable ca && a.GetType() == b.GetType())
        {
            return ca.CompareTo(b);
        }
        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
        {
            return da.CompareTo(db);
        }
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static bool TryToDouble(object o, out double value)
    {
        switch (o)
        {
            case double d: value = d; return true;
            case float f: value = f; return true;
            case long l: value = l; return true;
            case int i: value = i; return true;
            case short s: value = s; return true;
            case decimal m: value = (double)m; return true;
        }
        value = 0;
        return false;
    }
}
