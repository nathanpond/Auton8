using System.Text;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records.Fields;

namespace AutoNate.Web.Services.Records;

/// <summary>
/// Composes a list of <see cref="RecordFilterClause"/> against a record type's
/// field definitions into a single parameterized SQL fragment. Each clause's
/// <see cref="FilterSqlFragment"/> uses <c>{0}</c>-style placeholders; this
/// compiler renumbers them so the combined fragment fits a single
/// <c>FormattableString</c>-style parameter list.
/// </summary>
internal sealed class RecordFilterCompiler
{
    private readonly IFieldTypeRegistry _registry;
    private readonly IReadOnlyDictionary<string, RecordTypeField> _fieldsByKey;

    public RecordFilterCompiler(IFieldTypeRegistry registry, IEnumerable<RecordTypeField> fields)
    {
        _registry = registry;
        _fieldsByKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);
    }

    /// <summary>
    /// Combines clauses with AND. Returns null Sql when no clauses produced
    /// fragments. Parameters are offset by <paramref name="parameterOffset"/>
    /// so they can be appended after caller-supplied parameters (e.g. the
    /// record_type_id filter).
    /// </summary>
    public (string? Sql, IReadOnlyList<object?> Parameters) Compile(
        IEnumerable<RecordFilterClause> clauses,
        int parameterOffset)
    {
        var combined = new StringBuilder();
        var parameters = new List<object?>();
        var nextIndex = parameterOffset;

        foreach (var clause in clauses)
        {
            if (!_fieldsByKey.TryGetValue(clause.FieldKey, out var field))
            {
                throw new RecordValidationException($"Unknown field '{clause.FieldKey}' for filter.");
            }
            if (!_registry.TryGet(field.DataType, out var fieldType))
            {
                throw new RecordValidationException($"Unknown data type '{field.DataType}' for filter.");
            }

            FilterSqlFragment fragment;
            try
            {
                fragment = fieldType.BuildFilter(clause.FieldKey, clause.Operator, clause.Value, field.Config);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new RecordValidationException($"Invalid filter for '{clause.FieldKey}': {ex.Message}");
            }

            var fragmentSql = RewritePlaceholders(fragment.Sql, nextIndex);
            if (combined.Length > 0)
            {
                combined.Append(" AND ");
            }
            combined.Append('(').Append(fragmentSql).Append(')');
            parameters.AddRange(fragment.Parameters);
            nextIndex += fragment.Parameters.Count;
        }

        return combined.Length == 0
            ? (null, Array.Empty<object?>())
            : (combined.ToString(), parameters);
    }

    private static string RewritePlaceholders(string sql, int offset)
    {
        if (offset == 0)
        {
            return sql;
        }
        var output = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '{')
            {
                var close = sql.IndexOf('}', i);
                if (close > i)
                {
                    var token = sql.Substring(i + 1, close - i - 1);
                    if (int.TryParse(token, out var index))
                    {
                        output.Append('{').Append(index + offset).Append('}');
                        i = close;
                        continue;
                    }
                }
            }
            output.Append(sql[i]);
        }
        return output.ToString();
    }
}
