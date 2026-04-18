using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

public interface IWorkflowDraftStore
{
    Task<WorkflowDraft?> GetMostRecentAsync(CancellationToken cancellationToken = default);

    Task<WorkflowDraft> SaveAsync(WorkflowDraft draft, CancellationToken cancellationToken = default);
}
