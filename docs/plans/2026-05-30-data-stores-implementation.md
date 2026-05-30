# Data Stores, Datasets & Analytics Pipeline — Implementation Plan

> **File location note:** per `reference_plan_location` memory, plan files live in `./docs/plans/` as `YYYY-MM-DD-kebab.md`. After approval, move this file to `/Users/npond/RiderProjects/AutoNate/docs/plans/2026-05-30-data-stores-implementation.md`.

## Context

AutoNate today handles records, workflows, notes/documents, and an AQL query layer over a fixed set of in-host entities. It has no first-class concept of *user-managed data assets* — there's no way to upload a folder of files into a permissioned bucket, ingest a CSV into a queryable table, pull from an external REST/SMB source on a schedule, or compose those sources into a downstream analytical product. The Workflow engine (Flowable) is human-task-shaped and unsuited to data-pipeline DAGs. This plan adds a coherent stack — **DataStores → DataConnectors → Datasets → Queries → Transformers/Analyzers → Analytics Pipelines** — that lets users bring data into the AutoNate ecosystem, expose it through the existing AQL surface, and compose it through a visual pipeline editor backed by a multi-language execution engine.

## Locked-in decisions

| Decision | Choice | Why |
|---|---|---|
| **SQL DataStore isolation** | Second Postgres DB `autonate_datastores` on the same cluster; one schema + read-only role per datastore; one shared writer role for ingest | Strong blast-radius isolation without standing up another cluster |
| **Pipeline executor** | Native .NET orchestrator + Node.js sidecar (`services/executor/`) for user-authored JS (V8 isolates) and Python (Pyodide WASM by default) | Multi-language is mandatory; mirrors `services/hocuspocus/` precedent |
| **Sandboxing** | User-authored JS/Python sandboxed by default; new `executeunsafe` action unlocks full CPython | Trust-but-verify; pandas/numpy is needed for real Python work |
| **AQL surface** | One `Dataset` meta-entity — `FROM Dataset("name") WHERE …`; dataset grants override underlying source grants | Curated-product model; user explicitly endorsed |
| **Cube Core** | Deferred to the dashboarding PR | Doesn't actually abstract REST APIs; its strengths (pre-aggs, BI connectivity) are dashboard-shaped |
| **Cached refresh & pipeline schedule** | Both modeled as `IProjection<T>` and driven by the existing `ProjectionWorker` | Reuses pause/resume/health UI; no new BackgroundService |

## Critical files to read before starting

- `src/AutoNate.Web/Services/Query/Entities/IQueryEntity.cs` + `IQueryEntityRegistry.cs` (entity contract)
- `src/AutoNate.Web/Services/Query/AqlParser.cs` + `AqlValidator.cs` (grammar — needs generalised `FROM Entity("arg")`)
- `src/AutoNate.Web/Authorization/EntityKinds.cs` + `Authorization/Selectors/` (permission wiring)
- `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs` (the *single*-DB initializer to abstract)
- `src/AutoNate.Web/Services/Projections/ProjectionWorker.cs` + `IProjection.cs` + `PeriodicPollingFeed.cs` (the refresh/schedule engine to reuse)
- `src/AutoNate.Plugin.Abstractions/IPluginContext.cs` + `IPluginBehaviors.cs` + `IPluginProjections.cs` (pattern to mirror for the new plugin surfaces)
- `src/AutoNate.Web/Services/Content/FilesystemContentAttachmentStore.cs` + `Storage/DataPaths.cs` (file storage to reuse for the Files DataStore)
- `src/AutoNate.Web/Endpoints/ExternalConnectionEndpoints.cs` (encrypted-secret pattern to reuse for REST/SMB creds)
- `services/hocuspocus/` (Node sidecar precedent)
- Project skills: `add-permission-gate`, `add-projection`, `add-audit-event`, `plugin-creator`

## Phase 0 — Cross-cutting prerequisites

**EntityKinds added** to `src/AutoNate.Web/Authorization/EntityKinds.cs`:
`datastore`, `dataconnector`, `dataset`, `transformer`, `analyzer`, `pipeline`, `pipelinerun`. (Saved queries already exist via `SavedQueryEndpoints.cs` — verify whether to introduce a `query` kind or reuse.)

**Actions added** to `Authorization/Actions.cs`:
`Refresh`, `Run`, `Schedule`, `ExecuteUnsafe`, `Share`, `Connect` (test-connection). `View`/`Edit`/`Delete`/`Create`/`List`/`Manage` already exist.

**Selector compilers** under `src/AutoNate.Web/Authorization/Selectors/`, one per kind. Follow the `RecordSelectorCompiler` + `RecordSelectorSqlCompiler` split where AQL pushdown will need a SQL form.

Each kind ships its full `add-permission-gate` checklist (selector compiler, `IAuthorizer<T>`, `[RequirePermission]` filter, admin UI, enforcement test).

**Abstract the migration runner.** Today `DatabaseSchemaInitializer.cs` is single-DB. Extract an `IDatabaseInitializer` interface and a `PrimaryDatabaseInitializer` implementation, then add a `DatastoresDatabaseInitializer` in Phase 1 that targets the second DB. Bootstrap order: primary → datastores → schemas → plugins.

---

## Phase 1 — DataStores (Files + SQL) + DataConnectors (REST + SMB)

**Goal:** create a datastore, attach a connector, upload files, ingest a CSV. No datasets/AQL yet.

**New files (host):**
- `src/AutoNate.Web/Services/DataStores/IDataStore.cs`, `IDataStoreRegistry.cs`, `DataStoreKind.cs` (`FileType`, `SqlType`)
- `src/AutoNate.Web/Services/DataStores/File/FileDataStore.cs` — backs a new `DataPaths.DatastoresRoot` (extend `Storage/DataPaths.cs` + `DataOptions.cs`). Reuses `IContentAttachmentStore` semantics for blob durability
- `src/AutoNate.Web/Services/DataStores/Sql/SqlDataStoreProvisioner.cs` — per-datastore schema + read-only role in `autonate_datastores`. Shared writer role provisioned once at startup
- `src/AutoNate.Web/Services/DataStores/Sql/CsvIngestor.cs` — `CsvHelper` (new NuGet) + `Npgsql.NpgsqlBinaryImporter` (COPY) for streaming uploads; auto-infers types with a confirm-before-commit editor in the UI; one table per CSV upload (user-named, sanitized)
- `src/AutoNate.Web/Services/DataConnectors/IDataConnector.cs`, `IDataConnectorRegistry.cs`, `ConnectorTestResult.cs`, `IConnectorRefreshState.cs` (holds `LastFetchedAt`, cursor, etc.)
- `src/AutoNate.Web/Services/DataConnectors/Builtin/RestDataConnector.cs` (HttpClient + bearer/api-key/basic auth; `{lastFetchDate}` token substitution in URL/body)
- `src/AutoNate.Web/Services/DataConnectors/Builtin/SmbDataConnector.cs` (`SMBLibrary` NuGet; path allowlist; credential reuse via existing encrypted-secrets pattern)
- `src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs`, `DataConnectorEndpoints.cs` (CRUD + `POST /test`, `POST /datastores/{id}/files` multipart, `POST /datastores/{id}/tables` CSV ingest). Multipart pattern modeled on `Endpoints/AdminPluginsEndpoints.cs`

**Persistence (primary DB):**
- New SQL migration adds `datastores`, `dataconnectors`, `datastore_files` (file metadata), `datastore_tables` (per-table metadata with column schema JSON), `connector_runs` (history of fetches)
- New `DatastoresDatabaseInitializer.cs` (via the Phase 0 `IDatabaseInitializer` abstraction) ensures `autonate_datastores` DB exists, creates the shared writer role, and on each datastore creation provisions the schema + read-only role at runtime

**Config:** `appsettings.json` additive `ConnectionStrings:Datastores`. If absent → feature disabled and a `SystemIssue` is recorded rather than crashing.

**Secrets:** REST tokens + SMB creds reuse `ExternalConnectionEndpoints.cs`'s encryption-at-rest pattern.

**Plugin surface:** new `IPluginConnectors` in `src/AutoNate.Plugin.Abstractions/`, mirroring `IPluginBehaviors.cs` exactly. Added as `Connectors` property to `IPluginContext`. Plugins call `context.Connectors.Register(IDataConnector)`. (Files/SQL DataStores stay host-built-in for now; if plugins ever need to add a *kind* of datastore, surface `IPluginDataStoreKinds` later.)

**SPA:** `src/AutoNate.Spa/src/pages/admin/datastores/*` and `pages/admin/dataconnectors/*`. Mantine v9 forms via the `mantine-form` skill. Use `DataTable` wrapper for file/table lists.

**Tests:**
- xUnit: provisioner creates schema + role, role can read its own schema but not siblings; REST connector retries on 5xx with backoff; SMB connector enforces path allowlist; CSV ingestor handles malformed rows
- Playwright `tests/AutoNate.E2E.Tests/DataStoreAdminTests.cs`: admin creates file datastore → uploads files → folder navigation works; admin creates SQL datastore → uploads CSV → table appears; unprivileged user cannot see either

---

## Phase 2 — Datasets (Virtual + Cached) + AQL `Dataset(...)` entity

**Goal:** make datastores queryable through AQL.

**New files (host):**
- `src/AutoNate.Web/Services/Datasets/IDataset.cs`, `DatasetMode.cs` (`Virtual`, `Cached`), `IDatasetRegistry.cs`, `DatasetSchema.cs` (column→type map resolved at create-time and persisted)
- `src/AutoNate.Web/Services/Datasets/Virtual/VirtualDatasetExecutor.cs` — translates `AqlAst` predicates into per-source query (SQL `WHERE`, REST query params, file scan)
- `src/AutoNate.Web/Services/Datasets/Cached/CachedDatasetStore.cs` — materialised cache table per dataset, in `autonate_datastores.cache_<datasetid>` schema (host-managed for cached datasets regardless of source kind)
- `src/AutoNate.Web/Services/Datasets/Cached/DatasetRefreshProjection.cs` — `IProjection<DatasetId>` driven by `PeriodicPollingFeed<DatasetId>` using each dataset's configured cron. Registered through `ProjectionServiceCollectionExtensions.cs`. Inherits `_health.IsPaused()` admin UI from the projection framework
- `src/AutoNate.Web/Services/Query/Entities/DatasetQueryEntity.cs` — implements `IQueryEntity`. `PrepareAsync` resolves the literal name argument, fetches the schema, returns an `IPreparedQuery` that delegates to virtual/cached executors. Permission filter: `dataset:view:<id>` only — dataset grant fully overrides source grants
- `src/AutoNate.Web/Endpoints/DatasetEndpoints.cs` (CRUD + `POST /refresh` to enqueue immediate refresh)

**AQL grammar change (load-bearing):**
Generalise `AqlParser.cs` so any `IQueryEntity` can opt into a string-literal argument: `FROM EntityName("arg-value")`. Implement as: parser produces a new `FromClause.Argument` AST node; `IQueryEntity` gains an optional `AcceptsArgument => false` default; `AqlValidator.cs` rejects args on entities that don't accept; `AqlSuggestionService.cs` learns the new shape; the chatbot's `describe_aql_entity` introspection picks up the new field. This is the first parameterised `FROM` — touch every site that assumes bare identifiers.

**Cross-datastore JOIN gap:** v2 of this phase forbids multi-datastore JOINs at the validator (clear error). Phase 3 of *this* feature, or a follow-up, can lift it via in-memory join with a hard row cap (e.g. 100k per side).

**Permissions test:** extend `tests/AutoNate.E2E.Tests/PermissionGatingTests.cs` to prove a user with `dataset:view:<id>` but no `datastore:view` can still query through the dataset (curated-product semantics).

**Tests:**
- xUnit: virtual executor pushdown (predicate → SQL `WHERE`, REST query params, file scan filter); cached executor reads from cache table; refresh projection idempotent (replay = no-op)
- Playwright `DatasetAqlTests.cs`: author runs `Dataset("sales") WHERE Amount > 100` in the existing AQL UI and sees rows

---

## Phase 3 — Saved Queries (defined + shareable)

**Goal:** elevate AQL queries to first-class permissioned entities with share semantics.

**Files:**
- Audit `src/AutoNate.Web/Endpoints/SavedQueryEndpoints.cs` + `EfCoreSavedQueryStore.cs`. Add: share-token issuance, signed-URL anonymous view (model on `ContentShareEndpoints.cs`), per-query parameter binding (`?param=…` substitution into AQL literals — **binding, not interpolation**; `AqlValidator` re-runs over the substituted tree)
- Permission gate for `query` kind if not already present (verify via `add-permission-gate` skill before introducing — saved queries may already be `siteconfig`-gated)
- SPA: query browser page + share modal

**Tests:** Playwright share-link round trip, anonymous viewer sees results, edit gated by `query:edit`.

---

## Phase 4 — Transformer & Analyzer registries + .NET built-ins

**Goal:** ship the runner contracts and the plugin extension surface *before* the pipeline editor (Phase 5) depends on them.

**New files:**
- `src/AutoNate.Web/Services/Transformers/ITransformer.cs` (input schemas, output schema, `RunAsync(DataFrame[], CancellationToken)`), `TransformerRegistry.cs`
- `src/AutoNate.Web/Services/Analyzers/IAnalyzer.cs`, `AnalyzerRegistry.cs`
- `src/AutoNate.Plugin.Abstractions/DataFrame.cs` — schema + rows abstraction. Lives in the plugin abstractions package so plugin code can produce/consume it without taking a host reference. **Lock the shape before this phase ships; it's an ABI surface.**
- Built-in transformers under `Services/Transformers/Builtin/`: `csv-to-json`, `json-to-csv`, `xlsx-to-csv`, `json-flatten`, `column-rename-cast`, `filter-rows`, `dedupe`, `join-two-inputs`, `pivot`, `unpivot`, `regex-extract`, `schema-infer`, `null-fill`, `date-normalize`. CSV via `CsvHelper`, XLSX via `ClosedXML`. The rest are pure LINQ over `DataFrame`
- Built-in analyzers under `Services/Analyzers/Builtin/`: `summary-statistics`, `null-rate`, `distinct-count`, `top-k`, `correlation-matrix`, `anomaly-zscore`, `anomaly-iqr`, `trend-linear-regression`, `group-by-aggregate`, `histogram-bin`, `k-means-cluster` (bounded K and N — written by hand, no `Accord.NET` dep)

**Plugin surface:** `IPluginTransformers` + `IPluginAnalyzers` in `src/AutoNate.Plugin.Abstractions/`, added as `Transformers` + `Analyzers` properties to `IPluginContext`. Same shape as `IPluginBehaviors`. Update `plugins/HelloPlugin/HelloPlugin.cs` to demo one of each.

**Tests:** xUnit per transformer/analyzer with deterministic fixtures; plugin-contributed transformer end-to-end via `tests/AutoNate.Web.Tests.SamplePlugin`.

---

## Phase 5 — Analytics Pipeline (data model + native .NET executor + React Flow UI)

**Goal:** author and run a pipeline that uses .NET transformers/analyzers only. Works without the sidecar existing.

**New files (host):**
- `src/AutoNate.Web/Services/Pipelines/PipelineGraph.cs` (DAG; node types: `dataset-source`, `transformer`, `analyzer`, `dataset-sink`), `PipelineDefinition.cs`, `PipelineRun.cs`
- `src/AutoNate.Web/Services/Pipelines/Orchestration/PipelineOrchestrator.cs` — topological sort, materialise upstream outputs, invoke node runners, persist `pipeline_run_steps` with status/timings/output-row-counts
- `src/AutoNate.Web/Services/Pipelines/Execution/INodeRunner.cs`, `BuiltinTransformerRunner.cs`, `BuiltinAnalyzerRunner.cs`, `PluginNodeRunner.cs`
- `src/AutoNate.Web/Services/Pipelines/Scheduling/PipelineScheduleProjection.cs` — `IProjection<PipelineId>` driven by `PeriodicPollingFeed<PipelineId>`. Manual `POST /pipelines/{id}/run` just inserts a `pipeline_runs` row in `Queued` state; the same projection drains it. Reuses pause/resume/health UI
- `src/AutoNate.Web/Endpoints/PipelineEndpoints.cs` + `PipelineRunEndpoints.cs`

**JetStream:** add stream `pipeline-runs` with `pipeline-run.>` subjects to `Services/Nats/NatsStreamProvisioner.cs` + subscription in `infra/dapr/components/pubsub.yaml`. In Phase 5 the only subscriber is the in-process orchestrator. This is forward-compat plumbing for Phase 6 fan-out — landing it now avoids a Phase 6 migration.

**SPA:**
- `src/AutoNate.Spa/src/pages/analytics/pipelines/PipelineEditor.tsx` — introduces React Flow (new dep `@xyflow/react`). Lazy-load the editor route to limit bundle hit
- `pages/analytics/pipelines/PipelineList.tsx`, `PipelineRunHistory.tsx`
- Page context provider via `add-page-context-provider` skill so the chatbot can see and mutate the current pipeline draft

**Tests:**
- xUnit: DAG validator (cycle detection, schema-flow type-checks across nodes); orchestrator runs a 3-node pipeline end-to-end
- Playwright `PipelineEditorTests.cs`: drag dataset → transformer → analyzer → dataset-sink, save, run, see results

---

## Phase 6 — `services/executor/` sidecar (JS + Python) + sandbox/unsafe split

**Goal:** user-authored JS and Python transformer/analyzer nodes.

**New service:**
- `services/executor/` — Node.js, layout mirrors `services/hocuspocus/` (`package.json`, `Dockerfile`, README, optional health endpoint)
- Subscribes to `pipeline-run.code-node.>` via JetStream durable consumer named `executor`
- JS: `isolated-vm` (V8 isolates). No `require`, no `fetch`, no fs. Hard timeout, memory cap
- Python: Pyodide WASM by default. Browser-grade sandbox — no `os`, no `subprocess`, no host fs
- Result published to a NATS reply subject; orchestrator awaits with timeout, surfaces failures as run-step failures

**Host side:**
- `src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs` — publishes the node payload, awaits reply
- `src/AutoNate.Web/Services/Pipelines/Execution/UnsafePythonRunner.cs` — separate runner for full CPython (pandas/numpy). Gated by new `executeunsafe` action on `transformer` + `analyzer` kinds. Authoring UI shows a "trusted" badge and forces re-approval on each code edit
- Authoring UI for JS/Python code (Monaco — likely already in the bundle for the AQL editor; verify)

**Plugin surface:** unchanged. The sandbox split applies to *user-authored* code only; plugin code runs in the host as before.

**Infra:** `infra/docker-compose.yml` adds the executor service; `Makefile` `infra-up` brings it up alongside hocuspocus.

**Tests:** xUnit on the serialization contract; integration test spinning up the executor in a Testcontainer; Playwright authoring a JS transformer in a pipeline and watching it run.

---

## Phase 7 — Polish, hardening, lineage, events, docs

- Dataset lineage view (source datastores/connectors → datasets → pipelines → output datasets)
- Pipeline run history UI with per-step durations and row counts
- Pipeline diff / version (semantic diff over `PipelineDefinition`)
- Audit events via `add-audit-event` skill: `dataset.created`, `dataset.refreshed`, `dataset.refresh.failed`, `pipeline.run.started`, `pipeline.run.completed`, `pipeline.run.failed`, `datastore.created`, `dataconnector.tested`
- EventCatalog entries via `EventCatalogEndpoints.cs`
- Project skill: write a new `add-data-connector` skill that codifies the registration path for plugin authors
- `audit-security` skill run before Phase 1 ships (secrets), and again before Phase 6 ships (sandbox escape surface)

---

## Default shipped Transformers & Analyzers (recap)

| Transformers | Analyzers |
|---|---|
| csv-to-json · json-to-csv · xlsx-to-csv | summary-statistics · null-rate · distinct-count |
| json-flatten · column-rename-cast · filter-rows | top-k · correlation-matrix |
| dedupe · join-two-inputs · pivot · unpivot | anomaly-zscore · anomaly-iqr |
| regex-extract · schema-infer | trend-linear-regression · group-by-aggregate |
| null-fill · date-normalize | histogram-bin · k-means-cluster (bounded) |

---

## Risks & gaps to flag during implementation

1. **`autonate_datastores` DB provisioning has no precedent.** Phase 0's `IDatabaseInitializer` abstraction must land cleanly before Phase 1.
2. **Parameterised `FROM` is a real AQL grammar change**, not a side note. Touch parser, validator, suggestion service, chatbot introspection.
3. **`DataFrame` ABI lock-in.** Putting it in `AutoNate.Plugin.Abstractions` makes its shape a versioned contract across plugins. Focused review before Phase 4 ships.
4. **Cross-datastore JOINs forbidden in v1.** Validator must produce a clear error pointing at the offending JOIN. In-memory bounded JOIN is a follow-up.
5. **React Flow bundle size.** Lazy-load the editor route in Phase 5.
6. **Sandbox escape surface.** Re-run `audit-security` after Phase 6 lands.
7. **Secrets storage** for REST/SMB creds, sidecar JetStream creds, and the datastores DB writer password — audit via `audit-security` before Phase 1 ships.

---

## End-to-end verification per phase

**Phase 1.** `make infra-up` → sign in as admin → SPA admin → DataStores → create file datastore → upload files; create SQL datastore → upload CSV → table visible. Create REST DataConnector with a public API → "Test connection" returns 200 sample. Verify `autonate_datastores` DB exists and the per-datastore role can SELECT only its schema (`psql` smoke test). Run xUnit + Playwright `DataStoreAdminTests.cs`.

**Phase 2.** Create a Dataset over the Phase 1 SQL datastore → open AQL playground → `FROM Dataset("sales") WHERE Amount > 100` returns rows. Toggle dataset to Cached + 5-minute refresh → admin Projections page shows the new feed → pause/resume works. Phase-2 permission test in `PermissionGatingTests.cs` passes (dataset grant alone is enough).

**Phase 3.** Save a query → share link → open in private window → results render with the shared user's permissions.

**Phase 4.** xUnit suite green; HelloPlugin's demo transformer registered and visible in `/api/transformers`.

**Phase 5.** Author a pipeline (dataset → filter-rows transformer → summary-statistics analyzer → dataset-sink) → save → run → run-history shows green status + step rows. Schedule a 10-minute cron → wait → run appears.

**Phase 6.** `make infra-up` brings up `services/executor/`. Author a JS transformer that doubles a numeric column → run inside a pipeline → output correct. Author a Python transformer using Pyodide → same. Grant `executeunsafe` to admin → author a full-CPython pandas transformer → runs. Without `executeunsafe`, the unsafe path is rejected.

**Phase 7.** Lineage view renders end-to-end graph; audit events appear in `/admin/config/bus-watcher` for each lifecycle moment.
