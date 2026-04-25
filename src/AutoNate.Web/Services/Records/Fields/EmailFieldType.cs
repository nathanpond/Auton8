using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Email address.
/// Config: {} (no per-field config today)
/// </summary>
public sealed class EmailFieldType : IFieldType
{
    // Pragmatic email regex. Matches the common subset; not RFC 5322 compliant,
    // which is fine — we treat the server as the last word on format.
    private static readonly Regex Email = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string DataType => FieldTypeNames.Email;

    public JsonElement NormalizeConfig(JsonElement config) => FieldJsonHelpers.Serialize(new { });

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

        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Fail("type", "Email value must be a string.");
        }

        var raw = (value.GetString() ?? string.Empty).Trim().ToLowerInvariant();

        if (isRequired && raw.Length == 0)
        {
            return FieldValidationResult.Fail("required", "Value is required.");
        }

        if (raw.Length > 0 && !Email.IsMatch(raw))
        {
            return FieldValidationResult.Fail("format", "Value must be a valid email address.");
        }

        normalized = FieldJsonHelpers.Serialize(raw);
        return FieldValidationResult.Success;
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var path = $"values->>'{SqlIdentifier.EscapeSingleQuotes(fieldKey)}'";
        var operandValue = (operand.ValueKind == JsonValueKind.String ? operand.GetString() : operand.ToString()) ?? string.Empty;
        operandValue = operandValue.ToLowerInvariant();

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new object?[] { operandValue }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new object?[] { operandValue }),
            FilterOperator.Contains => new FilterSqlFragment($"{path} ILIKE {{0}}", new object?[] { $"%{operandValue}%" }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for email fields.")
        };
    }
}
