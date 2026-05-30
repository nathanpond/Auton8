using DataStoreModel = AutoNate.Web.Persistence.Scaffolded.DataStore;
using DataConnectorModel = AutoNate.Web.Persistence.Scaffolded.DataConnector;

namespace AutoNate.Web.Authorization.EntityTypes;

// EntityType registrations for the Data Stores & Analytics Pipeline feature
// (docs/plans/2026-05-30-data-stores-implementation.md). Phase 1 lands
// DataStore + DataConnector. The Dataset, Transformer, Analyzer, Pipeline,
// and PipelineRun kinds join this list as their phases ship.
public static class AnalyticsEntityTypes
{
    public static IReadOnlyList<IEntityType> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<IEntityType>> _all = new(() =>
        new IEntityType[]
        {
            DataStore!, DataConnector!,
        });

    // Refresh is intentionally not on DataStore — refresh is a Dataset
    // concept (cached datasets pulling from sources). DataStores don't
    // refresh; their contents are uploaded or written by ingest jobs.
    public static EntityTypeDefinition DataStore { get; } = new(
        kind: EntityKinds.DataStore,
        clrType: typeof(DataStoreModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.List, Actions.Create, Actions.Edit, Actions.Delete
        },
        // No tag vocabulary in v1; PathOnlySelectorCompiler covers
        // `/datastore/<id>` and `/datastore/*` grants. Tags like `kind` or
        // `owner` could be added later if admin grants need to scope by
        // file-vs-sql or by ownership without enumerating ids.
        tags: Array.Empty<string>());

    // Connect gates POST /test (probe the configured endpoint without
    // writing data). Refresh gates POST /refresh (manual on-demand fetch).
    public static EntityTypeDefinition DataConnector { get; } = new(
        kind: EntityKinds.DataConnector,
        clrType: typeof(DataConnectorModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.List, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Connect, Actions.Refresh
        },
        tags: Array.Empty<string>());
}
