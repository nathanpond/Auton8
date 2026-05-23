using System.Text.Json.Serialization;

namespace AutoNate.Web.Services.Query;

// AST nodes are deliberately POCOs that round-trip through System.Text.Json.
// A future TypeScript client-side parser can produce the same JSON shape and
// hand it to the backend pre-parsed; for v1 the only consumer is the server
// parser + a round-trip test that keeps the contract honest.

public sealed record AqlQuery(
    string Entity,
    AqlWhere? Where,
    IReadOnlyList<AqlOrderItem> OrderBy,
    IReadOnlyList<AqlSelectItem>? Columns,
    IReadOnlyList<string>? Group,
    int? Limit);

public sealed record AqlSelectItem(
    string? Field,
    string? AggregateFn,
    string? AggregateField,
    string? Alias = null)
{
    [JsonIgnore]
    public bool IsAggregate => AggregateFn is not null;

    // `AS <alias>` overrides the column header in the result. When absent,
    // fall back to the field name or a canonical aggregate-call rendering
    // (so `COUNT()` shows up as "COUNT()" rather than blank).
    public string DisplayName => Alias ?? DefaultName;

    [JsonIgnore]
    public string DefaultName => IsAggregate
        ? (AggregateField is null ? $"{AggregateFn}()" : $"{AggregateFn}({AggregateField})")
        : Field ?? "<unknown>";
}

public sealed record AqlOrderItem(AqlSelectItem Item, bool Descending);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(AqlBinary), "binary")]
[JsonDerivedType(typeof(AqlCompare), "compare")]
[JsonDerivedType(typeof(AqlFunctionCompare), "fncompare")]
[JsonDerivedType(typeof(AqlFunctionCall), "fn")]
[JsonDerivedType(typeof(AqlIn), "in")]
[JsonDerivedType(typeof(AqlBetween), "between")]
[JsonDerivedType(typeof(AqlContains), "contains")]
public abstract record AqlWhere;

public sealed record AqlBinary(string Op, AqlWhere Left, AqlWhere Right) : AqlWhere;
public sealed record AqlCompare(string Field, string Op, AqlValue Value) : AqlWhere;

// Scalar function appearing on the left side of a comparison, e.g.
// NUMNODES() > 5. The function evaluates to a value (typically number or
// date); the entity adapter decides how to compute it.
public sealed record AqlFunctionCompare(
    string FnName,
    IReadOnlyList<AqlValue> Args,
    string Op,
    AqlValue Value) : AqlWhere;

// Standalone predicate function call (e.g. USESNODE("userTask")).
public sealed record AqlFunctionCall(string Name, IReadOnlyList<AqlValue> Args) : AqlWhere;
public sealed record AqlIn(string Field, IReadOnlyList<AqlValue> Values) : AqlWhere;
public sealed record AqlBetween(string Field, AqlValue Lo, AqlValue Hi) : AqlWhere;
public sealed record AqlContains(string Field, string Substr) : AqlWhere;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(AqlString), "string")]
[JsonDerivedType(typeof(AqlNumber), "number")]
[JsonDerivedType(typeof(AqlBool), "bool")]
[JsonDerivedType(typeof(AqlNull), "null")]
[JsonDerivedType(typeof(AqlRelativeDate), "reldate")]
public abstract record AqlValue;

public sealed record AqlString(string Value) : AqlValue;
public sealed record AqlNumber(double Value) : AqlValue;
public sealed record AqlBool(bool Value) : AqlValue;
public sealed record AqlNull : AqlValue;
public sealed record AqlRelativeDate(int Magnitude, char Unit) : AqlValue
{
    // Resolve against an explicit "now" so the same query at different
    // execution times gets a consistent point-in-time anchor.
    public DateTime Resolve(DateTime nowUtc) => char.ToLowerInvariant(Unit) switch
    {
        'h' => nowUtc.AddHours(Magnitude),
        'd' => nowUtc.AddDays(Magnitude),
        'w' => nowUtc.AddDays(Magnitude * 7),
        'm' => nowUtc.AddMonths(Magnitude),
        'y' => nowUtc.AddYears(Magnitude),
        _ => throw new InvalidOperationException($"Unknown relative-date unit '{Unit}'.")
    };
}
