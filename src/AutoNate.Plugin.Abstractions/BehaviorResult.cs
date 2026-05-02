namespace AutoNate.Plugins.Abstractions;

// What a behavior returns to the Flowable bridge. `VariableUpdates` are
// applied to the running execution before control returns to Flowable.
//
// `Failed` is for *predictable* failures (e.g. "userNotFound") that the
// workflow author should handle via an exclusive gateway on a result
// variable — when set, the bridge does NOT throw, so the engine continues.
// For unexpected failures (DB errors, etc.) implementations should let
// the exception propagate; the endpoint surfaces a 500 and the bridge
// throws, hitting Flowable's job retry pipeline.
public sealed record BehaviorResult(
    IReadOnlyDictionary<string, BehaviorVariableValue>? VariableUpdates = null,
    bool Failed = false,
    string? FailureCode = null,
    string? FailureMessage = null)
{
    public static BehaviorResult Ok(IReadOnlyDictionary<string, BehaviorVariableValue>? variableUpdates = null) =>
        new(variableUpdates, Failed: false);

    public static BehaviorResult Fail(
        string failureCode,
        string failureMessage,
        IReadOnlyDictionary<string, BehaviorVariableValue>? variableUpdates = null) =>
        new(variableUpdates, Failed: true, FailureCode: failureCode, FailureMessage: failureMessage);
}
