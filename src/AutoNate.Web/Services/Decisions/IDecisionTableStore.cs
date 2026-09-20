using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Services.Decisions;

/// <summary>
/// Decision tables and their published versions (#110).
/// </summary>
public interface IDecisionTableStore
{
    Task<IReadOnlyList<DecisionTableModel>> ListAsync(CancellationToken cancellationToken = default);

    Task<DecisionTableModel?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DecisionTableModel?> GetByKeyAsync(string decisionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a draft.
    /// </summary>
    /// <remarks>
    /// <b>Validated here, not only at the endpoint.</b> The whole point of storing
    /// rules as structured data is that a bad cell never reaches the database, and a
    /// store that trusted its caller would leave the only enforcement in whichever
    /// call site remembered.
    /// </remarks>
    Task<DecisionTableModel> SaveAsync(DecisionTableModel table, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Records a published version and stamps the table's last-deployment columns.</summary>
    Task<DecisionTableModel> RecordPublishAsync(
        Guid id,
        string dmnXml,
        DecisionDeploymentInfo deployment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DecisionTableVersionSummary>> ListVersionsAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The published version a process publishing NOW would bind to, with the DMN
    /// that version was deployed from (#111).
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when the key names no table, or names one with nothing
    /// published — the two cases that must be refused at publish rather than
    /// discovered when an instance reaches the step.
    /// </remarks>
    Task<PublishedDecisionSnapshot?> GetPublishedSnapshotAsync(
        string decisionKey, CancellationToken cancellationToken = default);
}

/// <summary>The exact bytes a process binds to, and the version number they are (#111).</summary>
public sealed record PublishedDecisionSnapshot(
    string DecisionKey,
    int VersionNumber,
    string DmnXml);

public sealed record DecisionTableVersionSummary(
    Guid Id,
    Guid DecisionTableId,
    int VersionNumber,
    string Name,
    string DecisionKey,
    string DecisionId,
    int DecisionVersion,
    DateTimeOffset PublishedAtUtc);

/// <summary>Raised when a table is saved with cells the engine could not use.</summary>
/// <remarks>
/// Carries every error rather than the first, because an author fixing a table one
/// message at a time is the experience this story exists to avoid.
/// </remarks>
public sealed class DecisionTableInvalidException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
