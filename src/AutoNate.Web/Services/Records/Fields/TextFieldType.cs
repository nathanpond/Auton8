using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Single- or multi-line text.
/// Config: { "variant": "single"|"multi", "maxLength": int? }
/// </summary>
public sealed class TextFieldType : IFieldType
{
    private const int DefaultMaxLength = 4000;
    private const int HardMaxLength = 65_536;

    public string DataType => FieldTypeNames.Text;

    public JsonElement NormalizeConfig(JsonElement config)
    {
        var variant = "single";
        if (FieldJsonHelpers.TryGetString(config, "variant", out var v))
        {
            if (v != "single" && v != "multi")
            {
                throw new FieldConfigException("text.variant must be 'single' or 'multi'.");
            }
            variant = v;
        }

        int? maxLength = null;
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("maxLength", out var maxLenProp) &&
            maxLenProp.ValueKind == JsonValueKind.Number)
        {
            if (!maxLenProp.TryGetInt32(out var m) || m <= 0 || m > HardMaxLength)
            {
                throw new FieldConfigException($"text.maxLength must be between 1 and {HardMaxLength}.");
            }
            maxLength = m;
        }

        return FieldJsonHelpers.Serialize(new
        {
            variant,
            maxLength = maxLength ?? DefaultMaxLength
        });
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
            return FieldValidationResult.Fail("type", "Text value must be a string.");
        }

        var raw = value.GetString() ?? string.Empty;
        var maxLength = FieldJsonHelpers.TryGetInt32(config, "maxLength", out var m) ? m : DefaultMaxLength;

        if (raw.Length > maxLength)
        {
            return FieldValidationResult.Fail("length", $"Text exceeds maximum length of {maxLength}.");
        }

        if (isRequired && raw.Length == 0)
        {
            return FieldValidationResult.Fail("required", "Value is required.");
        }

        normalized = FieldJsonHelpers.Serialize(raw);
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
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for text fields.")
        };
    }
}

internal static class SqlIdentifier
{
    public static string EscapeSingleQuotes(string value) => value.Replace("'", "''");
}
