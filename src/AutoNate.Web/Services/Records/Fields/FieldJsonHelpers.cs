using System.Text.Json;

namespace AutoNate.Web.Services.Records.Fields;

internal static class FieldJsonHelpers
{
    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, FieldTypeJsonOptions.Default);
        return Parse(json);
    }

    public static bool IsUndefinedOrNull(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    public static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) { value = true; return true; }
            if (prop.ValueKind == JsonValueKind.False) { value = false; return true; }
        }

        value = false;
        return false;
    }

    public static bool TryGetNumber(JsonElement element, string propertyName, out double value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number)
        {
            value = prop.GetDouble();
            return true;
        }

        value = 0;
        return false;
    }

    public static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out var intValue))
        {
            value = intValue;
            return true;
        }

        value = 0;
        return false;
    }
}

internal static class FieldTypeJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
