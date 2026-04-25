using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

/// <summary>
/// Single- or multi-select option (dropdown / multiselect).
/// Config: { "multi": bool, "choices": [ { "value": string, "label": string } ] }
/// Value: single -> string (choice.value); multi -> string[] of choice values.
/// </summary>
public sealed class OptionFieldType : IFieldType
{
    public string DataType => FieldTypeNames.Option;

    public JsonElement NormalizeConfig(JsonElement config)
    {
        var multi = false;
        if (FieldJsonHelpers.TryGetBoolean(config, "multi", out var m))
        {
            multi = m;
        }

        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("choices", out var choicesProp) ||
            choicesProp.ValueKind != JsonValueKind.Array)
        {
            throw new FieldConfigException("option.choices must be an array of {value, label} objects.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalizedChoices = new List<object>();
        foreach (var choice in choicesProp.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                throw new FieldConfigException("option.choices entries must be objects.");
            }
            if (!FieldJsonHelpers.TryGetString(choice, "value", out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new FieldConfigException("option.choices[*].value is required.");
            }
            if (!seen.Add(value))
            {
                throw new FieldConfigException($"Duplicate option value '{value}'.");
            }
            var label = FieldJsonHelpers.TryGetString(choice, "label", out var l) && !string.IsNullOrWhiteSpace(l)
                ? l
                : value;
            normalizedChoices.Add(new { value, label });
        }

        if (normalizedChoices.Count == 0)
        {
            throw new FieldConfigException("option.choices must have at least one entry.");
        }

        return FieldJsonHelpers.Serialize(new
        {
            multi,
            choices = normalizedChoices
        });
    }

    public FieldValidationResult ValidateValue(JsonElement value, JsonElement config, bool isRequired, out JsonElement normalized)
    {
        normalized = default;

        var multi = FieldJsonHelpers.TryGetBoolean(config, "multi", out var m) && m;
        var allowed = ExtractAllowedValues(config);

        if (FieldJsonHelpers.IsUndefinedOrNull(value))
        {
            if (isRequired && value.ValueKind == JsonValueKind.Null)
            {
                return FieldValidationResult.Fail("required", "Value is required.");
            }
            normalized = multi
                ? FieldJsonHelpers.Serialize(Array.Empty<string>())
                : FieldJsonHelpers.Serialize<string?>(null);
            return FieldValidationResult.Success;
        }

        if (multi)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                return FieldValidationResult.Fail("type", "Multi-select value must be an array.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var chosen = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return FieldValidationResult.Fail("type", "Multi-select values must be strings.");
                }
                var v = item.GetString() ?? string.Empty;
                if (!allowed.Contains(v))
                {
                    return FieldValidationResult.Fail("unknown_choice", $"Value '{v}' is not an allowed choice.");
                }
                if (seen.Add(v))
                {
                    chosen.Add(v);
                }
            }

            if (isRequired && chosen.Count == 0)
            {
                return FieldValidationResult.Fail("required", "Value is required.");
            }

            normalized = FieldJsonHelpers.Serialize(chosen);
            return FieldValidationResult.Success;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Fail("type", "Option value must be a string.");
        }

        var singleValue = value.GetString() ?? string.Empty;

        if (isRequired && singleValue.Length == 0)
        {
            return FieldValidationResult.Fail("required", "Value is required.");
        }

        if (singleValue.Length > 0 && !allowed.Contains(singleValue))
        {
            return FieldValidationResult.Fail("unknown_choice", $"Value '{singleValue}' is not an allowed choice.");
        }

        normalized = FieldJsonHelpers.Serialize(singleValue);
        return FieldValidationResult.Success;
    }

    public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config)
    {
        var keySql = SqlIdentifier.EscapeSingleQuotes(fieldKey);
        var multi = FieldJsonHelpers.TryGetBoolean(config, "multi", out var m) && m;

        if (multi)
        {
            // JSONB containment: values->'key' @> '["val"]'
            if (op == FilterOperator.Contains || op == FilterOperator.Equals)
            {
                if (operand.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Multi-option filter operand must be a string.");
                }
                var val = operand.GetString() ?? string.Empty;
                return new FilterSqlFragment(
                    $"values->'{keySql}' @> {{0}}::jsonb",
                    new object?[] { $"[\"{val.Replace("\"", "\\\"")}\"]" });
            }
            throw new NotSupportedException($"Operator '{op}' is not supported for multi-option fields.");
        }

        var path = $"values->>'{keySql}'";
        if (operand.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Option filter operand must be a string.");
        }
        var singleValue = operand.GetString() ?? string.Empty;

        return op switch
        {
            FilterOperator.Equals => new FilterSqlFragment($"{path} = {{0}}", new object?[] { singleValue }),
            FilterOperator.NotEquals => new FilterSqlFragment($"{path} <> {{0}}", new object?[] { singleValue }),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported for option fields.")
        };
    }

    private static HashSet<string> ExtractAllowedValues(JsonElement config)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (FieldJsonHelpers.TryGetString(choice, "value", out var v))
                {
                    set.Add(v);
                }
            }
        }
        return set;
    }
}
