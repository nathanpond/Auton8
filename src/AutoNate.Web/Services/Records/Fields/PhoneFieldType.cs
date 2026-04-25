using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Phone number field. Stores the digits-only form, prefixed by '+' when a
/// country code is present. Non-digits (spaces, dashes, parens) are stripped.
/// Config: { "region": "US"|... }  (currently informational; reserved for future
/// region-aware validation)
/// </summary>
public sealed class PhoneFieldType : IFieldType
{
    private static readonly Regex DigitsOnly = new(@"\D", RegexOptions.Compiled);

    public string DataType => FieldTypeNames.Phone;

    public JsonElement NormalizeConfig(JsonElement config)
    {
        var region = "US";
        if (FieldJsonHelpers.TryGetString(config, "region", out var r) && !string.IsNullOrWhiteSpace(r))
        {
            region = r.ToUpperInvariant();
            if (region.Length < 2 || region.Length > 3)
            {
                throw new FieldConfigException("phone.region must be a 2-3 letter country code.");
            }
        }

        return FieldJsonHelpers.Serialize(new { region });
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

        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Fail("type", "Phone value must be a string.");
        }

        var raw = (value.GetString() ?? string.Empty).Trim();
        var hasPlus = raw.StartsWith('+');
        var digits = DigitsOnly.Replace(raw, string.Empty);

        if (isRequired && digits.Length == 0)
        {
            return FieldValidationResult.Fail("required", "Value is required.");
        }

        if (digits.Length == 0)
        {
            normalized = FieldJsonHelpers.Serialize(string.Empty);
            return FieldValidationResult.Success;
        }

        if (digits.Length < 7 || digits.Length > 15)
        {
            return FieldValidationResult.Fail("format", "Phone number must be 7-15 digits.");
        }

        normalized = FieldJsonHelpers.Serialize(hasPlus ? "+" + digits : digits);
        return FieldValidationResult.Success;
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var path = $"values->>'{SqlIdentifier.EscapeSingleQuotes(fieldKey)}'";
        var operandValue = operand.ValueKind == JsonValueKind.String ? operand.GetString() ?? string.Empty : operand.ToString();

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new object?[] { operandValue }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new object?[] { operandValue }),
            FilterOperator.Contains => new FilterSqlFragment($"{path} ILIKE {{0}}", new object?[] { $"%{operandValue}%" }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for phone fields.")
        };
    }
}
