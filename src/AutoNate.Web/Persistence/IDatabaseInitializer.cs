namespace AutoNate.Web.Persistence;

// Bootstrap-time SQL schema/seed runner. The primary AutoNate DB has an
// implementation that runs `DatabaseSchemaInitializer.EnsureAsync`; the
// Data Stores feature adds a second one against the `autonate_datastores`
// cluster DB (docs/plans/2026-05-30-data-stores-implementation.md). Each
// implementation is registered as an `IDatabaseInitializer` in DI and run
// in `Order` ascending by `Program.cs` before the rest of the host stands
// up. Order < 0 reserved for cluster-level bootstrap (e.g. CREATE DATABASE);
// 0 = primary AutoNate DB; 10 = secondary DBs.
public interface IDatabaseInitializer
{
    int Order { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
