using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

public interface IWorkflowModelStore
{
    Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default);
}
