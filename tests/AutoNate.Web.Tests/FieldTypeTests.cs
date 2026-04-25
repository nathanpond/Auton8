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

    // ===== Registry =========================================================

    [Fact]
    public void Registry_Get_ReturnsRegisteredType()
    {
        var registry = PostgresTestDatabase.BuildDefaultFieldTypeRegistry();

        var text = registry.Get("text");
        Assert.IsType<TextFieldType>(text);
    }

    [Fact]
    public void Registry_TryGet_ReturnsFalseForUnknown()
    {
        var registry = PostgresTestDatabase.BuildDefaultFieldTypeRegistry();

        var found = registry.TryGet("nope", out var ft);
        Assert.False(found);
        Assert.Null(ft);
    }

    [Fact]
    public void Registry_TryGet_ReturnsTrueForKnown()
    {
        var registry = PostgresTestDatabase.BuildDefaultFieldTypeRegistry();

        var found = registry.TryGet("number", out var ft);
        Assert.True(found);
        Assert.IsType<NumberFieldType>(ft);
    }

    [Fact]
    public void Registry_All_ContainsAllSeededTypes()
    {
        var registry = PostgresTestDatabase.BuildDefaultFieldTypeRegistry();

        var dataTypes = registry.All.Select(ft => ft.DataType).OrderBy(s => s).ToArray();
        Assert.Equal(
            new[] { "boolean", "date", "email", "number", "option", "phone", "text" },
            dataTypes);
    }

    // ===== Required + null vs undefined paths (shared shape across types) ===

    [Fact]
    public void Boolean_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new BooleanFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Boolean_OptionalAndNull_NormalizesToNull()
    {
        var ft = new BooleanFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal(JsonValueKind.Null, normalized.ValueKind);
    }

    [Fact]
    public void Email_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new EmailFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Email_NonString_FailsValidation()
    {
        var ft = new EmailFieldType();
        var result = ft.ValidateValue(Json("42"), ft.NormalizeConfig(Json("{}")), isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Email_RequiredAndEmpty_FailsValidation()
    {
        var ft = new EmailFieldType();
        var result = ft.ValidateValue(Json("\"  \""), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Phone_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new PhoneFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Phone_NonString_FailsValidation()
    {
        var ft = new PhoneFieldType();
        var result = ft.ValidateValue(Json("42"), ft.NormalizeConfig(Json("{}")), isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Phone_RequiredAndAllNonDigits_FailsValidation()
    {
        var ft = new PhoneFieldType();
        var result = ft.ValidateValue(Json("\"---\""), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Phone_OptionalAndAllNonDigits_NormalizesToEmpty()
    {
        var ft = new PhoneFieldType();
        var result = ft.ValidateValue(Json("\"---\""), ft.NormalizeConfig(Json("{}")), isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, normalized.GetString());
    }

    [Fact]
    public void Phone_NormalizeConfig_AcceptsExplicitRegion()
    {
        var ft = new PhoneFieldType();
        var config = ft.NormalizeConfig(Json("{\"region\":\"gb\"}"));
        Assert.Equal("GB", config.GetProperty("region").GetString());
    }

    [Fact]
    public void Phone_NormalizeConfig_RejectsRegionOfWrongLength()
    {
        var ft = new PhoneFieldType();
        Assert.Throws<FieldConfigException>(() =>
            ft.NormalizeConfig(Json("{\"region\":\"USAUSA\"}")));
    }

    [Fact]
    public void Text_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new TextFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Text_RequiredAndEmpty_FailsValidation()
    {
        var ft = new TextFieldType();
        var result = ft.ValidateValue(Json("\"\""), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Text_NormalizeConfig_RejectsMaxLengthOutOfRange()
    {
        var ft = new TextFieldType();
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"maxLength\":0}")));
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"maxLength\":99999999}")));
    }

    [Fact]
    public void Number_NormalizeConfig_RejectsBadVariant()
    {
        var ft = new NumberFieldType();
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"variant\":\"bogus\"}")));
    }

    [Fact]
    public void Number_NormalizeConfig_RejectsPrecisionOutOfRange()
    {
        var ft = new NumberFieldType();
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"precision\":-1}")));
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"precision\":13}")));
    }

    [Fact]
    public void Number_NormalizeConfig_IntegerVariantForcesPrecisionZero()
    {
        var ft = new NumberFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"integer\",\"precision\":4}"));
        Assert.Equal(0, config.GetProperty("precision").GetInt32());
    }

    [Fact]
    public void Number_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new NumberFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Number_NonNumeric_FailsValidation()
    {
        var ft = new NumberFieldType();
        var result = ft.ValidateValue(Json("\"42\""), ft.NormalizeConfig(Json("{}")), isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Number_DecimalWithPrecision_RoundsAwayFromZero()
    {
        var ft = new NumberFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"decimal\",\"precision\":2}"));
        var result = ft.ValidateValue(Json("1.235"), config, isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal(1.24, normalized.GetDouble(), precision: 6);
    }

    [Fact]
    public void Date_NormalizeConfig_RejectsBadVariant()
    {
        var ft = new DateFieldType();
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{\"variant\":\"bogus\"}")));
    }

    [Fact]
    public void Date_RequiredAndExplicitNull_FailsValidation()
    {
        var ft = new DateFieldType();
        var result = ft.ValidateValue(Json("null"), ft.NormalizeConfig(Json("{}")), isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Date_RangeWithNonObjectValue_FailsValidation()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"range\"}"));
        var result = ft.ValidateValue(Json("\"2026-01-01\""), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Date_NonStringValue_FailsValidation()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"date\"}"));
        var result = ft.ValidateValue(Json("42"), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Date_BadFormatString_FailsValidation()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"date\"}"));
        var result = ft.ValidateValue(Json("\"not-a-date\""), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Date_DatetimeVariant_NormalizesToIsoWithTime()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"datetime\"}"));
        var result = ft.ValidateValue(Json("\"2026-05-01T14:30:00Z\""), config, isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        var s = normalized.GetString()!;
        Assert.Contains("T", s);
        Assert.StartsWith("2026-05-01", s);
    }

    [Fact]
    public void Option_NormalizeConfig_RejectsMissingChoices()
    {
        var ft = new OptionFieldType();
        Assert.Throws<FieldConfigException>(() => ft.NormalizeConfig(Json("{}")));
    }

    [Fact]
    public void Option_NormalizeConfig_RejectsNonObjectChoice()
    {
        var ft = new OptionFieldType();
        Assert.Throws<FieldConfigException>(() =>
            ft.NormalizeConfig(Json("{\"choices\":[\"a\"]}")));
    }

    [Fact]
    public void Option_NormalizeConfig_RejectsChoiceWithoutValue()
    {
        var ft = new OptionFieldType();
        Assert.Throws<FieldConfigException>(() =>
            ft.NormalizeConfig(Json("{\"choices\":[{\"label\":\"A\"}]}")));
    }

    [Fact]
    public void Option_NormalizeConfig_RejectsEmptyChoicesArray()
    {
        var ft = new OptionFieldType();
        Assert.Throws<FieldConfigException>(() =>
            ft.NormalizeConfig(Json("{\"choices\":[]}")));
    }

    [Fact]
    public void Option_NormalizeConfig_DefaultsLabelToValue()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\"}]}"));
        var first = config.GetProperty("choices").EnumerateArray().First();
        Assert.Equal("a", first.GetProperty("label").GetString());
    }

    [Fact]
    public void Option_Single_RequiredAndNull_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("null"), config, isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Option_Single_OptionalAndNull_NormalizesToNull()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("null"), config, isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal(JsonValueKind.Null, normalized.ValueKind);
    }

    [Fact]
    public void Option_Multi_OptionalAndNull_NormalizesToEmptyArray()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("null"), config, isRequired: false, out var normalized);
        Assert.True(result.IsValid);
        Assert.Equal(0, normalized.GetArrayLength());
    }

    [Fact]
    public void Option_Multi_RequiredEmptyArray_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("[]"), config, isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Option_Multi_NonStringElement_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("[1]"), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Option_Multi_UnknownValue_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("[\"a\",\"z\"]"), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Option_Single_NonString_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("123"), config, isRequired: false, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Option_Single_RequiredEmptyString_FailsValidation()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var result = ft.ValidateValue(Json("\"\""), config, isRequired: true, out _);
        Assert.False(result.IsValid);
    }

    // ===== BuildFilter ======================================================

    [Fact]
    public void Text_BuildFilter_SupportsEqualsNotEqualsContains()
    {
        var ft = new TextFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        var eq = ft.BuildFilter("title", FilterOperator.Equals, Json("\"hi\""), config);
        Assert.Contains("=", eq.Sql);
        Assert.Equal("hi", eq.Parameters[0]);

        var neq = ft.BuildFilter("title", FilterOperator.NotEquals, Json("\"hi\""), config);
        Assert.Contains("<>", neq.Sql);

        var contains = ft.BuildFilter("title", FilterOperator.Contains, Json("\"hi\""), config);
        Assert.Contains("ILIKE", contains.Sql);
        Assert.Equal("%hi%", contains.Parameters[0]);
    }

    [Fact]
    public void Text_BuildFilter_RejectsUnsupportedOperators()
    {
        var ft = new TextFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("k", FilterOperator.GreaterThan, Json("\"x\""), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Email_BuildFilter_LowercasesOperand()
    {
        var ft = new EmailFieldType();
        var fragment = ft.BuildFilter("email", FilterOperator.Equals, Json("\"Alice@Example.com\""), ft.NormalizeConfig(Json("{}")));
        Assert.Equal("alice@example.com", fragment.Parameters[0]);
    }

    [Fact]
    public void Email_BuildFilter_ContainsWrapsOperandInPercents()
    {
        var ft = new EmailFieldType();
        var fragment = ft.BuildFilter("email", FilterOperator.Contains, Json("\"@Example\""), ft.NormalizeConfig(Json("{}")));
        Assert.Contains("ILIKE", fragment.Sql);
        Assert.Equal("%@example%", fragment.Parameters[0]);
    }

    [Fact]
    public void Email_BuildFilter_RejectsUnsupportedOperators()
    {
        var ft = new EmailFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("e", FilterOperator.LessThan, Json("\"a@b.c\""), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Phone_BuildFilter_SupportsEqualsNotEqualsContains()
    {
        var ft = new PhoneFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        Assert.Contains("=", ft.BuildFilter("p", FilterOperator.Equals, Json("\"123\""), config).Sql);
        Assert.Contains("<>", ft.BuildFilter("p", FilterOperator.NotEquals, Json("\"123\""), config).Sql);
        Assert.Contains("ILIKE", ft.BuildFilter("p", FilterOperator.Contains, Json("\"123\""), config).Sql);
    }

    [Fact]
    public void Phone_BuildFilter_RejectsUnsupportedOperators()
    {
        var ft = new PhoneFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("p", FilterOperator.GreaterThan, Json("\"1\""), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Boolean_BuildFilter_EqualsAndNotEquals()
    {
        var ft = new BooleanFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        var eq = ft.BuildFilter("b", FilterOperator.Equals, Json("true"), config);
        Assert.Equal(true, eq.Parameters[0]);

        var neq = ft.BuildFilter("b", FilterOperator.NotEquals, Json("false"), config);
        Assert.Equal(false, neq.Parameters[0]);
    }

    [Fact]
    public void Boolean_BuildFilter_RejectsNonBoolOperand()
    {
        var ft = new BooleanFieldType();
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("b", FilterOperator.Equals, Json("\"true\""), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Boolean_BuildFilter_RejectsUnsupportedOperators()
    {
        var ft = new BooleanFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("b", FilterOperator.Contains, Json("true"), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Number_BuildFilter_AllRangeOperatorsAndEquality()
    {
        var ft = new NumberFieldType();
        var config = ft.NormalizeConfig(Json("{}"));

        Assert.Contains("=", ft.BuildFilter("n", FilterOperator.Equals, Json("1"), config).Sql);
        Assert.Contains("<>", ft.BuildFilter("n", FilterOperator.NotEquals, Json("1"), config).Sql);
        Assert.Contains(">", ft.BuildFilter("n", FilterOperator.GreaterThan, Json("1"), config).Sql);
        Assert.Contains(">=", ft.BuildFilter("n", FilterOperator.GreaterThanOrEqual, Json("1"), config).Sql);
        Assert.Contains("<", ft.BuildFilter("n", FilterOperator.LessThan, Json("1"), config).Sql);
        Assert.Contains("<=", ft.BuildFilter("n", FilterOperator.LessThanOrEqual, Json("1"), config).Sql);
    }

    [Fact]
    public void Number_BuildFilter_RejectsNonNumericOperand()
    {
        var ft = new NumberFieldType();
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("n", FilterOperator.Equals, Json("\"1\""), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Number_BuildFilter_RejectsContains()
    {
        var ft = new NumberFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("n", FilterOperator.Contains, Json("1"), ft.NormalizeConfig(Json("{}"))));
    }

    [Fact]
    public void Date_BuildFilter_DatetimeVariantUsesTimestamptzCast()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"datetime\"}"));
        var fragment = ft.BuildFilter("d", FilterOperator.GreaterThan, Json("\"2026-01-01T00:00:00Z\""), config);
        Assert.Contains("timestamptz", fragment.Sql);
    }

    [Fact]
    public void Date_BuildFilter_DateVariantUsesDateCast()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"date\"}"));
        var fragment = ft.BuildFilter("d", FilterOperator.LessThanOrEqual, Json("\"2026-01-01\""), config);
        Assert.Contains("::date", fragment.Sql);
        Assert.Contains("<=", fragment.Sql);
    }

    [Fact]
    public void Date_BuildFilter_RejectsNonStringOrInvalidOperand()
    {
        var ft = new DateFieldType();
        var config = ft.NormalizeConfig(Json("{\"variant\":\"date\"}"));
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("d", FilterOperator.Equals, Json("123"), config));
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("d", FilterOperator.Equals, Json("\"not-a-date\""), config));
    }

    [Fact]
    public void Date_BuildFilter_RejectsContains()
    {
        var ft = new DateFieldType();
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("d", FilterOperator.Contains, Json("\"2026-01-01\""), ft.NormalizeConfig(Json("{\"variant\":\"date\"}"))));
    }

    [Fact]
    public void Option_Single_BuildFilter_EqualsAndNotEquals()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));

        var eq = ft.BuildFilter("k", FilterOperator.Equals, Json("\"a\""), config);
        Assert.Contains("=", eq.Sql);
        Assert.Equal("a", eq.Parameters[0]);

        var neq = ft.BuildFilter("k", FilterOperator.NotEquals, Json("\"a\""), config);
        Assert.Contains("<>", neq.Sql);
    }

    [Fact]
    public void Option_Single_BuildFilter_RejectsContains()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("k", FilterOperator.Contains, Json("\"a\""), config));
    }

    [Fact]
    public void Option_Single_BuildFilter_RejectsNonStringOperand()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("k", FilterOperator.Equals, Json("123"), config));
    }

    [Fact]
    public void Option_Multi_BuildFilter_UsesJsonbContainment()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        var fragment = ft.BuildFilter("k", FilterOperator.Contains, Json("\"a\""), config);

        Assert.Contains("@>", fragment.Sql);
        Assert.Contains("jsonb", fragment.Sql);
        Assert.Equal("[\"a\"]", fragment.Parameters[0]);
    }

    [Fact]
    public void Option_Multi_BuildFilter_RejectsNonStringOperand()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        Assert.Throws<ArgumentException>(() =>
            ft.BuildFilter("k", FilterOperator.Equals, Json("123"), config));
    }

    [Fact]
    public void Option_Multi_BuildFilter_RejectsRangeOperators()
    {
        var ft = new OptionFieldType();
        var config = ft.NormalizeConfig(Json("{\"multi\":true,\"choices\":[{\"value\":\"a\",\"label\":\"A\"}]}"));
        Assert.Throws<NotSupportedException>(() =>
            ft.BuildFilter("k", FilterOperator.GreaterThan, Json("\"a\""), config));
    }

    // ===== FilterSqlFragment ================================================

    [Fact]
    public void FilterSqlFragment_Empty_IsTrueWithNoParameters()
    {
        var empty = FilterSqlFragment.Empty;
        Assert.Equal("TRUE", empty.Sql);
        Assert.Empty(empty.Parameters);
    }
}
