using System.Globalization;
using System.Text;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Npgsql;

namespace AutoNate.Web.Services.Datasets;

// Translates the subset of AqlAst that's safely pushdown-able into a
// parameterized SELECT against a single `<schema>.<table>` reference.
// Anything outside the pushdown set (function calls, BETWEEN, IN, OR
// branches) falls through to in-memory filtering applied by the caller
// — this builder returns null when it can't translate cleanly.
internal static class DatasetSqlBuilder
{
    public sealed record Built(NpgsqlCommand Command, int ParameterCount);

    public static Built Build(
        string schemaName,
        string tableName,
        IReadOnlyList<QueryColumn> schema,
        AqlQuery query,
        int? hardCap)
    {
        var paramIndex = 0;
        var sb = new StringBuilder();
        var byName = schema.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        // Projection: COLUMNS or `*`. Aggregates aren't pushed down in v1 —
        // any aggregate falls back to in-memory grouping; the caller checks
        // query.Columns shape before invoking this builder.
        sb.Append("SELECT ");
        if (query.Columns is { Count: > 0 } cols && cols.All(c => !c.IsAggregate))
        {
            sb.Append(string.Join(", ",
                cols.Select(c => QuoteIdent(c.Field!))));
        }
        else
        {
            sb.Append('*');
        }

        sb.Append(" FROM ");
        sb.Append(QuoteIdent(schemaName));
        sb.Append('.');
        sb.Append(QuoteIdent(tableName));

        var cmd = new NpgsqlCommand();

        if (query.Where is not null)
        {
            var clause = TranslateWhere(query.Where, byName, cmd, ref paramIndex);
            if (clause is not null)
            {
                sb.Append(" WHERE ");
                sb.Append(clause);
            }
        }

        if (query.OrderBy is { Count: > 0 })
        {
            var parts = new List<string>();
            foreach (var o in query.OrderBy)
            {
                if (o.Item.IsAggregate || o.Item.Field is null) continue;
                if (!byName.ContainsKey(o.Item.Field)) continue;
                parts.Add($"{QuoteIdent(o.Item.Field)} {(o.Descending ? "DESC" : "ASC")}");
            }
            if (parts.Count > 0)
            {
                sb.Append(" ORDER BY ");
                sb.Append(string.Join(", ", parts));
            }
        }

        var effectiveLimit = ResolveLimit(query.Limit, hardCap);
        if (effectiveLimit is not null)
        {
            sb.Append(" LIMIT ");
            sb.Append(effectiveLimit.Value.ToString(CultureInfo.InvariantCulture));
        }

        cmd.CommandText = sb.ToString();
        return new Built(cmd, paramIndex);
    }

    private static int? ResolveLimit(int? userLimit, int? hardCap)
    {
        if (userLimit is { } u && hardCap is { } h) return Math.Min(u, h);
        return userLimit ?? hardCap;
    }

    private static string? TranslateWhere(
        AqlWhere where,
        IReadOnlyDictionary<string, QueryColumn> schema,
        NpgsqlCommand cmd,
        ref int paramIndex)
    {
        switch (where)
        {
            case AqlBinary { Op: var op, Left: var l, Right: var r }:
                {
                    if (!string.Equals(op, "AND", StringComparison.OrdinalIgnoreCase))
                        return null;
                    var ls = TranslateWhere(l, schema, cmd, ref paramIndex);
                    var rs = TranslateWhere(r, schema, cmd, ref paramIndex);
                    if (ls is null || rs is null) return null;
                    return $"({ls} AND {rs})";
                }

            case AqlCompare cmp:
                {
                    if (!schema.ContainsKey(cmp.Field)) return null;
                    var op = TranslateOp(cmp.Op);
                    if (op is null) return null;
                    var name = $"p{paramIndex++}";
                    cmd.Parameters.AddWithValue(name, BindValue(cmp.Value));
                    return $"{QuoteIdent(cmp.Field)} {op} @{name}";
                }

            // Function calls / IN / BETWEEN / CONTAINS not in v1 pushdown.
            default:
                return null;
        }
    }

    private static string? TranslateOp(string op) => op switch
    {
        "=" or "==" => "=",
        "!=" or "<>" => "<>",
        "<" => "<",
        "<=" => "<=",
        ">" => ">",
        ">=" => ">=",
        _ => null,
    };

    private static object BindValue(AqlValue value) => value switch
    {
        AqlString s => s.Value,
        AqlNumber n => n.Value,
        AqlBool b => b.Value,
        AqlNull => DBNull.Value,
        AqlRelativeDate rd => rd.Resolve(DateTime.UtcNow),
        _ => DBNull.Value,
    };

    public static string QuoteIdent(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
