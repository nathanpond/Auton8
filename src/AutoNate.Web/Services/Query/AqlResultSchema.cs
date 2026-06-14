using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Query;

// Derives the post-projection result schema of an AQL query.
//
// AqlValidator.ValidateAsync returns a prepared.Schema that's the SOURCE
// entity's static schema (every dataset column / every record field) —
// correct for "what can this entity offer me" introspection but wrong for
// "what columns will my query's rows actually have". This helper closes
// that gap: given the parsed AST and the source schema, it walks
// query.Columns and returns the projected column list, deriving aliases
// (via AqlSelectItem.DisplayName) and aggregate result types as it goes.
//
// Used by AqlAssistSkill.parse_aql (to surface a resultColumns field
// alongside sourceColumns) and by WidgetConfigValidator (to check that
// widget column-mapping fields like savedQueryValueColumn point at columns
// the query actually produces — "avg_temp", not "temperature").
public static class AqlResultSchema
{
    public static IReadOnlyList<QueryColumn> Derive(
        AqlQuery query, IReadOnlyList<QueryColumn> sourceSchema)
    {
        // No COLUMNS clause = no projection. Every column from the source
        // entity flows through unchanged.
        if (query.Columns is null || query.Columns.Count == 0)
            return sourceSchema;

        var result = new List<QueryColumn>(query.Columns.Count);
        foreach (var item in query.Columns)
        {
            QueryDataType dataType;
            bool isAggregable;
            if (item.IsAggregate)
            {
                dataType = InferAggregateType(item.AggregateFn, item.AggregateField, sourceSchema);
                // Once aggregated, the column can't be re-aggregated by a
                // downstream layer — there isn't one in widget/chart binding.
                isAggregable = false;
            }
            else
            {
                var src = item.Field is null
                    ? null
                    : sourceSchema.FirstOrDefault(c => string.Equals(c.Name, item.Field, StringComparison.OrdinalIgnoreCase));
                dataType = src?.DataType ?? QueryDataType.String;
                isAggregable = src?.IsAggregable ?? false;
            }
            // DisplayName resolves to (Alias ?? Field ?? "AGG(field)"), which
            // matches what the executor's row stream actually emits.
            result.Add(new QueryColumn(item.DisplayName, dataType, isAggregable, IsSystem: false));
        }
        return result;
    }

    // COUNT yields a number. AVG/SUM coerce to number even on date columns
    // (the validator would have already rejected nonsensical aggregations).
    // MIN/MAX preserve the source column's type so MIN(date) is still a date
    // and MAX(name) is still a string.
    private static QueryDataType InferAggregateType(
        string? aggregateFn, string? aggregateField, IReadOnlyList<QueryColumn> sourceSchema)
    {
        var fn = aggregateFn?.ToUpperInvariant();
        if (fn == "COUNT") return QueryDataType.Number;
        if (fn is "AVG" or "SUM") return QueryDataType.Number;
        if (fn is "MIN" or "MAX")
        {
            var src = aggregateField is null
                ? null
                : sourceSchema.FirstOrDefault(c => string.Equals(c.Name, aggregateField, StringComparison.OrdinalIgnoreCase));
            return src?.DataType ?? QueryDataType.Number;
        }
        return QueryDataType.String;
    }
}
