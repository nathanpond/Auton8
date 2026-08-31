using AutoNate.Web.Models.Forms;

namespace AutoNate.Web.Services.Forms;

public interface IFormStore
{
    Task<IReadOnlyList<FormSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<Form?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Form?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    Task<Form> CreateAsync(CreateFormRequest request, Guid actorId, CancellationToken cancellationToken = default);

    Task<Form> SaveAsync(Guid id, SaveFormRequest request, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Form> PublishAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormVersion>> ListVersionsAsync(Guid formId, CancellationToken cancellationToken = default);


    Task<Form?> RestoreAsync(Guid id, int versionNumber, Guid actorId, CancellationToken cancellationToken = default);

    Task<FormDraftSnapshot?> GetDraftSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    Task<FormPublishedSnapshot?> GetPublishedSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    // Returns the form_code an internal consumer (e.g. a workflow user task)
    // should render: the currently-published version if one exists,
    // otherwise the current draft. site_available is intentionally ignored
    // here — that flag gates only the public /form/{shortCode} surface.
    Task<FormWorkflowSnapshot?> GetWorkflowSnapshotByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
}
