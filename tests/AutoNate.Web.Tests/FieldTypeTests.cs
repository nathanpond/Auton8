using System.Text.Json;
using AutoNate.Web.Services.Records.Fields;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class FieldTypeTests
{
    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Registry_ThrowsOnUnknownType()
    {
        var registry = PostgresTestDatabase.BuildDefaultFieldTypeRegistry();

        Assert.Throws<UnknownFieldTypeException>(() => registry.Get("made_up"));
    }

    [Fact]
    public void Registry_RejectsDuplicateRegistrations()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new FieldTypeRegistry(new IFieldType[] { new TextFieldType(), new TextFieldType() }));
    }

    [Fact]
    public void Text_NormalizeConfig_FillsDefaults()
    {
        var config = new TextFieldType().NormalizeConfig(Json("{}"));

        Assert.Equal("single", config.GetProperty("variant").GetString());
        Assert.Equal(4000, config.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Text_NormalizeConfig_RejectsBadVariant()
    {
        Assert.Throws<FieldConfigException>(() =>
            new TextFieldType().NormalizeConfig(Json("{\"variant\":\"bogus\"}")));
    }

    [Fact]
    public void Text_Validate_RejectsNonString()
    {
        var ft = new TextFieldType();
        var result = ft.ValidateValue(Json("42"), ft.NormalizeConfig(Json("{}")), isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Text_Validate_EnforcesMaxLength()
    {
        var ft = new TextFieldType();
        var config = ft.NormalizeConfig(Json("{\"maxLength\":5}"));
        var result = ft.ValidateValue(Json("\"too long\""), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Number_Integer_RejectsDecimal()
    {
        var ft = new NumberFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"integer\"}"));
        var result = ft.ValidateValue(Json("3.14"), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Number_EnforcesMinMax()
    {
        var ft = new NumberFieldType();
        var config = ft.NormalizeConfig(Json("{\"min\":0,\"max\":10}"));

        var low = ft.ValidateValue(Json("-1"), config, isRequired: false, out _);
        Assert.False(low.IsValid);

        var high = ft.ValidateValue(Json("11"), config, isRequired: false, out _);
        Assert.False(high.IsValid);

        var ok = ft.ValidateValue(Json("5.5"), config, isRequired: false, out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal(5.5, normalized.GetDouble(), precision: 6);
    }

    [Fact]
    public void Number_Config_RejectsMinGreaterThanMax()
    {
        Assert.Throws<FieldConfigException>(() =>
            new NumberFieldType().NormalizeConfig(Json("{\"min\":10,\"max\":5}")));
    }

    [Fact]
    public void Date_Validate_ParsesIsoString()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"date\"}"));

        var result = ft.ValidateValue(Json("\"2026-05-01\""), config, isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal("2026-05-01", normalized.GetString());
    }

    [Fact]
    public void Date_Range_RequiresStartAndEnd()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"range\"}"));

        var missing = ft.ValidateValue(Json("{\"start\":\"2026-01-01\"}"), config, isRequired: false, out _);
        Assert.False(missing.IsValid);

        var reversed = ft.ValidateValue(
            Json("{\"start\":\"2026-05-01\",\"end\":\"2026-01-01\"}"),
            config,
            isRequired: false,
            out _);
        Assert.False(reversed.IsValid);

        var ok = ft.ValidateValue(
            Json("{\"start\":\"2026-01-01\",\"end\":\"2026-05-01\"}"),
            config,
            isRequired: false,
            out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal("2026-01-01", normalized.GetProperty("start").GetString());
        Assert.Equal("2026-05-01", normalized.GetProperty("end").GetString());
    }

    [Fact]
    public void Phone_NormalizesDigitsAndPlus()
    {
        var ft = new PhoneFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        var ok = ft.ValidateValue(Json("\"+1 (415) 555-2671\""), config, isRequired: false, out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal("+14155552671", normalized.GetString());

        var bad = ft.ValidateValue(Json("\"123\""), config, isRequired: false, out _);
        Assert.False(bad.IsValid);
    }

    [Fact]
    public void Email_LowercasesAndValidates()
    {
        var ft = new EmailFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        var ok = ft.ValidateValue(Json("\"Alice@Example.COM\""), config, isRequired: false, out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal("alice@example.com", normalized.GetString());

        var bad = ft.ValidateValue(Json("\"no-at-sign\""), config, isRequired: false, out _);
        Assert.False(bad.IsValid);
    }

    [Fact]
    public void Option_Single_RequiresKnownChoice()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json(
            "{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}"));

        var ok = ft.ValidateValue(Json("\"open\""), config, isRequired: true, out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal("open", normalized.GetString());

        var unknown = ft.ValidateValue(Json("\"pending\""), config, isRequired: true, out _);
        Assert.False(unknown.IsValid);
    }

    [Fact]
    public void Option_Multi_RequiresArrayAndDeduplicates()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json(
            "{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"b\",\"label\":\"B\"},{\"value\":\"c\",\"label\":\"C\"}]}"));

        var ok = ft.ValidateValue(Json("[\"a\",\"b\",\"a\"]"), config, isRequired: false, out var normalized);
        Assert.True(ok.IsValid);
        Assert.Equal(2, normalized.GetArrayLength());

        var notArray = ft.ValidateValue(Json("\"a\""), config, isRequired: false, out _);
        Assert.False(notArray.IsValid);
    }

    [Fact]
    public void Option_Config_RejectsDuplicateValues()
    {
        Assert.Throws<FieldConfigException>(() =>
            new OptionFieldType().NormalizeConfig(Json(
                "{\"multi\":false,\"choices\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"a\",\"label\":\"Again\"}]}")));
    }

    [Fact]
    public void Boolean_AcceptsTrueAndFalse()
    {
        var ft = new BooleanFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        var t = ft.ValidateValue(Json("true"), config, isRequired: true, out var nt);
        Assert.True(t.IsValid);
        Assert.True(nt.GetBoolean());

        var f = ft.ValidateValue(Json("false"), config, isRequired: true, out var nf);
        Assert.True(f.IsValid);
        Assert.False(nf.GetBoolean());

        var bad = ft.ValidateValue(Json("\"true\""), config, isRequired: true, out _);
        Assert.False(bad.IsValid);
    }
}
