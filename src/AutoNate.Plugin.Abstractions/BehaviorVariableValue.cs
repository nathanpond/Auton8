using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoNate.Plugins.Abstractions;

// Discriminated wrapper for variable updates a behavior wants applied to the
// running Flowable execution. The wire `Type` field tells the Java bridge
// how to coerce the value before calling DelegateExecution.setVariable —
// Flowable infers types from the runtime instance (Long vs Integer vs
// BigDecimal matters for downstream EL/script use), so being explicit on
// the way back avoids drift.
//
// Use the static factories rather than constructing directly — they keep
// the (Type, Value) pair internally consistent.
//
// Serialized by name (default System.Text.Json emits enums as integers,
// which the Flowable bridge can't dispatch on). The bridge lowercases
// before matching, so "String" / "Long" / "BigDecimal" all line up with
// its switch cases.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BehaviorVariableType
{
    String,
    Long,
    Double,
    Bool,
    Date,
    Json,
    BigDecimal,

    // Sentinel for "remove this variable from the execution".
    Remove,
}

public sealed record BehaviorVariableValue(BehaviorVariableType Type, object? Value)
{
    public static BehaviorVariableValue String(string? value) => new(BehaviorVariableType.String, value);
    public static BehaviorVariableValue Long(long value) => new(BehaviorVariableType.Long, value);
    public static BehaviorVariableValue Double(double value) => new(BehaviorVariableType.Double, value);
    public static BehaviorVariableValue Bool(bool value) => new(BehaviorVariableType.Bool, value);
    public static BehaviorVariableValue Date(DateTimeOffset value) => new(BehaviorVariableType.Date, value);
    public static BehaviorVariableValue Json(JsonElement value) => new(BehaviorVariableType.Json, value);
    public static BehaviorVariableValue BigDecimal(string value) => new(BehaviorVariableType.BigDecimal, value);
    public static BehaviorVariableValue Remove() => new(BehaviorVariableType.Remove, null);
}
