namespace AutoNate.Web.Persistence;

// Runs the primary AutoNate DB schema migration via the existing static
// `DatabaseSchemaInitializer`. The static is kept intentionally — it owns
// 3400+ lines of SQL that aren't worth touching during the abstraction
// extraction. This shim is what lets Program.cs iterate IDatabaseInitializer
// implementations uniformly (primary + datastores + future cluster DBs).
internal sealed class PrimaryDatabaseInitializer(IServiceProvider services) : IDatabaseInitializer
{
    public int Order => 0;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => DatabaseSchemaInitializer.EnsureAsync(services, cancellationToken);
}
