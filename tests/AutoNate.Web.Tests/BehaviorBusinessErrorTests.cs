using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A behaviour's business error is catchable only if the behaviour declared it (#114).
/// </summary>
/// <remarks>
/// The asymmetry is the feature, and it was decided in planning rather than left
/// open: if every failure became a catchable BPMN error, "the database was
/// briefly unreachable" would travel down the "payment declined" branch — the
/// hardest failure of all to diagnose. So the declared case and the undeclared
/// case are both asserted; testing only the first would pass against an
/// implementation that routes everything.
/// </remarks>
public sealed class BehaviorBusinessErrorTests
{
    [Fact]
    public void A_declared_business_error_survives_and_stays_routable()
    {
        var result = BehaviorResult.BusinessError("PAYMENT_DECLINED", "card declined");

        var enforced = WorkflowBehaviorEndpoints.EnforceDeclaredBusinessError(
            result, new StubBehavior("PAYMENT_DECLINED"), "test", NullLogger.Instance);

        Assert.Equal("PAYMENT_DECLINED", enforced.BusinessErrorCode);
    }

    [Fact]
    public void An_undeclared_business_error_is_stripped_and_stays_an_ordinary_failure()
    {
        var result = BehaviorResult.BusinessError("DB_UNREACHABLE", "timeout");

        var enforced = WorkflowBehaviorEndpoints.EnforceDeclaredBusinessError(
            result, new StubBehavior("PAYMENT_DECLINED"), "test", NullLogger.Instance);

        // Not routable...
        Assert.Null(enforced.BusinessErrorCode);

        // ...but still a failure, with its code and message intact. Stripping the
        // routing must not also swallow the diagnosis — the point is that it stays
        // visible and retryable, not that it disappears.
        Assert.True(enforced.Failed);
        Assert.Equal("DB_UNREACHABLE", enforced.FailureCode);
        Assert.Equal("timeout", enforced.FailureMessage);
    }

    [Fact]
    public void A_behaviour_that_declares_nothing_can_raise_no_catchable_error()
    {
        // The default from IWorkflowBehavior. A plugin built against the pinned
        // 1.0.0.0 ABI implements no CatchableErrorCodes at all, and must not
        // thereby gain the ability to route arbitrary failures.
        var result = BehaviorResult.BusinessError("ANYTHING", "boom");

        var enforced = WorkflowBehaviorEndpoints.EnforceDeclaredBusinessError(
            result, new LegacyBehavior(), "legacy", NullLogger.Instance);

        Assert.Null(enforced.BusinessErrorCode);
    }

    [Fact]
    public void An_ordinary_failure_is_untouched()
    {
        var result = BehaviorResult.Fail("userNotFound", "no such user");

        var enforced = WorkflowBehaviorEndpoints.EnforceDeclaredBusinessError(
            result, new StubBehavior("PAYMENT_DECLINED"), "test", NullLogger.Instance);

        Assert.Null(enforced.BusinessErrorCode);
        Assert.Equal("userNotFound", enforced.FailureCode);
    }

    private sealed class StubBehavior(params string[] codes) : IWorkflowBehavior
    {
        public string Key => "test.behavior";
        public string DisplayName => "Test";
        public string? Description => null;
        public IReadOnlyCollection<string> CatchableErrorCodes => codes;
        public Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // Declares no codes — exactly what an existing plugin compiled before #114
    // looks like, since CatchableErrorCodes has a default implementation.
    private sealed class LegacyBehavior : IWorkflowBehavior
    {
        public string Key => "legacy.behavior";
        public string DisplayName => "Legacy";
        public string? Description => null;
        public Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
