using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

public interface IWorkflowModelStore
{
    Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Published workflows, each carrying the XML that was actually PUBLISHED (#544).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="ListAsync"/> plus a filter, and the difference is the half
    /// that bit. <c>ListAsync</c> returns every row including never-published
    /// drafts, and each row's <c>BpmnXml</c> is the WORKING copy — which diverges
    /// from what the engine deployed the moment somebody edits the draft of a
    /// published workflow.
    /// </para>
    /// <para>
    /// A caller asking "what do published workflows declare" wants neither of
    /// those. `workflow_model_versions` already holds the per-version XML and
    /// `PublishedVersionNumber` points at the right row, so this reads what was
    /// published rather than what is being drafted.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<WorkflowModel>> ListPublishedAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default);

    Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The PUBLISHED definition for a process key, or null (#553).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetByProcessKeyAsync"/> filters nothing and returns the
    /// working copy, which is right for the studio and wrong for anything
    /// reasoning about a RUNNING instance: Flowable is executing what was
    /// published, so a caller that reads the draft is answering questions about
    /// a diagram the engine has never seen.
    /// </para>
    /// <para>
    /// Same shape and same reason as <see cref="ListPublishedAsync"/>. A caller
    /// cannot get this by filtering the other method's result — by then the row
    /// already carries the draft's xml.
    /// </para>
    /// </remarks>
    Task<WorkflowModel?> GetPublishedByProcessKeyAsync(
        string processKey, CancellationToken cancellationToken = default);

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
