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
    internal sealed record ProjectionItem(
        string DisplayName,
        QueryDataType DataType,
        AqlSelectItem Source);

    public sealed record Built(
        NpgsqlCommand Command,
        int ParameterCount,
        IReadOnlyList<ProjectionItem> Projection);

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

        var projection = ResolveProjection(query, byName);

        sb.Append("SELECT ");
        if (projection.Count > 0)
        {
            sb.Append(string.Join(", ", projection.Select(p =>
                ProjectionToSql(p.Source) + " AS " + QuoteIdent(p.DisplayName))));
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

        if (query.Group is { Count: > 0 } group)
        {
            var groupParts = group
                .Where(g => byName.ContainsKey(g))
                .Select(QuoteIdent)
                .ToList();
            if (groupParts.Count > 0)
            {
                sb.Append(" GROUP BY ");
                sb.Append(string.Join(", ", groupParts));
            }
        }

        if (query.OrderBy is { Count: > 0 })
        {
            // Aliases declared in COLUMNS(... AS name). Postgres resolves
            // `ORDER BY "alias"` against the projection's AS clause natively,
            // so we emit the alias as a quoted identifier when the ORDER BY
            // field matches one. Validator (with SupportsAliasOrderBy=true)
            // has already accepted the reference.
            var aliasNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (query.Columns is not null)
            {
                foreach (var c in query.Columns)
                {
                    if (c.Alias is not null) aliasNames.Add(c.Alias);
                }
            }
            var parts = new List<string>();
            foreach (var o in query.OrderBy)
            {
                string? expr = null;
                if (o.Item.IsAggregate)
                {
                    expr = ProjectionToSql(o.Item);
                }
                else if (o.Item.Field is not null)
                {
                    // Alias wins over a source column of the same name —
                    // matches SQL ORDER-BY-by-alias semantics.
                    if (aliasNames.Contains(o.Item.Field) || byName.ContainsKey(o.Item.Field))
                    {
                        expr = QuoteIdent(o.Item.Field);
                    }
                }
                if (expr is null) continue;
                parts.Add($"{expr} {(o.Descending ? "DESC" : "ASC")}");
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
        return new Built(cmd, paramIndex, projection);
    }

    // Resolve the projected columns. Explicit COLUMNS wins. If GROUP is
    // set without COLUMNS, project the group columns (a bare `SELECT *`
    // would violate aggregation rules). Otherwise leave empty so the
    // caller emits `*`.
    private static IReadOnlyList<ProjectionItem> ResolveProjection(
        AqlQuery query,
        IReadOnlyDictionary<string, QueryColumn> byName)
    {
        if (query.Columns is { Count: > 0 } cols)
        {
            return cols.Select(c => ToProjection(c, byName)).ToList();
        }
        if (query.Group is { Count: > 0 } group)
        {
            return group
                .Where(g => byName.ContainsKey(g))
                .Select(g => new ProjectionItem(
                    g, byName[g].DataType, new AqlSelectItem(g, null, null)))
                .ToList();
        }
        return Array.Empty<ProjectionItem>();
    }

    private static ProjectionItem ToProjection(
        AqlSelectItem item,
        IReadOnlyDictionary<string, QueryColumn> byName)
    {
        if (item.IsAggregate)
        {
            // COUNT/AVG always return numeric; MIN/MAX/MEDIAN keep the
            // underlying column's type when known.
            var dt = QueryDataType.Number;
            if (item.AggregateFn is not "COUNT" and not "AVG"
                && item.AggregateField is not null
                && byName.TryGetValue(item.AggregateField, out var aggCol))
            {
                dt = aggCol.DataType;
            }
            return new ProjectionItem(item.DisplayName, dt, item);
        }
        var dataType = item.Field is not null && byName.TryGetValue(item.Field, out var c)
            ? c.DataType
            : QueryDataType.String;
        return new ProjectionItem(item.DisplayName, dataType, item);
    }

    private static string ProjectionToSql(AqlSelectItem item)
    {
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!;
            if (item.AggregateField is null)
            {
                return fn == "COUNT"
                    ? "COUNT(*)"
                    : throw new InvalidOperationException($"{fn}() requires a column argument.");
            }
            var expr = QuoteIdent(item.AggregateField);
            return fn switch
            {
                "COUNT" => $"COUNT({expr})",
                "MIN" => $"MIN({expr})",
                "MAX" => $"MAX({expr})",
                "AVG" => $"AVG({expr})",
                "MEDIAN" => $"PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY {expr})",
                _ => throw new InvalidOperationException($"Unknown aggregate '{fn}'."),
            };
        }
        return QuoteIdent(item.Field!);
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
