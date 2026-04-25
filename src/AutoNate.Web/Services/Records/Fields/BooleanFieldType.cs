using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// True/false.
/// Config: {}
/// </summary>
public sealed class BooleanFieldType : IFieldType
{
    public string DataType => FieldTypeNames.Boolean;

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
            normalized = FieldJsonHelpers.Serialize<bool?>(null);
            return FieldValidationResult.Success;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            normalized = FieldJsonHelpers.Serialize(true);
            return FieldValidationResult.Success;
        }
        if (value.ValueKind == JsonValueKind.False)
        {
            normalized = FieldJsonHelpers.Serialize(false);
            return FieldValidationResult.Success;
        }

        return FieldValidationResult.Fail("type", "Boolean value must be true or false.");
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var path = $"(values->>'{SqlIdentifier.EscapeSingleQuotes(fieldKey)}')::boolean";
        if (operand.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new ArgumentException("Boolean filter operand must be true or false.");
        }
        var val = operand.ValueKind == JsonValueKind.True;

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new object?[] { val }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new object?[] { val }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for boolean fields.")
        };
    }
}
