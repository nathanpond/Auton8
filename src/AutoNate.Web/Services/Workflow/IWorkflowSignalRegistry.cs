namespace AutoNate.Web.Services.Workflow;

// Tracks which (Dapr topic, signal name) pairs are currently expected by
// published workflows. Backed by an in-memory snapshot rebuilt from the
// workflow store; refreshed when a workflow is published or deleted.
public interface IWorkflowSignalRegistry
{
    IReadOnlyCollection<string> GetSubscribedTopics();

    IReadOnlySet<string> GetSignalNamesForTopic(string topic);

    IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
