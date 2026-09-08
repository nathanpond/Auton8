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

    // #114. A BUSINESS error the process itself should route on — "payment
    // declined" — as distinct from Failed above, which the author branches on with
    // a gateway, and from a thrown exception, which is an operational fault the
    // engine retries.
    //
    // Set only when the behaviour DECLARES the code in CatchableErrorCodes. The
    // bridge turns a declared code into a BPMN error the engine routes to a
    // matching error boundary event; anything undeclared stays an unhandled
    // failure. That opt-in is the whole point: if every exception became a
    // catchable BPMN error, "the database was briefly unreachable" would travel
    // down the "payment declined" branch, which is the hardest failure of all to
    // diagnose.
    //
    // An init-only PROPERTY, not a positional parameter. Adding a parameter would
    // change the primary constructor's signature, and a plugin compiled against
    // the pinned 1.0.0.0 ABI calling `new BehaviorResult(...)` would then fail at
    // run time with MissingMethodException — which is exactly what invariant 2
    // exists to prevent.
    public string? BusinessErrorCode { get; init; }

    public static BehaviorResult BusinessError(
        string errorCode,
        string message,
        IReadOnlyDictionary<string, BehaviorVariableValue>? variableUpdates = null) =>
        new(variableUpdates, Failed: true, FailureCode: errorCode, FailureMessage: message)
        {
            BusinessErrorCode = errorCode
        };
}
