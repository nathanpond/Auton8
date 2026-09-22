using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Deploy succeeded, record failed: the deployment is withdrawn (#169).
/// </summary>
/// <remarks>
/// This is the atomicity claim that a "deliberately invalid second pool" cannot
/// make. Flowable already refuses a whole file at parse, so a bad pool never
/// deploys anything and a test built on one passes against a loop that deploys
/// one pool at a time. The window that is actually open is deploy-succeeds then
/// record-fails, and it is only closable from this side.
/// </remarks>
public sealed class WorkflowPublishCompensationTests
{
    private static readonly WorkflowDeploymentInfo Deployment = new()
    {
        DeploymentId = "dep-42",
        ProcessDefinitionId = "order:1:dep-42",
        ProcessDefinitionKey = "order",
        ProcessDefinitionVersion = 1,
        DeployedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task When_recording_fails_the_deployment_is_deleted_and_the_failure_is_the_records()
    {
        var deleted = new List<string>();
        var failure = new InvalidOperationException("the database said no");

        var outcome = await WorkflowPublishCompensation.RecordOrWithdrawAsync(
            Deployment,
            _ => throw failure,
            (id, _) => { deleted.Add(id); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.Compensated);
        Assert.Same(failure, outcome.RecordFailure);
        Assert.Null(outcome.CompensationFailure);
        // THE deployment, by id -- not "a" delete.
        Assert.Equal(["dep-42"], deleted);
    }

    /// <summary>The complement: a record that succeeds withdraws nothing.</summary>
    [Fact]
    public async Task When_recording_succeeds_nothing_is_deleted()
    {
        var deleted = new List<string>();
        var model = new WorkflowModel { Id = Guid.NewGuid(), Name = "Order", ProcessKey = "order" };

        var outcome = await WorkflowPublishCompensation.RecordOrWithdrawAsync(
            Deployment,
            _ => Task.FromResult(model),
            (id, _) => { deleted.Add(id); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Same(model, outcome.Published);
        Assert.Empty(deleted);
    }

    /// <summary>
    /// Both failing is reported as BOTH, because "the engine still holds an
    /// orphan" is the more serious fact and must not vanish into the first error.
    /// </summary>
    [Fact]
    public async Task When_the_withdrawal_also_fails_both_failures_are_reported()
    {
        var recordFailure = new InvalidOperationException("record failed");
        var deleteFailure = new HttpRequestException("engine unreachable");

        var outcome = await WorkflowPublishCompensation.RecordOrWithdrawAsync(
            Deployment,
            _ => throw recordFailure,
            (_, _) => throw deleteFailure,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Compensated);
        Assert.Same(recordFailure, outcome.RecordFailure);
        Assert.Same(deleteFailure, outcome.CompensationFailure);
    }

    /// <summary>Cancellation is not a failure to compensate; it propagates.</summary>
    [Fact]
    public async Task Cancellation_propagates_rather_than_withdrawing()
    {
        var deleted = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            WorkflowPublishCompensation.RecordOrWithdrawAsync(
                Deployment,
                _ => throw new OperationCanceledException(),
                (id, _) => { deleted.Add(id); return Task.CompletedTask; },
                CancellationToken.None));

        Assert.Empty(deleted);
    }
}
