namespace AutoNate.Web.Services.Projections;

// Snapshot of every projection registered at application start. The worker
// reads this to spin up one (projection, feed) loop per registration. Tests
// can resolve it and trigger a single ApplyAsync directly without the worker.
public interface IProjectionRegistry
{
    IReadOnlyList<IProjection> Projections { get; }

    IProjection? TryGet(string name);
}
