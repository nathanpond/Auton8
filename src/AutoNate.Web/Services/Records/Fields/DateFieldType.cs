using System.Globalization;
using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Date / datetime / date-range.
/// Config: { "variant": "date"|"datetime"|"range" }
/// Values: date -> "YYYY-MM-DD"; datetime -> ISO-8601 with offset; range -> { "start": "...", "end": "..." }
/// </summary>
public sealed class DateFieldType : IFieldType
{
    public string DataType => FieldTypeNames.Date;

    public JsonElement NormalizeConfig(JsonElement config)
    {
        var variant = "date";
        if (FieldJsonHelpers.TryGetString(config, "variant", out var v))
        {
            if (v != "date" && v != "datetime" && v != "range")
            {
                throw new FieldConfigException("date.variant must be 'date', 'datetime', or 'range'.");
            }
            variant = v;
        }

        return FieldJsonHelpers.Serialize(new { variant });
    }

    public FieldValidationResult ValidateValue(JsonElement value, JsonElement config, bool isRequired, out JsonElement normalized)
    {
        normalized = default;

        if (FieldJsonHelpers.IsUndefinedOrNull(value))
        {
            if (isRequired && value.ValueKind == JsonValueKind.Null)
            {
                return FieldValidationResult.Fail("required", "Value is required.");
            }
            normalized = FieldJsonHelpers.Serialize<string?>(null);
            return FieldValidationResult.Success;
        }

        var variant = FieldJsonHelpers.TryGetString(config, "variant", out var v) ? v : "date";

        if (variant == "range")
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return FieldValidationResult.Fail("type", "Date range must be an object with start and end.");
            }

            var start = ParseIso(value, "start");
            var end = ParseIso(value, "end");
            if (start is null || end is null)
            {
                return FieldValidationResult.Fail("format", "Date range requires valid ISO-8601 start and end dates.");
            }
            if (end.Value < start.Value)
            {
                return FieldValidationResult.Fail("range", "end must be on or after start.");
            }

            normalized = FieldJsonHelpers.Serialize(new
            {
                start = FormatIso(start.Value, includeTime: false),
                end = FormatIso(end.Value, includeTime: false)
            });
            return FieldValidationResult.Success;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Fail("type", "Date value must be an ISO-8601 string.");
        }

        if (!TryParseIsoString(value.GetString() ?? string.Empty, out var parsed))
        {
            return FieldValidationResult.Fail("format", "Value must be a valid ISO-8601 date.");
        }

        normalized = FieldJsonHelpers.Serialize(FormatIso(parsed, includeTime: variant == "datetime"));
        return FieldValidationResult.Success;
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var variant = FieldJsonHelpers.TryGetString(config, "variant", out var v) ? v : "date";
        var keySql = SqlIdentifier.EscapeSingleQuotes(fieldKey);
        var path = variant == "datetime"
            ? $"(values->>'{keySql}')::timestamptz"
            : $"(values->>'{keySql}')::date";

        if (operand.ValueKind != JsonValueKind.String || !TryParseIsoString(operand.GetString() ?? string.Empty, out var parsed))
        {
            throw new ArgumentException("Date filter operand must be an ISO-8601 string.");
        }
        object operandObj = variant == "datetime" ? (object)parsed : parsed.Date;

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new[] { operandObj }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new[] { operandObj }),
            FilterOperator.GreaterThan => new FilterSqlFragment($"{path} > {{0}}", new[] { operandObj }),
            FilterOperator.GreaterThanOrEqual => new FilterSqlFragment($"{path} >= {{0}}", new[] { operandObj }),
            FilterOperator.LessThan => new FilterSqlFragment($"{path} < {{0}}", new[] { operandObj }),
            FilterOperator.LessThanOrEqual => new FilterSqlFragment($"{path} <= {{0}}", new[] { operandObj }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for date fields.")
        };
    }

    private static DateTimeOffset? ParseIso(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String &&
            TryParseIsoString(prop.GetString() ?? string.Empty, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static bool TryParseIsoString(string raw, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);

    private static string FormatIso(DateTimeOffset value, bool includeTime) =>
        includeTime
            ? value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture)
            : value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
