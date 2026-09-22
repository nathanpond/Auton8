using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

/// <summary>
/// Deploy succeeded; recording it did not (#169). This is the window that had
/// no mechanism: the engine holds a deployment Auton8 has no row for -- a set of
/// definitions nobody can see or manage, which is the orphan #578 was filed
/// about, reached by a different door.
/// </summary>
/// <remarks>
/// <para>Shaped as delegates rather than as a class over <c>IFlowableClient</c>
/// and <c>IWorkflowModelStore</c> deliberately: the test project has no mocking
/// library, and a fake of a thirty-member interface is a place for the tested
/// behaviour to hide. Two functions is the whole contract, and the test passes
/// two lambdas.</para>
/// <para>The compensation failing is reported to the caller as a separate fact --
/// the original failure is what the caller must see, but "and the deployment is
/// still there" is the more serious of the two and must not be swallowed into
/// it.</para>
/// </remarks>
public static class WorkflowPublishCompensation
{
    public sealed record Outcome(
        WorkflowModel? Published,
        Exception? RecordFailure,
        Exception? CompensationFailure)
    {
        public bool Succeeded => Published is not null;
        public bool Compensated => RecordFailure is not null && CompensationFailure is null;
    }

    public static async Task<Outcome> RecordOrWithdrawAsync(
        WorkflowDeploymentInfo deployment,
        Func<CancellationToken, Task<WorkflowModel>> record,
        Func<string, CancellationToken, Task> deleteDeployment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(deleteDeployment);

        WorkflowModel published;
        try
        {
            published = await record(cancellationToken);
        }
        catch (Exception recordFailure) when (recordFailure is not OperationCanceledException)
        {
            try
            {
                await deleteDeployment(deployment.DeploymentId, cancellationToken);
                return new Outcome(null, recordFailure, null);
            }
            catch (Exception compensationFailure) when (compensationFailure is not OperationCanceledException)
            {
                return new Outcome(null, recordFailure, compensationFailure);
            }
        }

        return new Outcome(published, null, null);
    }
}
