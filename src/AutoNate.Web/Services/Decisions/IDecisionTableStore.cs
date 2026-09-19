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
    /// The version a deployed process is bound to, by the engine's decision id.
    /// </summary>
    /// <remarks>
    /// #111 resolves a business rule task's reference through this, which is what
    /// makes republishing a table unable to change what an already-deployed
    /// definition decides.
    /// </remarks>
    Task<DecisionTableVersionSummary?> GetVersionByDecisionIdAsync(
        string decisionId, CancellationToken cancellationToken = default);
}

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
