using DataStoreModel = AutoNate.Web.Persistence.Scaffolded.DataStore;
using DataConnectorModel = AutoNate.Web.Persistence.Scaffolded.DataConnector;
using DatasetModel = AutoNate.Web.Persistence.Scaffolded.Dataset;
using SavedQueryModel = AutoNate.Web.Persistence.Scaffolded.SavedQuery;
using PipelineModel = AutoNate.Web.Persistence.Scaffolded.Pipeline;
using PipelineRunModel = AutoNate.Web.Persistence.Scaffolded.PipelineRun;

namespace AutoNate.Web.Authorization.EntityTypes;

// EntityType registrations for the Data Stores & Analytics Pipeline feature
// (docs/plans/2026-05-30-data-stores-implementation.md). Phase 1 lands
// DataStore + DataConnector. Phase 2 adds Dataset. Phase 3 promotes
// SavedQuery to the Query kind. Phase 4 adds Transformer + Analyzer.
// Phase 5 adds Pipeline + PipelineRun.
public static class AnalyticsEntityTypes
{
    public static IReadOnlyList<IEntityType> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<IEntityType>> _all = new(() =>
        new IEntityType[]
        {
            DataStore!, DataConnector!, Dataset!, Query!, Transformer!, Analyzer!,
            Pipeline!, PipelineRun!,
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

    // Refresh gates the on-demand POST /refresh on cached datasets, plus the
    // scheduled DatasetRefreshProjection's per-dataset cron tick (the
    // projection treats absence of refresh as "don't run", same as a paused
    // projection). Schedule gates editing refresh_cron. Curated-product
    // permission model: `dataset:view:<id>` is sufficient to query rows via
    // AQL `Dataset("name")` even when the underlying source isn't visible
    // to the actor (see DatasetQueryEntity).
    public static EntityTypeDefinition Dataset { get; } = new(
        kind: EntityKinds.Dataset,
        clrType: typeof(DatasetModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.List, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Refresh, Actions.Schedule
        },
        tags: Array.Empty<string>());

    // Share gates POST /shares (token issuance for anonymous URL access).
    // Edit gates the existing PATCH and DELETE on the saved query; List
    // covers the /api/saved-queries collection. The store keeps an
    // intrinsic-owner fallback so a creator never needs an explicit grant
    // on their own row — this kind only matters for non-owner access.
    public static EntityTypeDefinition Query { get; } = new(
        kind: EntityKinds.Query,
        clrType: typeof(SavedQueryModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.List, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Share
        },
        tags: Array.Empty<string>());

    // Transformers and Analyzers are catalog kinds in Phase 4 — the actual
    // implementations are DI-registered (built-ins) or plugin-contributed,
    // not user-created rows. The List action gates the /api/transformers
    // and /api/analyzers palette endpoints; Run will gate Phase 5's per-node
    // pipeline execution. No instance authorizer is needed today (the
    // catalog is global); a per-key gate joins later if/when transformer-
    // specific permissions become a real need.
    // Create/Edit/Delete exist because Phase 6 added user-authored code
    // transformers (code_transformers rows) on top of the read-only catalog.
    // Without them the authoring endpoints had no correct token to gate on and
    // fell back to Run — which made a grant meant to let someone *execute* a
    // pipeline node also let them author the sandboxed code that later runs
    // execute (#23).
    public static EntityTypeDefinition Transformer { get; } = new(
        kind: EntityKinds.Transformer,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[]
        {
            Actions.List, Actions.View, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Run, Actions.ExecuteUnsafe
        },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition Analyzer { get; } = new(
        kind: EntityKinds.Analyzer,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[]
        {
            Actions.List, Actions.View, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Run, Actions.ExecuteUnsafe
        },
        tags: Array.Empty<string>());

    // Pipeline = the DAG definition; PipelineRun = one execution. Run is
    // gated separately from Edit so an operator can hand out execution
    // rights without authoring rights. Schedule covers the schedule_cron
    // edit path; Cancel terminates an in-flight run.
    public static EntityTypeDefinition Pipeline { get; } = new(
        kind: EntityKinds.Pipeline,
        clrType: typeof(PipelineModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.List, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.Run, Actions.Schedule, Actions.Cancel
        },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition PipelineRun { get; } = new(
        kind: EntityKinds.PipelineRun,
        clrType: typeof(PipelineRunModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.List, Actions.Cancel },
        tags: Array.Empty<string>());
}
