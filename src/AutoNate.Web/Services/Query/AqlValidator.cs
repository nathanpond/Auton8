using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Query;

// Walks a parsed AqlQuery and produces a friendly error list. Schema-aware
// checks (field existence, operator-type compatibility) come from the
// entity. Pipeline-shape checks (GROUP rules, LIMIT bounds) live here.
internal sealed class AqlValidator
{
    private readonly IQueryEntityRegistry _registry;

    public AqlValidator(IQueryEntityRegistry registry)
    {
        _registry = registry;
    }

    public async Task<IPreparedQuery> ValidateAsync(
        AqlQuery query,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (!_registry.TryGet(query.Entity, out var entity))
        {
            throw new AqlValidationException(
                $"Unknown entity '{query.Entity}'. Available: {string.Join(", ", _registry.EntityNames)}.");
        }

        var prepared = await entity.PrepareAsync(query, cancellationToken);
        errors.AddRange(prepared.ValidationErrors);
        var schema = prepared.Schema;
        var byName = schema.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        QueryColumn? Lookup(string? name)
        {
            if (name is null) return null;
            return byName.TryGetValue(name, out var c) ? c : null;
        }

        // WHERE clause walk.
        if (query.Where is not null)
        {
            ValidateWhere(query.Where, entity, byName, errors);
        }

        // ORDER BY items: each is either a column ref or an aggregate call.
        foreach (var item in query.OrderBy)
        {
            ValidateSelectItem(item.Item, "ORDER BY", entity, byName, query.Group, errors);
        }

        // COLUMNS items.
        if (query.Columns is not null)
        {
            foreach (var item in query.Columns)
            {
                ValidateSelectItem(item, "COLUMNS", entity, byName, query.Group, errors);
            }
        }

        // GROUP rules: every COLUMNS / ORDER BY item must be in GROUP() OR
        // be an aggregate. The grouped columns themselves must exist.
        if (query.Group is not null)
        {
            foreach (var g in query.Group)
            {
                if (Lookup(g) is null)
                {
                    errors.Add($"Unknown field '{g}' in GROUP() for entity '{entity.Name}'.");
                }
            }
            var groupSet = new HashSet<string>(query.Group, StringComparer.OrdinalIgnoreCase);
            void AssertGroupingOk(AqlSelectItem item, string ctx)
            {
                if (item.IsAggregate) return;
                if (item.Field is not null && groupSet.Contains(item.Field)) return;
                if (item.Field is not null)
                {
                    errors.Add($"{ctx} contains non-grouped column '{item.Field}' without aggregation. " +
                               $"Either add it to GROUP() or wrap it in COUNT/MIN/MAX/AVG/MEDIAN.");
                }
            }
            foreach (var item in query.OrderBy) AssertGroupingOk(item.Item, "ORDER BY");
            if (query.Columns is not null)
            {
                foreach (var item in query.Columns) AssertGroupingOk(item, "COLUMNS()");
            }
        }
        else
        {
            // Without GROUP, aggregates are illegal — except entity row
            // functions, which are evaluated per row and never aggregate.
            void AssertNoAggregate(AqlSelectItem item, string ctx)
            {
                if (!item.IsAggregate) return;
                var fn = item.AggregateFn!;
                if (entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
                errors.Add($"Aggregate '{fn}()' in {ctx} requires a GROUP(...) clause.");
            }
            foreach (var item in query.OrderBy) AssertNoAggregate(item.Item, "ORDER BY");
            if (query.Columns is not null)
            {
                foreach (var item in query.Columns) AssertNoAggregate(item, "COLUMNS()");
            }
        }

        // LIMIT bounds.
        if (query.Limit is { } limit)
        {
            if (limit <= 0)
            {
                errors.Add($"LIMIT must be positive; got {limit}.");
            }
            else if (hardCap is { } cap && limit > cap)
            {
                errors.Add($"LIMIT {limit} exceeds the maximum of {cap}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new AqlValidationException(errors);
        }

        return prepared;
    }

    private static void ValidateSelectItem(
        AqlSelectItem item,
        string ctx,
        IQueryEntity entity,
        IReadOnlyDictionary<string, QueryColumn> byName,
        IReadOnlyList<string>? group,
        List<string> errors)
    {
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!;
            // Entity-specific row functions: no-arg scalar calls evaluated
            // per row (e.g. COUNTCHILDREN() on Notes). These don't require
            // a GROUP() clause; the entity computes the value per row.
            if (entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
            {
                if (item.AggregateField is not null
                    && !entity.RowFunctionAcceptsArgument(fn))
                {
                    errors.Add($"{fn}() does not take an argument ({ctx}).");
                }
                return;
            }
            if (fn is not ("COUNT" or "MIN" or "MAX" or "AVG" or "MEDIAN"))
            {
                errors.Add($"Unknown aggregate function '{fn}' in {ctx}. " +
                           "Available: COUNT, MIN, MAX, AVG, MEDIAN.");
                return;
            }
            // COUNT() with no argument is allowed; everything else needs one.
            if (item.AggregateField is null)
            {
                if (fn != "COUNT")
                {
                    errors.Add($"{fn}() requires a column argument in {ctx}.");
                }
                return;
            }
            if (!byName.TryGetValue(item.AggregateField, out var col))
            {
                errors.Add($"Unknown field '{item.AggregateField}' in {fn}() ({ctx}).");
                return;
            }
            if (fn is not "COUNT" && col.DataType is not (QueryDataType.Number or QueryDataType.Date))
            {
                errors.Add($"{fn}() requires a numeric or date column; '{col.Name}' is {col.DataType}.");
            }
            return;
        }

        if (item.Field is null)
        {
            errors.Add($"{ctx} item is missing a field name.");
            return;
        }
        if (!byName.ContainsKey(item.Field))
        {
            errors.Add($"Unknown field '{item.Field}' for entity '{entity.Name}' ({ctx}).");
        }
    }

    private static void ValidateWhere(
        AqlWhere where,
        IQueryEntity entity,
        IReadOnlyDictionary<string, QueryColumn> byName,
        List<string> errors)
    {
        switch (where)
        {
            case AqlBinary b:
                ValidateWhere(b.Left, entity, byName, errors);
                ValidateWhere(b.Right, entity, byName, errors);
                break;

            case AqlCompare c:
                CheckField(c.Field, c.Op, c.Value, entity, byName, errors);
                break;

            case AqlContains contains:
                CheckFieldOp(contains.Field, "~", QueryDataType.String, entity, byName, errors);
                break;

            case AqlIn inFilter:
                if (!byName.TryGetValue(inFilter.Field, out var inCol))
                {
                    errors.Add($"Unknown field '{inFilter.Field}' in IN().");
                    break;
                }
                if (inFilter.Values.Count == 0)
                {
                    errors.Add("IN() requires at least one value.");
                }
                foreach (var v in inFilter.Values)
                {
                    if (!IsValueCompatible(inCol.DataType, v))
                    {
                        errors.Add($"Value '{Describe(v)}' is not compatible with field '{inCol.Name}' ({inCol.DataType}).");
                    }
                }
                break;

            case AqlBetween between:
                if (!byName.TryGetValue(between.Field, out var btCol))
                {
                    errors.Add($"Unknown field '{between.Field}' in BETWEEN().");
                    break;
                }
                if (btCol.DataType is not (QueryDataType.Number or QueryDataType.Date))
                {
                    errors.Add($"BETWEEN() requires a numeric or date field; '{btCol.Name}' is {btCol.DataType}.");
                }
                if (!IsValueCompatible(btCol.DataType, between.Lo) || !IsValueCompatible(btCol.DataType, between.Hi))
                {
                    errors.Add($"BETWEEN() values must be compatible with '{btCol.Name}' ({btCol.DataType}).");
                }
                break;

            case AqlFunctionCall fc:
                if (!entity.AllowedFunctions.Any(f => string.Equals(f, fc.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Function '{fc.Name}()' is not supported for entity '{entity.Name}'. " +
                               $"Available: {(entity.AllowedFunctions.Count == 0 ? "(none)" : string.Join(", ", entity.AllowedFunctions))}.");
                }
                break;

            case AqlFunctionCompare fcmp:
                if (!entity.AllowedFunctions.Any(f => string.Equals(f, fcmp.FnName, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Function '{fcmp.FnName}()' is not supported for entity '{entity.Name}'. " +
                               $"Available: {(entity.AllowedFunctions.Count == 0 ? "(none)" : string.Join(", ", entity.AllowedFunctions))}.");
                }
                break;
        }
    }

    private static void CheckField(
        string fieldName,
        string op,
        AqlValue value,
        IQueryEntity entity,
        IReadOnlyDictionary<string, QueryColumn> byName,
        List<string> errors)
    {
        if (!byName.TryGetValue(fieldName, out var col))
        {
            errors.Add($"Unknown field '{fieldName}' for entity '{entity.Name}' (WHERE).");
            return;
        }

        if (!IsOperatorSupported(col.DataType, op))
        {
            errors.Add($"Operator '{op}' is not supported for {col.DataType.ToString().ToLowerInvariant()} field '{col.Name}'.");
            return;
        }

        if (!IsValueCompatible(col.DataType, value))
        {
            errors.Add($"Value '{Describe(value)}' is not compatible with field '{col.Name}' ({col.DataType}).");
        }
    }

    private static void CheckFieldOp(
        string fieldName,
        string op,
        QueryDataType expectedType,
        IQueryEntity entity,
        IReadOnlyDictionary<string, QueryColumn> byName,
        List<string> errors)
    {
        if (!byName.TryGetValue(fieldName, out var col))
        {
            errors.Add($"Unknown field '{fieldName}' for entity '{entity.Name}' (WHERE).");
            return;
        }
        if (col.DataType != expectedType)
        {
            errors.Add($"Operator '{op}' (substring) requires a string field; '{col.Name}' is {col.DataType}.");
        }
    }

    private static bool IsOperatorSupported(QueryDataType type, string op) => type switch
    {
        QueryDataType.String => op is "=" or "!=" or "~",
        QueryDataType.Number => op is "=" or "!=" or "<" or "<=" or ">" or ">=",
        QueryDataType.Bool => op is "=" or "!=",
        QueryDataType.Date => op is "=" or "!=" or "<" or "<=" or ">" or ">=",
        QueryDataType.Json => op is "=" or "!=",
        _ => false
    };

    private static bool IsValueCompatible(QueryDataType type, AqlValue value) => value switch
    {
        AqlNull => true,
        // Strings are NOT compatible with date columns: AQL has no ISO-date
        // string syntax, and accepting them here was the bug that let
        // BETWEEN(StartDate, "2w ago", "now") pass validation and then
        // silently return zero rows because no row's DateTime ever equals a
        // string. Json columns still accept strings (the value is serialized
        // to a JSON literal at the SQL layer).
        AqlString => type is QueryDataType.String or QueryDataType.Json,
        AqlNumber => type is QueryDataType.Number,
        AqlBool => type is QueryDataType.Bool,
        AqlRelativeDate => type is QueryDataType.Date,
        _ => false
    };

    private static string Describe(AqlValue v) => v switch
    {
        AqlString s => $"\"{s.Value}\"",
        AqlNumber n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AqlBool b => b.Value ? "true" : "false",
        AqlNull => "null",
        AqlRelativeDate r => $"{r.Magnitude}{r.Unit}",
        _ => v.ToString() ?? "?"
    };
}
