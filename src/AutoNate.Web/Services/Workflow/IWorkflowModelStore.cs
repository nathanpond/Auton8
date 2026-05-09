using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

public interface IWorkflowModelStore
{
    Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default);

    Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default);

    Task<WorkflowModel> PublishAsync(
        WorkflowModel model,
        WorkflowDeploymentInfo deployment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(
        Guid workflowModelId,
        CancellationToken cancellationToken = default);

    // Hard delete. Cascades to workflow_model_versions via the FK. Returns
    // the deleted row's pre-delete projection for audit purposes, or null
    // when the row doesn't exist. Does NOT touch the Flowable deployment if
    // the workflow was published — operators are expected to pause + undeploy
    // on the Flowable side first.
    Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default);
}
