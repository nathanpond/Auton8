---
name: add-projection
description: Use when adding a new projection-framework cache — materializing an external API or expensive aggregate into a Postgres cache table that AQL can query. Walks through schema + EF entity + projection + change feed + AQL entity + selector compiler + DI wiring + tests so the cache is operable end-to-end (admin pause/resume/rebuild, health metrics, retention). For plugin-contributed scheduled jobs use the lighter-weight `IPluginContext.Projections.RegisterScheduled` path instead — that doesn't need this skill.
---

# Adding a projection

A projection caches an external system's data (Flowable, Zendesk, etc.) or an
expensive internal aggregate (record-activity rollups, dashboard pre-comps)
into a Postgres table that AQL can query directly. The substrate handles
batching, retries, pause/resume, health metrics, and admin endpoints — your
code contributes the *what* and the *where-from*.

Full framework docs: `docs/projection-framework/`. This skill is the
condensed checklist; consult the docs (especially
`recipe-add-a-projection.md` and `examples.md`) when the steps below feel
under-specified.

## When to invoke this

- Adding a new cache table populated from an external API on a schedule.
- Adding a new internal aggregate that's too expensive to compute per request.
- Adding a new AQL entity backed by a cache table you already have.
- Anything where the user says "cache X so AQL can query it" or
  "materialize Y so dashboards don't keep re-aggregating".

## When NOT to invoke this

- Plugin-contributed periodic jobs — use `IPluginContext.Projections.RegisterScheduled`.
  See `docs/projection-framework/recipe-plugin-projection.md`.
- One-off pre-computation that fires on user action (compute it in the
  endpoint handler, no projection needed).
- Cross-instance signaling, real-time pub/sub fanout — use Dapr / NATS
  directly. Projections are for *materialization*.

## Decision: copy which example?

The shipped projections cover most patterns. Pick the closest fit and copy
its skeleton rather than building from scratch.

| You need… | Copy from |
|---|---|
| External API + per-row upsert + own permission tags | `FlowableExecutionProjection` (+ `FlowableExecutionPollingFeed`, `WorkflowExecutionsQueryEntity`, `WorkflowExecutionCacheSelectorCompiler`) |
| Per-parent-instance snapshot, child rows | `FlowableVariableProjection` (+ `FlowableVariablePollingFeed`, `WorkflowVariablesQueryEntity`) — uses parent-auth inheritance |
| Append-only event log with deterministic IDs | `FlowableHistoryProjection` (+ `FlowableHistoryPollingFeed`, `WorkflowHistoryQueryEntity`) — `ON CONFLICT DO NOTHING`, watermark-driven |
| Internal aggregate from our own DB | `RecordActivityRollupProjection` (+ `RecordActivityRollupFeed`, `RecordActivityRollupQueryEntity`) — auth-gated via parent kind |
| Cross-tier analytics over hot Postgres + cold Parquet | `WorkflowAnalyticsQueryEntity` + `DuckDbAnalyticsRunner` |

## Steps

### 1. Schema

File: `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs`

Add a `private const string XxxCacheSchemaSql = """ ... """;` block and a
matching `ExecuteSqlRawAsync` call inside `EnsureAsync`. The table must
include:

- A primary key matching what your `ChangeEvent.SourceId` will be.
- Indexed scalar columns for the predicates you expect in `WHERE`.
- An `auth_tags JSONB NOT NULL DEFAULT '{{}}'::jsonb` column **if** the
  cache has its own permission tags (Pattern A in
  `docs/projection-framework/aql-integration.md`).
- `projection_version INT NOT NULL DEFAULT 1` and
  `last_sync_at TIMESTAMPTZ NOT NULL` (bookkeeping the framework relies on).
- A `GIN (auth_tags jsonb_path_ops)` index on the JSONB column.

**Pitfall**: `ExecuteSqlRawAsync` uses `string.Format` placeholder syntax,
so literal `{` in the SQL must be doubled (`{{`). The empty-JSONB literal
is `'{{}}'::jsonb`.

### 2. Scaffolded EF entity

File: `src/AutoNate.Web/Persistence/Scaffolded/XxxCache.cs`

Plain partial class with one property per column. Convention: JSONB columns
become `string` properties named `XxxJson` (the framework serializes /
deserializes at the projection boundary).

### 3. DbContext partial

File: `src/AutoNate.Web/Persistence/AutoNateDbContext.ProjectionCaches.cs`

Add a `public virtual DbSet<XxxCache> XxxCache { get; set; } = null!;`
property and an `entity.HasKey(...).HasName(...); entity.ToTable(...); ...`
block inside `OnModelCreatingPartial`.

### 4. Source type

File: typically `src/AutoNate.Web/Services/<Area>/<Aspect>Snapshot.cs`

A `sealed record class` describing one source row. This is what the feed
emits and the projection consumes. Different from the EF entity — the source
shape mirrors the external system; the EF entity mirrors the DB table.

### 5. Projection

File: `src/AutoNate.Web/Services/<Area>/<Aspect>Projection.cs`

Implements `IProjection<TSource>`. Use the explicit dedup-then-upsert
pattern from `FlowableExecutionProjection`:

```csharp
// 1. Collapse batch to latest-per-source-id.
var latest = new Dictionary<string, ChangeEvent<TSource>>(StringComparer.Ordinal);
var deletes = new HashSet<string>(StringComparer.Ordinal);
foreach (var change in batch) { /* dedup */ }

// 2. Per-row upsert via raw SQL with ON CONFLICT DO UPDATE.
foreach (var change in latest.Values)
{
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO xxx_cache (...) VALUES (...)
        ON CONFLICT (id) DO UPDATE SET ...
        """, ct);
}

// 3. Per-id delete for Op = Delete.
foreach (var id in deletes) { /* DELETE WHERE id = {id} */ }
```

Idempotency rule: `ON CONFLICT DO UPDATE` for mutable rows, `ON CONFLICT
DO NOTHING` for append-only event logs (use deterministic IDs from a
content hash so replays collide on the same PK).

### 6. Change feed

File: `src/AutoNate.Web/Services/<Area>/<Aspect>PollingFeed.cs`

For poll-based sources, subclass `PeriodicPollingFeed<TSource>` and
override `TickAsync`. For incremental polling, inject
`IProjectionWatermarkStore`, read with `GetAsync(FeedName, ct)` before
fetching, call `SetAsync` after.

For event-driven sources, implement `IChangeFeed<TSource>` directly and
emit `ChangeEvent`s from the subscription handler.

### 7. AQL entity

File: `src/AutoNate.Web/Services/Query/Entities/XxxQueryEntity.cs`

Copy `WorkflowExecutionsQueryEntity` as a skeleton. Adjust:

- `Name` → user-facing entity name (use in `FROM` clauses).
- `StaticSchema` → the columns AQL exposes.
- The `db.XxxCache` reference inside `ExecuteAsync`.
- The `EntityKinds.X` passed to `FilterQueryAsync` (Pattern A) or
  the parent kind (Pattern B).

If you need `COUNT/SUM/AVG/...` over the data, also handle the GROUP
path inside `ExecuteAsync` — see `RecordActivityRollupQueryEntity` for
a simple case.

For scalar-per-row functions like `NUMEXECUTIONS()` (no GROUP required),
declare them in the entity's `RowFunctions` property and implement
`RowFunctionDataType`. The validator and projection layer both consult
those lists. See `WorkflowModelsQueryEntity`.

### 8. Selector compiler (only if cache has its own permission tags)

File: `src/AutoNate.Web/Authorization/Selectors/XxxSelectorCompiler.cs`

Implement `ISelectorCompiler<XxxCache>`. Map each tag in your cache's
`auth_tags` column to a LINQ predicate expression. Reuses the existing
selector AST and grant evaluator — see
`WorkflowExecutionCacheSelectorCompiler`.

If you go with Pattern B (parent-auth inheritance), skip this step
entirely — the parent kind's selector compiler does the work.

### 9. Add the kind + action vocabulary (only if Pattern A)

If your cache has its own permission kind, add the const to
`EntityKinds.cs`, register the `EntityTypeDefinition` in
`CoreEntityTypes.cs` (including the `tags[]` array — must match the
selector compiler's accepted tag names), and use the
`add-permission-gate` skill for the rest.

### 10. Wire up in `Program.cs`

Around the existing projection registrations:

```csharp
builder.Services.Configure<XxxOptions>(
    builder.Configuration.GetSection(XxxOptions.SectionName));

AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<XxxSnapshot, XxxProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<XxxSnapshot, XxxPollingFeed>(builder.Services);

builder.Services.AddSingleton<ISelectorCompiler, XxxSelectorCompiler>();  // Pattern A only

builder.Services.AddScoped<XxxQueryEntity>();
builder.Services.AddScoped<IQueryEntity>(sp => sp.GetRequiredService<XxxQueryEntity>());
```

**Don't forget the double registration** for the AQL entity — once as
concrete, once as `IQueryEntity`. Forgetting the second produces a
silent failure: the entity instantiates but isn't in the executor's
registry.

### 11. Configuration defaults

`appsettings.json`:

```json
"XxxCache": {
  "PollInterval": "00:05:00",
  "CurrentProjectionVersion": 1
}
```

See `docs/projection-framework/configuration.md` for the
full reference.

### 12. Tests

Copy the shape from `ProjectionFrameworkTests` (Phase 1) for a basic
round-trip test:

1. `await using var factory = await AutoNateWebApplicationFactory.CreateAsync();`
2. Seed via the projection directly (`projection.ApplyAsync(...)`).
3. Assert the cache row exists.
4. Run an `AqlQuery` through the AQL entity's
   `PrepareAsync` → `ExecuteAsync`.
5. Assert on `result.Rows`.

The test factory disables the worker by default
(`Projections:WorkerEnabled = false`), so background ticks don't
interfere. Direct `projection.ApplyAsync` calls give deterministic
seeding.

## Verification checklist

After wiring, check:

- `dotnet build` is clean (warnings about float equality / static methods
  are pre-existing in the codebase, not yours).
- `dotnet test --filter "FullyQualifiedName~ProjectionFramework"` still
  green (existing framework tests should never break — if they do, you
  changed a contract).
- `GET /api/admin/projections/` returns your new entry with
  `lastAppliedAtUtc` advancing as the feed ticks.
- An AQL query against your new entity returns rows.
- Pausing via `POST /api/admin/projections/{name}/pause` stops new
  applies; resuming restarts them.

## Common slip-ups

- **`'{}'::jsonb` in `ExecuteSqlRawAsync`** — crashes with a
  format-parsing error. Use `'{{}}'::jsonb` (doubled braces). Same
  rule for any other `{` literal in raw SQL.
- **Forgetting the second `IQueryEntity` DI registration** — entity
  instantiates fine, AQL executor never sees it, queries return
  "Unknown entity 'X'".
- **DateTime kind mismatches** — Postgres `timestamptz` columns only
  accept `Kind = Utc` parameters. After EF queries (especially
  `SqlQueryRaw`), call `DateTime.SpecifyKind(..., Utc)` before passing
  the result back to another EF query.
- **Skipping idempotency** — a projection that uses `INSERT` (not
  `ON CONFLICT DO ...`) will double-insert on retry. Always use the
  upsert idiom.
- **Watermark store updates on failure** — if your tick advances the
  watermark before the data is durably committed, a crash will skip
  rows. Update the watermark only after `EmitAsync` has completed for
  every event in the page.
- **Sub-minute polling intervals** — usually wrong. The polling layer is
  the safety net; sub-minute freshness should come from a push/event
  feed once the bridge is wired up. If your business case actually
  needs sub-minute poll, document it and lower the interval explicitly.
