namespace AutoNate.Web.Services.Projections;

public enum ProjectionVersionStatus
{
    Active = 1,
    Shadow = 2,
    Retired = 3
}

public sealed record ProjectionVersionRecord(
    string Name,
    int Version,
    ProjectionVersionStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc);

// Bookkeeping table behind the version-bump / backfill workflow. Active row
// is what readers query; Shadow row exists during a backfill and is renamed
// in once it catches up.
public interface IProjectionVersionStore
{
    Task<ProjectionVersionRecord?> GetActiveAsync(string name, CancellationToken cancellationToken);

    Task SetActiveAsync(string name, int version, CancellationToken cancellationToken);

    Task RecordShadowStartAsync(string name, int version, CancellationToken cancellationToken);

    Task PromoteShadowAsync(string name, int version, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectionVersionRecord>> ListAsync(CancellationToken cancellationToken);
}
