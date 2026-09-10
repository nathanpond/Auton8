using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Workflow.Behaviors;

// #114 / #223. A worked example of the business-error contract, and the only way
// to exercise it end to end.
//
// It raises a DECLARED business error every time, which the bridge turns into a
// BPMN error an error boundary event can catch. That is the contract described
// for behaviour authors in the plugin-creator skill, and this is what it looks
// like implemented.
//
// Registered only in Development. It is a diagnostic, not a product feature: a
// behaviour that always fails has no place in a production catalogue, and an
// author picking it from the studio's list by mistake would be a poor joke.
public sealed class AlwaysDeclinesBehavior : IWorkflowBehavior
{
    public const string BehaviorKey = "autonate.always-declines";
    public const string ErrorCode = "PAYMENT_DECLINED";

    public string Key => BehaviorKey;

    public string DisplayName => "Always Declines (diagnostic)";

    public string? Description =>
        "Development only. Always raises the declared business error " +
        $"'{ErrorCode}', so an error boundary event carrying that code can be " +
        "exercised end to end.";

    // The declaration is what makes the error catchable. Without this line the
    // host strips the code and the process simply carries on down the task's
    // normal outgoing flow — not caught, and not retried either (#251) — which
    // is the asymmetry #114 exists to enforce.
    public IReadOnlyCollection<string> CatchableErrorCodes => [ErrorCode];

    public Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken) =>
        Task.FromResult(BehaviorResult.BusinessError(ErrorCode, "The card was declined."));
}

// #114 / #223. The other half of the asymmetry, and the only end-to-end exercise
// of EnforceDeclaredBusinessError.
//
// This behaviour returns the SAME error code as AlwaysDeclinesBehavior but never
// declares it. The host must strip the code, and the process then continues down
// its normal outgoing flow — an error boundary event carrying that very code does
// not catch it, and nothing retries it (#251).
//
// Without this, "undeclared errors are not catchable" is only demonstrated by an
// unknown behaviour key, which 404s before any of that logic runs — a test that
// would still pass with the declaration check deleted.
public sealed class UndeclaredErrorBehavior : IWorkflowBehavior
{
    public const string BehaviorKey = "autonate.always-fails-undeclared";

    public string Key => BehaviorKey;

    public string DisplayName => "Always Fails, Undeclared (diagnostic)";

    public string? Description =>
        "Development only. Returns the business error code " +
        $"'{AlwaysDeclinesBehavior.ErrorCode}' WITHOUT declaring it, so the code " +
        "is stripped and the failure stays unhandled.";

    // Deliberately empty. This is the whole point of the behaviour.
    // CatchableErrorCodes defaults to empty, but stating it here stops a future
    // reader from "fixing" the omission.
    public IReadOnlyCollection<string> CatchableErrorCodes => [];

    public Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken) =>
        Task.FromResult(BehaviorResult.BusinessError(
            AlwaysDeclinesBehavior.ErrorCode, "Undeclared - must not be catchable."));
}
