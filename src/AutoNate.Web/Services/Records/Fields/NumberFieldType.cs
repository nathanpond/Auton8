using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Numeric field.
/// Config: { "variant": "integer"|"decimal", "precision": int?, "min": number?, "max": number? }
/// </summary>
public sealed class NumberFieldType : IFieldType
{
    public string DataType => FieldTypeNames.Number;

    public JsonElement NormalizeConfig(JsonElement config)
    {
        var variant = "decimal";
        if (FieldJsonHelpers.TryGetString(config, "variant", out var v))
        {
            if (v != "integer" && v != "decimal")
            {
                throw new FieldConfigException("number.variant must be 'integer' or 'decimal'.");
            }
            variant = v;
        }

        int precision = 2;
        if (FieldJsonHelpers.TryGetInt32(config, "precision", out var p))
        {
            if (p < 0 || p > 12)
            {
                throw new FieldConfigException("number.precision must be between 0 and 12.");
            }
            precision = p;
        }

        double? min = null;
        double? max = null;
        if (config.ValueKind == JsonValueKind.Object)
        {
            if (config.TryGetProperty("min", out var minProp) && minProp.ValueKind == JsonValueKind.Number)
            {
                min = minProp.GetDouble();
            }
            if (config.TryGetProperty("max", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number)
            {
                max = maxProp.GetDouble();
            }
        }

        if (min is not null && max is not null && min > max)
        {
            throw new FieldConfigException("number.min must be less than or equal to number.max.");
        }

        return FieldJsonHelpers.Serialize(new
        {
            variant,
            precision = variant == "integer" ? 0 : precision,
            min,
            max
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
            normalized = FieldJsonHelpers.Serialize<double?>(null);
            return FieldValidationResult.Success;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            return FieldValidationResult.Fail("type", "Number value must be numeric.");
        }

        var variant = FieldJsonHelpers.TryGetString(config, "variant", out var v) ? v : "decimal";
        if (variant == "integer" && !value.TryGetInt64(out _))
        {
            return FieldValidationResult.Fail("integer", "Value must be a whole number.");
        }

        var num = value.GetDouble();

        if (FieldJsonHelpers.TryGetNumber(config, "min", out var min) && num < min)
        {
            return FieldValidationResult.Fail("min", $"Value must be at least {min}.");
        }

        if (FieldJsonHelpers.TryGetNumber(config, "max", out var max) && num > max)
        {
            return FieldValidationResult.Fail("max", $"Value must be at most {max}.");
        }

        if (variant == "decimal" && FieldJsonHelpers.TryGetInt32(config, "precision", out var precision) && precision > 0)
        {
            num = Math.Round(num, precision, MidpointRounding.AwayFromZero);
        }

        normalized = variant == "integer"
            ? FieldJsonHelpers.Serialize((long)num)
            : FieldJsonHelpers.Serialize(num);
        return FieldValidationResult.Success;
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var path = $"(values->>'{SqlIdentifier.EscapeSingleQuotes(fieldKey)}')::numeric";
        if (operand.ValueKind != JsonValueKind.Number)
        {
            throw new ArgumentException("Number filter operand must be a number.");
        }
        var val = operand.GetDouble();

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new object?[] { val }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new object?[] { val }),
            FilterOperator.GreaterThan => new FilterSqlFragment($"{path} > {{0}}", new object?[] { val }),
            FilterOperator.GreaterThanOrEqual => new FilterSqlFragment($"{path} >= {{0}}", new object?[] { val }),
            FilterOperator.LessThan => new FilterSqlFragment($"{path} < {{0}}", new object?[] { val }),
            FilterOperator.LessThanOrEqual => new FilterSqlFragment($"{path} <= {{0}}", new object?[] { val }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for number fields.")
        };
    }
}
