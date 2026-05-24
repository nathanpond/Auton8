# Architecture

Five interfaces compose the framework. Source lives in
`src/AutoNate.Web/Services/Projections/`.

## Core types

### `ChangeEvent<TSource>`

```csharp
public sealed record ChangeEvent<TSource>(
    ChangeOp Op,            // Upsert | Delete
    string SourceId,         // stable key for dedup (e.g. flowable_instance_id)
    TSource? Source,         // null when Op = Delete
    DateTimeOffset ObservedAt);
```

The unit of work that crosses every boundary in the framework. `SourceId`
is what the projection's `ON CONFLICT` clause keys on; two events with the
same `SourceId` are guaranteed to apply in `ObservedAt` order *within a
single batch*, with the last write winning for `Upsert`s.

### `IProjection<TSource>`

```csharp
public interface IProjection<TSource> : IProjection
{
    string Name { get; }                // globally unique
    int Version { get; }                // bump → triggers reprojection
    Type SourceType { get; }
    Task ApplyAsync(
        IReadOnlyList<ChangeEvent<TSource>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken);
}
```

Owns the persistence. Receives a batch under a shared `DbContext`, writes
however the cache table is shaped. Two contract requirements:

- **Idempotent on `SourceId`.** Replaying the same Upsert is a no-op
  modulo row contents; replaying a Delete after the row is gone is a no-op.
  This is what lets a polling feed and a NATS feed both target the same
  projection without dedup logic in the framework.
- **All-or-nothing per batch.** If `ApplyAsync` throws, the framework
  retries the whole batch with exponential backoff. Partial commits inside
  `ApplyAsync` will lead to double-applies on retry — wrap a transaction
  if you can't make the writes individually idempotent.

### `IChangeFeed<TSource>`

```csharp
public interface IChangeFeed<TSource>
{
    string FeedName { get; }
    IAsyncEnumerable<ChangeEvent<TSource>> StreamAsync(CancellationToken ct);
}
```

A source of changes. The framework ships three implementations:

- **`PeriodicPollingFeed<TSource>`** (base class) — subclass it, override
  `TickAsync` to fetch a page and `EmitAsync` each row. The base handles
  the timer, channel-based backpressure, and graceful shutdown.
- **`ManualChangeFeed<TSource>`** — in-memory channel that tests and admin
  endpoints can push into directly.
- *(Future)* `NatsEventChangeFeed<T>` — push-based feed for sub-second
  freshness, paired with a Flowable event-registry bridge.

Multiple feeds can target the same projection — that's the model for "push
for freshness + poll for safety." The projection's idempotency contract
makes the overlap a no-op.

### `IProjectionRegistry`

DI-populated snapshot of every registered `IProjection`. Resolved once by
the worker at startup; consulted by admin endpoints and `BackfillRunner`.

### `IProjectionHealthService`

Single-process state holder for runtime telemetry:

```csharp
bool IsPaused(string projectionName);
void Pause(string projectionName);
void Resume(string projectionName);
void RecordApply(string projectionName, string feedName, int eventCount);
void RecordFailure(string projectionName, string feedName, string message);
void RecordWatermark(string feedName, DateTimeOffset watermark);
IReadOnlyList<ProjectionHealthSnapshot> Snapshot(IEnumerable<IProjection> ps);
```

Written by `ProjectionWorker` on every apply, by
`PostgresProjectionWatermarkStore` on every set, and by the
`PluginScheduledJobsHostedService` on every plugin tick. Read by
`/api/admin/projections` and by the `projection.lag_seconds` Prometheus
gauge.

Multi-process deployments share `projection_versions` and
`projection_watermarks` rows (Postgres-backed), but the per-instance
counts and pause flag are local to each app process. Cross-instance pause
signaling is a future enhancement; today, hitting "pause" on a deploy
behind a load balancer pauses only the instance that handled the click.

### `IProjectionVersionStore` and `IProjectionWatermarkStore`

Postgres-backed bookkeeping. Versions track active vs. shadow rows during
reprojection; watermarks hold per-feed cursors so a restart doesn't
replay the whole history.

```sql
CREATE TABLE projection_versions (
    name TEXT NOT NULL,
    version INT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('active','shadow','retired')),
    started_at_utc TIMESTAMPTZ NOT NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    PRIMARY KEY (name, version)
);

CREATE TABLE projection_watermarks (
    feed_name TEXT PRIMARY KEY,
    watermark_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);
```

## Background services

### `ProjectionWorker`

The heart of the framework. One instance per app process. On startup:

1. Reads the projection registry.
2. For each projection, resolves every `IChangeFeed<TSource>` whose source
   type matches (DI lookup via reflected `IEnumerable<IChangeFeed<TSource>>`).
3. Spawns one drain loop per (projection, feed) pair.
4. Each loop pulls events, batches them up to `MaxBatchSize`, and calls
   `IProjection.ApplyAsync` inside a fresh `DbContext` scope.
5. On exception: retries with exponential backoff up to `MaxAttempts`,
   then logs and drops the poison batch so the feed doesn't wedge.
6. Honors `IProjectionHealthService.IsPaused` before flushing each batch.

Disabled in test fixtures by default
(`Projections:WorkerEnabled = false`) so xUnit parallelism doesn't burn
CPU on dozens of polling loops across simultaneous test factories. Tests
that need to exercise the live drain path opt in explicitly.

### `BackfillRunner`

Orchestrates a one-shot full reprojection. Invoked by:

- The admin endpoint `POST /api/admin/projections/{name}/rebuild`.
- Tests that need to seed the cache without waiting for the live feed.

Requires an `IProjectionBackfillSource<TSource>` to be registered for the
projection's source type. The Flowable projections don't ship a backfill
source yet — the live polling feed picks up historic rows as it pages.
Future work: per-projection backfill sources that page Flowable history.

### `PluginScheduledJobsHostedService`

Snapshots the `PluginScheduledJobRegistry` at startup and spawns one
drain loop per plugin-registered scheduled job. Records into
`ProjectionHealthService` so plugin jobs appear on
`/api/admin/projections` alongside built-in projections. See the
[plugin recipe](recipe-plugin-projection.md).

### `WorkflowCacheRetentionService`

Periodic sweep that purges cache rows past their retention horizon. Reads
per-process overrides from `process_retention_config` and falls back to
`FlowableCache:DefaultRetentionDays` (default 2555 ≈ 7 years). Public
`RunOnceAsync` for tests.

### `ColdTierArchiverService`

Phase 3 service that moves aged event-log rows out of Postgres and into
zstd-compressed Parquet files on disk. Disabled by default
(`FlowableCache:ColdTier:Enabled = false`); enable when you need history
older than ~90 days and Postgres growth is becoming a concern. See
[operations: cold tier](operations.md#cold-tier).

## Authorization integration

Cache rows carry an `auth_tags` JSONB column populated by the projection
at write time (e.g. `{"startedby": "alice", "processkey": "approval"}`).
A per-kind `ISelectorCompiler<T>` translates AutoNate's selector grants
(`[startedby=$me]`, `[processkey=approval]`) into LINQ predicates that
EF translates to SQL `WHERE` clauses against the JSONB column.

```
SelectorAst grant (e.g. "[startedby=$me]")
   │
   ▼
ISelectorCompiler<WorkflowExecutionCache>     ← per-kind compiler
   │   builds Expression<Func<WorkflowExecutionCache, bool>>
   ▼
IAuthorizer.FilterQueryAsync<WorkflowExecutionCache>
   │   AND-combines allows, NOT-OR's denies
   ▼
IQueryable<WorkflowExecutionCache>             ← filtered, ready to scan
```

The AQL entity calls `IAuthorizer.FilterQueryAsync` against the cache
table, then runs the user's WHERE/ORDER BY/LIMIT on the result. The
existing record-level auth machinery handles everything else — no new
permission infrastructure required.

For projections whose rows belong to a parent entity (e.g. tasks belong
to executions, history events belong to executions), the pattern is:

1. Authorize the parent: `FilterQueryAsync<ParentCache>` to get visible
   parent IDs.
2. Restrict the child query to `WHERE parent_id IN (visible_ids)`.

See `WorkflowVariablesQueryEntity` and `WorkflowHistoryQueryEntity` for
the canonical implementation.

## Cold-tier query path (Phase 3 only)

`WorkflowAnalyticsQueryEntity` shows how to unify hot + cold for
analytical queries:

1. Resolve visible instance IDs via `FilterQueryAsync` against the hot
   `workflow_execution_cache`.
2. Pull hot event rows from Postgres via EF (limited to visible IDs).
3. Open a per-request `DuckDbAnalyticsRunner`.
4. Load hot rows into an in-memory DuckDB staging table.
5. Register a view over `read_parquet('var/projections/.../*.parquet')`
   for cold rows.
6. `UNION ALL` the two views and run the user's aggregate query.

Per-request DuckDB connections are cheap (~10ms setup/teardown); avoids
any shared-state coordination across concurrent queries.

## File map

```
src/AutoNate.Web/Services/Projections/
├── ChangeEvent.cs                     ← record + ChangeOp enum
├── IProjection.cs                     ← contract + non-generic facet
├── IChangeFeed.cs                     ← contract
├── IProjectionRegistry.cs / ProjectionRegistry.cs
├── IProjectionVersionStore.cs
├── IProjectionWatermarkStore.cs
├── IProjectionHealthService.cs
├── ProjectionHealth.cs                ← snapshot records
├── ProjectionHealthService.cs         ← in-memory thread-safe impl
├── ProjectionOptions.cs               ← framework-wide knobs
├── ProjectionMetrics.cs               ← OTel meters
├── ProjectionWorker.cs                ← BackgroundService drain loop
├── BackfillRunner.cs                  ← one-shot full reprojection
├── IProjectionBackfillSource.cs       ← contract for backfill sources
├── ProjectionServiceCollectionExtensions.cs  ← AddProjection<>, AddChangeFeed<>
├── Feeds/
│   ├── ManualChangeFeed.cs
│   └── PeriodicPollingFeed.cs
└── Stores/
    ├── PostgresProjectionVersionStore.cs
    └── PostgresProjectionWatermarkStore.cs

src/AutoNate.Web/Services/Flowable/Cache/
├── FlowableCacheOptions.cs
├── FlowableExecutionProjection.cs / FlowableExecutionPollingFeed.cs
├── FlowableTaskProjection.cs / FlowableTaskPollingFeed.cs
├── FlowableVariableProjection.cs / FlowableVariablePollingFeed.cs
├── FlowableHistoryProjection.cs / FlowableHistoryPollingFeed.cs
├── FlowableInstanceVariables.cs           ← per-instance variable snapshot
├── IFlowableReadThrough.cs / FlowableReadThrough.cs
├── WorkflowCacheRetentionService.cs
└── ColdTier/
    ├── ColdTierOptions.cs / ColdTierLayout.cs
    ├── ColdTierArchiverService.cs
    └── DuckDbAnalyticsRunner.cs

src/AutoNate.Web/Services/Records/Rollups/   ← internal-aggregate example
├── RecordActivityRollupOptions.cs
├── RecordActivityRollupSnapshot.cs
├── RecordActivityRollupProjection.cs
└── RecordActivityRollupFeed.cs

src/AutoNate.Web/Services/Query/Entities/    ← AQL entities
├── WorkflowExecutionsQueryEntity.cs        ← raw cache-column debug view
├── FlowsQueryEntity.cs                     ← user-facing view: derived
│                                              Status (Errored overlay),
│                                              FlowName JOIN, CURRENTSTEP()
├── WorkflowTasksQueryEntity.cs
├── WorkflowVariablesQueryEntity.cs
├── WorkflowHistoryQueryEntity.cs
├── WorkflowAnalyticsQueryEntity.cs         ← cold+hot UNION via DuckDB
└── RecordActivityRollupQueryEntity.cs

src/AutoNate.Web/Authorization/Selectors/    ← per-kind selector compilers
├── WorkflowExecutionCacheSelectorCompiler.cs
└── WorkflowTaskCacheSelectorCompiler.cs

src/AutoNate.Web/Endpoints/
└── AdminProjectionsEndpoints.cs             ← admin REST surface

src/AutoNate.Spa/src/pages/admin/
└── Projections.tsx                          ← admin SPA page
```
