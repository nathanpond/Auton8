# Projection Framework

A reusable substrate for materializing external or expensive-to-compute data
into AQL-queryable Postgres tables (the **cache**), with pluggable
change-detection, versioned schemas, optional cold-tier archival, and
selector-based row-level authorization that piggybacks on AutoNate's
existing `IAuthorizer` pipeline.

The framework's first consumer is the Flowable workflow cache (Phases 1–3).
A second internal consumer (`record_activity_rollup`) demonstrates that it
isn't Flowable-specific — anything that can be re-emitted as a stream of
"this thing changed" events can become a cache.

## Why caches at all

Two recurring problems in AutoNate:

1. **External APIs are the bottleneck.** A dashboard widget that wants
   "executions per workflow in the last 30 days" can't make N round trips
   to Flowable per page load. The data has to live somewhere local that
   AQL can scan in a single query plan.

2. **Some aggregates are too expensive to compute per request.** Counting
   records-by-type-by-day over millions of rows wastes CPU on every
   dashboard render. Materialize once, read forever.

The projection framework solves both with one substrate: a write side
(projection + change feed) populates a Postgres table; the read side
(AQL entity + authorizer) queries it with full row-level security.

## Mental model

```
┌──────────────────────────────────────────────────────────────┐
│  Source (Flowable REST, records table, internal aggregate,   │
│  plugin data source, eventually NATS push, etc.)             │
└────────────────────────────┬─────────────────────────────────┘
                             │ emits ChangeEvent<TSource>
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  IChangeFeed<TSource>                                        │
│  (FlowableExecutionPollingFeed, ManualChangeFeed<T>, ...)    │
└────────────────────────────┬─────────────────────────────────┘
                             │ drained by
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  ProjectionWorker (BackgroundService)                        │
│  · honors pause flag                                         │
│  · batches into MaxBatchSize windows                         │
│  · retries with exponential backoff                          │
│  · records health on every apply / failure                   │
└────────────────────────────┬─────────────────────────────────┘
                             │ calls IProjection<TSource>.ApplyAsync
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  IProjection<TSource> (your code)                            │
│  · idempotent UPSERT by SourceId                             │
│  · auth-tag extraction → JSONB column                        │
│  · writes into the cache table                               │
└────────────────────────────┬─────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  Postgres cache table (hot tier)                             │
│  · indexed for the AQL access pattern                        │
│  · auth_tags JSONB consumed by selector compiler             │
└────────────────────────────┬─────────────────────────────────┘
                  (optional) │ archived by ColdTierArchiverService
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  Parquet files on disk (cold tier, queried via DuckDB)       │
└──────────────────────────────────────────────────────────────┘
```

On the read side:

```
AQL query
   │
   ▼
IQueryEntity (your entity)
   │
   │  applies IAuthorizer.FilterQueryAsync<T>
   │  using a tag-based ISelectorCompiler<T>
   ▼
Postgres cache table  →  rows  →  QueryResult
```

## What to read next

- **[Architecture](architecture.md)** — the framework's interfaces in detail.
- **[Recipe: add a host projection](recipe-add-a-projection.md)** —
  step-by-step for a projection in the main Web project.
- **[Recipe: plugin-contributed scheduled job](recipe-plugin-projection.md)** —
  for plugin authors using `IPluginProjections`.
- **[Operations](operations.md)** — admin endpoints, pause/resume, rebuild,
  cold tier, retention janitor.
- **[Examples](examples.md)** — tour of the two shipped projections.
- **[Configuration](configuration.md)** — every `appsettings.json` knob.

## Quick decision tree

| Question | Answer |
|---|---|
| "I want AQL to query data that lives in an external API." | Build a projection + polling feed. See [recipe](recipe-add-a-projection.md). |
| "I want AQL to query an expensive aggregate over our own DB." | Build a projection + polling feed that reads from your DB. See `RecordActivityRollup` in [examples](examples.md). |
| "A plugin should contribute a scheduled refresh of its own data." | Use `IPluginContext.Projections.RegisterScheduled`. See [plugin recipe](recipe-plugin-projection.md). |
| "I want sub-second freshness, not poll-based." | Build a `NatsEventChangeFeed<T>` and emit `ChangeEvent` from a NATS subscriber. The polling feed can stay registered as a safety net. |
| "I need to query 5+ years of history without melting Postgres." | Enable the cold tier — see [operations: cold tier](operations.md#cold-tier). |
| "I have caches already; I just need a friendlier AQL view that composes them." | Build a read-only AQL entity that reads from existing caches. See `Flows` in [examples](examples.md) — derived `Status` (Errored overlay), JOINed `FlowName`, `CURRENTSTEP(arg)` row function. No new projection needed. |
