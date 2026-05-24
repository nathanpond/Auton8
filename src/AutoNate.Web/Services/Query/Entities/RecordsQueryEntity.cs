using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using FieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeField;

namespace AutoNate.Web.Services.Query.Entities;

// The Records entity adapter. Translates a validated AQL query into raw SQL
// against the records table, reusing RecordFilterCompiler for dynamic-field
// WHERE clauses and IAuthorizer.BuildRecordSqlFilterAsync for row visibility.
//
// System columns (resolved via JOIN) map ID columns to display names so the
// query language stays ID-free:
//   RecordType   -> record_types.name
//   CreatedBy    -> users.display_name
//   UpdatedBy    -> users.display_name
//   Assignees    -> string_agg(users.display_name)
public sealed class RecordsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IFieldTypeRegistry _fieldTypes;
    private readonly IAuthorizer _authorizer;

    public RecordsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IFieldTypeRegistry fieldTypes,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _fieldTypes = fieldTypes;
        _authorizer = authorizer;
    }

    public string Name => "Records";

    public IReadOnlyList<QueryColumn> StaticSchema => SystemColumns.Schema;

    public IReadOnlyList<string> AllowedFunctions => Array.Empty<string>();

    public async Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Walk the WHERE clause for literal RecordType references and resolve
        // them case-insensitively against record_types.name. Track which IDs
        // the resolved literals point at so the executor can both filter and
        // restrict the dynamic schema to those types' fields.
        var recordTypeLiterals = new List<string>();
        CollectRecordTypeLiterals(query.Where, recordTypeLiterals);

        var resolvedRecordTypeIds = new List<Guid>();
        if (recordTypeLiterals.Count > 0)
        {
            // RecordTypes is a small config table — pull all rows and resolve
            // case-insensitively in process. Avoids EF expression-tree issues
            // around translating `IList<string>.Contains(t.Name.ToLower())`.
            var allTypes = await db.RecordTypes.AsNoTracking()
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(cancellationToken);
            var byNameLc = allTypes
                .GroupBy(t => t.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Id);
            foreach (var literal in recordTypeLiterals.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (byNameLc.TryGetValue(literal.ToLowerInvariant(), out var id))
                {
                    resolvedRecordTypeIds.Add(id);
                }
                else
                {
                    errors.Add($"RecordType '{literal}' does not exist.");
                }
            }
        }

        // Resolve the dynamic schema. When at least one RecordType literal
        // resolves, schema = union of fields on those types. Otherwise, schema
        // = union of fields across every RecordType in the DB.
        IReadOnlyList<FieldEntity> dynamicFields;
        if (resolvedRecordTypeIds.Count > 0)
        {
            dynamicFields = await db.RecordTypeFields.AsNoTracking()
                .Where(f => resolvedRecordTypeIds.Contains(f.RecordTypeId) && !f.IsArchived)
                .ToListAsync(cancellationToken);
        }
        else if (recordTypeLiterals.Count == 0)
        {
            dynamicFields = await db.RecordTypeFields.AsNoTracking()
                .Where(f => !f.IsArchived)
                .ToListAsync(cancellationToken);
        }
        else
        {
            dynamicFields = Array.Empty<FieldEntity>();
        }

        // Build the column schema. Dedupe dynamic fields by FieldKey, keeping
        // the first occurrence; if two RecordTypes both define "color" with
        // different data types, the user can disambiguate by filtering on
        // RecordType. We pick whichever data type comes first; downstream
        // operator checks will catch mismatches.
        var schema = new List<QueryColumn>(SystemColumns.Schema);
        var seenFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fieldByName = new Dictionary<string, FieldEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dynamicFields)
        {
            if (!seenFieldKeys.Add(f.FieldKey)) continue;
            // Skip if a system column already owns this name.
            if (SystemColumns.ByName.ContainsKey(f.FieldKey)) continue;
            schema.Add(new QueryColumn(
                Name: f.FieldKey,
                DataType: MapFieldDataType(f.DataType),
                IsAggregable: IsAggregableDataType(f.DataType),
                IsSystem: false));
            fieldByName[f.FieldKey] = f;
        }

        return new RecordsPreparedQuery(
            entity: this,
            query: query,
            schema: schema,
            errors: errors,
            resolvedRecordTypeIds: resolvedRecordTypeIds,
            dynamicFields: fieldByName,
            dbFactory: _dbFactory,
            fieldTypes: _fieldTypes,
            authorizer: _authorizer);
    }

    private static void CollectRecordTypeLiterals(AqlWhere? where, List<string> sink)
    {
        if (where is null) return;
        switch (where)
        {
            case AqlBinary b:
                CollectRecordTypeLiterals(b.Left, sink);
                CollectRecordTypeLiterals(b.Right, sink);
                break;
            case AqlCompare c when IsRecordTypeField(c.Field):
                if (c.Op is "=" && c.Value is AqlString s) sink.Add(s.Value);
                break;
            case AqlIn inFilter when IsRecordTypeField(inFilter.Field):
                foreach (var v in inFilter.Values)
                {
                    if (v is AqlString str) sink.Add(str.Value);
                }
                break;
        }
    }

    private static bool IsRecordTypeField(string name) =>
        string.Equals(name, "RecordType", StringComparison.OrdinalIgnoreCase);

    private static QueryDataType MapFieldDataType(string dataType) => dataType switch
    {
        "text" or "email" or "phone" or "option" => QueryDataType.String,
        "number" => QueryDataType.Number,
        "boolean" => QueryDataType.Bool,
        "date" => QueryDataType.Date,
        _ => QueryDataType.String
    };

    private static bool IsAggregableDataType(string dataType) =>
        dataType is "number" or "date";
}

internal static class SystemColumns
{
    // System columns expose display names (no IDs) so the query language stays
    // ID-free. Each column knows its SQL projection expression and a per-op
    // builder lives in RecordsSqlTranslator. Keep this list in sync with the
    // record_types / users JOINs in the SELECT builder.
    public static readonly IReadOnlyList<QueryColumn> Schema = new[]
    {
        new QueryColumn("Id",          QueryDataType.String, false, true),
        new QueryColumn("Key",         QueryDataType.String, false, true),
        new QueryColumn("KeyNumber",   QueryDataType.Number, true,  true),
        new QueryColumn("Name",        QueryDataType.String, false, true),
        new QueryColumn("RecordType",  QueryDataType.String, false, true),
        new QueryColumn("Status",      QueryDataType.String, false, true),
        new QueryColumn("DueDate",     QueryDataType.Date,   true,  true),
        new QueryColumn("CreatedDate", QueryDataType.Date,   true,  true),
        new QueryColumn("UpdatedDate", QueryDataType.Date,   true,  true),
        new QueryColumn("CreatedBy",   QueryDataType.String, false, true),
        new QueryColumn("UpdatedBy",   QueryDataType.String, false, true),
        new QueryColumn("Assignees",   QueryDataType.String, false, true),
        new QueryColumn("IsArchived",  QueryDataType.Bool,   false, true)
    };

    public static readonly IReadOnlyDictionary<string, QueryColumn> ByName =
        Schema.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    // The "display name" for a user is resolved via a subquery against
    // local_users: first/last name when filled, falling back to username.
    // local_users.user_id is the join key (records.created_by is a Guid).
    private const string UserDisplayNameSelect =
        "SELECT COALESCE(NULLIF(TRIM(BOTH ' ' FROM " +
        "COALESCE(lu.first_name,'') || ' ' || COALESCE(lu.last_name,'')), ''), lu.username) " +
        "FROM local_users lu";

    public static string ToSqlExpr(string name) => name switch
    {
        "Id"          => "records.id",
        "Key"         => "records.key",
        "KeyNumber"   => "records.key_number",
        "Name"        => "records.name",
        "RecordType"  => "rt.name",
        "Status"      => "records.status",
        "DueDate"     => "records.due_date",
        "CreatedDate" => "records.created_at_utc",
        "UpdatedDate" => "records.updated_at_utc",
        "CreatedBy"   => $"({UserDisplayNameSelect} WHERE lu.user_id = records.created_by)",
        "UpdatedBy"   => $"({UserDisplayNameSelect} WHERE lu.user_id = records.updated_by)",
        "Assignees"   => $"(SELECT string_agg(({UserDisplayNameSelect} WHERE lu.user_id = a), ', ') FROM unnest(records.assignee_ids) AS a)",
        "IsArchived"  => "records.is_archived",
        _ => throw new InvalidOperationException($"Unknown system column '{name}'.")
    };
}

// A prepared query owns the resolved state: the schema, the resolved
// RecordType ids, the dynamic field defs by name. Calling ExecuteAsync builds
// SQL on top of those.
internal sealed class RecordsPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IFieldTypeRegistry _fieldTypes;
    private readonly IAuthorizer _authorizer;
    private readonly IReadOnlyList<Guid> _resolvedRecordTypeIds;
    private readonly IReadOnlyDictionary<string, FieldEntity> _dynamicFields;

    public RecordsPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        IReadOnlyList<string> errors,
        IReadOnlyList<Guid> resolvedRecordTypeIds,
        IReadOnlyDictionary<string, FieldEntity> dynamicFields,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IFieldTypeRegistry fieldTypes,
        IAuthorizer authorizer)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = errors;
        _resolvedRecordTypeIds = resolvedRecordTypeIds;
        _dynamicFields = dynamicFields;
        _dbFactory = dbFactory;
        _fieldTypes = fieldTypes;
        _authorizer = authorizer;
    }

    public IQueryEntity Entity { get; }
    public AqlQuery Query { get; }
    public IReadOnlyList<QueryColumn> Schema { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    public async Task<QueryResult> ExecuteAsync(
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Build the projection. If COLUMNS() is set, use those; otherwise
        // every schema column.
        var projection = ResolveProjection();

        // Build the WHERE.
        var paramList = new List<object?>();
        var whereBuilder = new StringBuilder();

        // Restrict to resolved RecordType IDs (if any).
        if (_resolvedRecordTypeIds.Count > 0)
        {
            var idsArr = _resolvedRecordTypeIds.ToArray();
            AppendParam(paramList, idsArr, out var idIdx);
            whereBuilder.Append($"records.record_type_id = ANY({{{idIdx}}})");
        }

        if (Query.Where is not null)
        {
            if (whereBuilder.Length > 0) whereBuilder.Append(" AND ");
            whereBuilder.Append('(').Append(TranslateWhere(Query.Where, paramList)).Append(')');
        }

        // Authorization filter.
        var visibility = await _authorizer.BuildRecordSqlFilterAsync(
            actor, Actions.View, paramList.Count, cancellationToken);
        if (!visibility.AccessOpen)
        {
            if (whereBuilder.Length > 0) whereBuilder.Append(" AND ");
            whereBuilder.Append(visibility.Sql);
            paramList.AddRange(visibility.Parameters);
        }

        // Build ORDER BY.
        var orderBy = BuildOrderBy();

        // Build GROUP BY.
        var groupBy = BuildGroupBy();

        // Resolve LIMIT — explicit takes precedence; otherwise apply hardCap
        // when present. Always +1 over the cap so we can detect truncation.
        int? effectiveCap = Query.Limit ?? hardCap;
        var fetchN = effectiveCap is { } cap ? cap + 1 : (int?)null;

        // Build the SELECT.
        var selectSql = new StringBuilder();
        selectSql.Append("SELECT ");
        for (var i = 0; i < projection.Count; i++)
        {
            if (i > 0) selectSql.Append(", ");
            var item = projection[i];
            selectSql.Append(ProjectionToSql(item, paramList));
            selectSql.Append(" AS ").Append(QuoteIdent(item.DisplayName));
        }
        selectSql.Append(" FROM records");
        selectSql.Append(" LEFT JOIN record_types rt ON rt.id = records.record_type_id");
        if (whereBuilder.Length > 0)
        {
            selectSql.Append(" WHERE ").Append(whereBuilder);
        }
        if (groupBy is not null)
        {
            selectSql.Append(" GROUP BY ").Append(groupBy);
        }
        if (orderBy is not null)
        {
            selectSql.Append(" ORDER BY ").Append(orderBy);
        }
        if (fetchN is { } n)
        {
            selectSql.Append(" LIMIT ").Append(n);
        }

        // Materialize.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await RawSqlRunner.ExecuteAsync(
            db, selectSql.ToString(), paramList, projection, cancellationToken);

        var truncated = effectiveCap is { } c && rows.Count > c;
        if (truncated)
        {
            rows = rows.Take(effectiveCap!.Value).ToList();
        }

        var columnMeta = projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList();
        return new QueryResult(
            Columns: columnMeta,
            Rows: rows,
            TotalCount: rows.Count + (truncated ? 1 : 0),
            Truncated: truncated,
            DurationMs: sw.ElapsedMilliseconds);
    }

    // ---- Projection -------------------------------------------------------

    private record ProjectionItem(
        string DisplayName,
        QueryDataType DataType,
        AqlSelectItem? Source);

    private IReadOnlyList<ProjectionItem> ResolveProjection()
    {
        if (Query.Columns is not null)
        {
            return Query.Columns.Select(SelectItemToProjection).ToList();
        }
        if (Query.Group is { } group)
        {
            return group
                .Select(name => new ProjectionItem(name, GetColumn(name).DataType,
                    new AqlSelectItem(name, null, null)))
                .ToList();
        }
        return Schema.Select(c => new ProjectionItem(
            c.Name, c.DataType, new AqlSelectItem(c.Name, null, null))).ToList();
    }

    private ProjectionItem SelectItemToProjection(AqlSelectItem item)
    {
        if (item.IsAggregate)
        {
            var dt = item.AggregateField is null ? QueryDataType.Number
                : GetColumn(item.AggregateField).DataType;
            // COUNT/MIN/MAX/MEDIAN over a date returns date; AVG returns number.
            if (item.AggregateFn == "AVG" || item.AggregateFn == "COUNT")
            {
                dt = QueryDataType.Number;
            }
            return new ProjectionItem(item.DisplayName, dt, item);
        }
        var col = GetColumn(item.Field!);
        // `AS <alias>` is captured on the select item; fall back to the
        // underlying column name when no alias is present.
        return new ProjectionItem(item.DisplayName, col.DataType, item);
    }

    private QueryColumn GetColumn(string name) =>
        Schema.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Column '{name}' was not in the resolved schema.");

    private string ProjectionToSql(ProjectionItem item, List<object?> paramList)
    {
        var src = item.Source!;
        if (src.IsAggregate)
        {
            var fn = src.AggregateFn!;
            if (src.AggregateField is null)
            {
                return fn == "COUNT" ? "COUNT(*)" : throw new InvalidOperationException();
            }
            var expr = FieldExprForRead(src.AggregateField);
            return fn switch
            {
                "COUNT" => $"COUNT({expr})",
                "MIN" => $"MIN({expr})",
                "MAX" => $"MAX({expr})",
                "AVG" => $"AVG({expr})",
                "MEDIAN" => $"PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY {expr})",
                _ => throw new InvalidOperationException($"Unknown aggregate '{fn}'.")
            };
        }
        return FieldExprForRead(src.Field!);
    }

    private string FieldExprForRead(string name)
    {
        if (SystemColumns.ByName.ContainsKey(name))
        {
            return SystemColumns.ToSqlExpr(name);
        }
        if (_dynamicFields.TryGetValue(name, out var field))
        {
            return BuildJsonReadExpr(field.FieldKey, field.DataType);
        }
        throw new InvalidOperationException($"Unknown column '{name}'.");
    }

    private static string BuildJsonReadExpr(string fieldKey, string dataType)
    {
        var key = fieldKey.Replace("'", "''");
        return dataType switch
        {
            "number" => $"NULLIF(records.values->>'{key}', '')::numeric",
            "boolean" => $"NULLIF(records.values->>'{key}', '')::boolean",
            "date" => $"NULLIF(records.values->>'{key}', '')::date",
            _ => $"records.values->>'{key}'"
        };
    }

    // ---- ORDER BY / GROUP BY ---------------------------------------------

    private string? BuildOrderBy()
    {
        if (Query.OrderBy.Count == 0) return null;
        var parts = new List<string>();
        foreach (var item in Query.OrderBy)
        {
            string expr;
            if (item.Item.IsAggregate)
            {
                expr = ProjectionToSql(SelectItemToProjection(item.Item), new List<object?>());
            }
            else
            {
                expr = FieldExprForRead(item.Item.Field!);
            }
            parts.Add($"{expr} {(item.Descending ? "DESC" : "ASC")} NULLS LAST");
        }
        return string.Join(", ", parts);
    }

    private string? BuildGroupBy()
    {
        if (Query.Group is null) return null;
        return string.Join(", ", Query.Group.Select(g => FieldExprForRead(g)));
    }

    // ---- WHERE translation ------------------------------------------------

    private string TranslateWhere(AqlWhere where, List<object?> paramList) => where switch
    {
        AqlBinary b => $"({TranslateWhere(b.Left, paramList)} {b.Op} {TranslateWhere(b.Right, paramList)})",
        AqlCompare c => TranslateCompare(c, paramList),
        AqlContains ct => TranslateContains(ct, paramList),
        AqlIn inFilter => TranslateIn(inFilter, paramList),
        AqlBetween bw => TranslateBetween(bw, paramList),
        AqlFunctionCall fc => throw new AqlValidationException(
            $"Function '{fc.Name}()' is not supported on Records."),
        AqlFunctionCompare fcmp => throw new AqlValidationException(
            $"Function '{fcmp.FnName}()' is not supported on Records."),
        _ => throw new InvalidOperationException("Unknown WHERE node.")
    };

    private string TranslateCompare(AqlCompare c, List<object?> paramList)
    {
        var col = GetColumn(c.Field);
        if (col.IsSystem)
        {
            return TranslateSystemCompare(col, c.Op, c.Value, paramList);
        }
        // Dynamic field — delegate to RecordFilterCompiler via a single clause.
        return TranslateDynamicCompare(c.Field, c.Op, c.Value, paramList);
    }

    private static string TranslateSystemCompare(
        QueryColumn col, string op, AqlValue value, List<object?> paramList)
    {
        var expr = SystemColumns.ToSqlExpr(col.Name);
        // RecordType "=" was already absorbed into record_type_id ANY(...)
        // during PrepareAsync, so the literal lookup doesn't fire again here.
        if (col.Name == "RecordType" && op == "=" && value is AqlString)
        {
            // We've already constrained record_type_id; emit TRUE to keep the
            // boolean structure intact in compound expressions.
            return "TRUE";
        }
        switch (value)
        {
            case AqlString s:
                {
                    if (op == "~")
                    {
                        AppendParam(paramList, "%" + EscapeLike(s.Value) + "%", out var idx);
                        return $"{expr} ILIKE {{{idx}}}";
                    }
                    if (col.Name == "RecordType")
                    {
                        // RecordType != "X" or other rare ops — compare on name.
                        AppendParam(paramList, s.Value, out var idx);
                        return $"LOWER({expr}) {op} LOWER({{{idx}}})";
                    }
                    AppendParam(paramList, s.Value, out var i2);
                    return $"{expr} {op} {{{i2}}}";
                }
            case AqlNumber n:
                AppendParam(paramList, n.Value, out var ni);
                return $"{expr} {op} {{{ni}}}";
            case AqlBool b:
                AppendParam(paramList, b.Value, out var bi);
                return $"{expr} {op} {{{bi}}}";
            case AqlNull:
                return op switch
                {
                    "=" => $"{expr} IS NULL",
                    "!=" => $"{expr} IS NOT NULL",
                    _ => throw new AqlValidationException($"Operator '{op}' is not supported against NULL.")
                };
            case AqlRelativeDate r:
                AppendParam(paramList, r.Resolve(DateTime.UtcNow), out var ri);
                return $"{expr} {op} {{{ri}}}";
            default:
                throw new InvalidOperationException("Unhandled value kind.");
        }
    }

    private string TranslateDynamicCompare(string fieldName, string op, AqlValue value, List<object?> paramList)
    {
        var field = _dynamicFields[fieldName];
        if (!_fieldTypes.TryGet(field.DataType, out var fieldType))
        {
            throw new AqlValidationException($"Unknown data type '{field.DataType}' for field '{fieldName}'.");
        }
        var fop = MapOperator(op);
        var operand = ToJsonElement(value);
        FilterSqlFragment fragment;
        try
        {
            fragment = fieldType.BuildFilter(field.FieldKey, fop, operand,
                ParseConfig(field.Config));
        }
        catch (NotSupportedException ex)
        {
            throw new AqlValidationException(
                $"Operator '{op}' is not supported for {field.DataType} field '{fieldName}': {ex.Message}");
        }
        var rewritten = RewriteFragment(fragment, paramList, "records");
        return rewritten;
    }

    private static FilterOperator MapOperator(string op) => op switch
    {
        "=" => FilterOperator.Equals,
        "!=" => FilterOperator.NotEquals,
        "<" => FilterOperator.LessThan,
        "<=" => FilterOperator.LessThanOrEqual,
        ">" => FilterOperator.GreaterThan,
        ">=" => FilterOperator.GreaterThanOrEqual,
        "~" => FilterOperator.Contains,
        _ => throw new AqlValidationException($"Unknown operator '{op}'.")
    };

    private static JsonElement ToJsonElement(AqlValue value)
    {
        var json = value switch
        {
            AqlString s => JsonSerializer.Serialize(s.Value),
            AqlNumber n => JsonSerializer.Serialize(n.Value),
            AqlBool b => b.Value ? "true" : "false",
            AqlNull => "null",
            AqlRelativeDate r => JsonSerializer.Serialize(r.Resolve(DateTime.UtcNow)
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            _ => "null"
        };
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement ParseConfig(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return JsonDocument.Parse("{}").RootElement.Clone();
        try { return JsonDocument.Parse(raw).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private string TranslateContains(AqlContains ct, List<object?> paramList)
    {
        var col = GetColumn(ct.Field);
        var expr = col.IsSystem ? SystemColumns.ToSqlExpr(col.Name) : FieldExprForRead(ct.Field);
        AppendParam(paramList, "%" + EscapeLike(ct.Substr) + "%", out var idx);
        return $"{expr} ILIKE {{{idx}}}";
    }

    private string TranslateIn(AqlIn inFilter, List<object?> paramList)
    {
        var col = GetColumn(inFilter.Field);
        if (col.IsSystem && col.Name == "RecordType")
        {
            // RecordType IN (...) was already absorbed into record_type_id ANY(...)
            return "TRUE";
        }
        var fragments = inFilter.Values
            .Select(v => col.IsSystem
                ? TranslateSystemCompare(col, "=", v, paramList)
                : TranslateDynamicCompare(inFilter.Field, "=", v, paramList))
            .ToList();
        return "(" + string.Join(" OR ", fragments) + ")";
    }

    private string TranslateBetween(AqlBetween bw, List<object?> paramList)
    {
        var col = GetColumn(bw.Field);
        var lo = col.IsSystem ? TranslateSystemCompare(col, ">=", bw.Lo, paramList)
                              : TranslateDynamicCompare(bw.Field, ">=", bw.Lo, paramList);
        var hi = col.IsSystem ? TranslateSystemCompare(col, "<=", bw.Hi, paramList)
                              : TranslateDynamicCompare(bw.Field, "<=", bw.Hi, paramList);
        return $"({lo} AND {hi})";
    }

    // ---- Parameter / placeholder helpers ---------------------------------

    private static void AppendParam(List<object?> list, object value, out int index)
    {
        index = list.Count;
        list.Add(value);
    }

    private static string RewriteFragment(FilterSqlFragment fragment, List<object?> paramList, string tableAlias)
    {
        // The field-type's fragment references the unqualified `values` column
        // (e.g. `values->>'foo'`). Qualify with our table reference so the
        // compound query JOINs cleanly.
        var sql = fragment.Sql.Replace("values->>'", $"{tableAlias}.values->>'");
        var offset = paramList.Count;
        var output = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '{')
            {
                var close = sql.IndexOf('}', i);
                if (close > i && int.TryParse(sql.AsSpan(i + 1, close - i - 1), out var idx))
                {
                    output.Append('{').Append(idx + offset).Append('}');
                    i = close;
                    continue;
                }
            }
            output.Append(sql[i]);
        }
        paramList.AddRange(fragment.Parameters);
        return output.ToString();
    }

    private static string QuoteIdent(string name) =>
        "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string EscapeLike(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\\' || c == '%' || c == '_') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

// ADO.NET helper: takes a SQL string with `{N}`-style placeholders and a
// flat parameter list, rewrites placeholders to `@pN`, executes via the
// shared DbConnection, and materializes rows into per-column dictionaries.
internal static class RawSqlRunner
{
    public static async Task<List<IReadOnlyDictionary<string, object?>>> ExecuteAsync(
        AutoNateDbContext db,
        string sqlWithBraceParams,
        IReadOnlyList<object?> parameters,
        IReadOnlyList<object> projection,
        CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var sqlPg = RewriteBracesToNamed(sqlWithBraceParams);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sqlPg;
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "p" + i;
            p.Value = parameters[i] ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var fieldCount = reader.FieldCount;
        var names = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++) names[i] = reader.GetName(i);

        while (await reader.ReadAsync(cancellationToken))
        {
            var dict = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
            for (var i = 0; i < fieldCount; i++)
            {
                var v = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                dict[names[i]] = v;
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static string RewriteBracesToNamed(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '{')
            {
                var close = sql.IndexOf('}', i);
                if (close > i && int.TryParse(sql.AsSpan(i + 1, close - i - 1), out var idx))
                {
                    sb.Append('@').Append('p').Append(idx);
                    i = close;
                    continue;
                }
            }
            sb.Append(sql[i]);
        }
        return sb.ToString();
    }
}
