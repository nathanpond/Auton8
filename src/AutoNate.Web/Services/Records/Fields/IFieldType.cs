using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

public interface IFieldType
{
    /// <summary>
    /// The data_type discriminator as it appears in record_type_fields.data_type
    /// (e.g. "text", "number"). See <see cref="FieldTypeNames"/>.
    /// </summary>
    string DataType { get; }

    /// <summary>
    /// Validates and normalizes the config JSON for this field type. The returned
    /// element should be written back to record_type_fields.config (e.g. after
    /// applying defaults). Throws <see cref="FieldConfigException"/> on invalid input.
    /// </summary>
    JsonElement NormalizeConfig(JsonElement config);

    /// <summary>
    /// Validates and normalizes a user-supplied value for storage in
    /// records.values. The returned element is what will be merged into the
    /// record's JSONB. For missing keys callers should skip the field entirely
    /// rather than passing JsonValueKind.Undefined — use null to explicitly clear.
    /// </summary>
    FieldValidationResult ValidateValue(JsonElement value, JsonElement config, bool isRequired, out JsonElement normalized);

    /// <summary>
    /// Builds a parameterized SQL fragment that filters against
    /// records.values-&gt;'fieldKey' for the given operator.
    /// </summary>
    FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config);
}

public sealed class FieldConfigException : Exception
{
    public FieldConfigException(string message) : base(message) { }
}
