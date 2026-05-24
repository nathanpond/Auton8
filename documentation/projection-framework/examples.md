# Examples

The framework ships with five projections plus two AQL-only entities
in tree. They cover most of the patterns you'd want to copy.

Numbered sections 1–6 are projections — they write into a cache table.
The `Flows` and `WorkflowAnalytics` entities are read-only — they
compose data from caches the other projections already populate.

## 1. Flowable execution cache

**Source**: Flowable REST API (external system, polling).
**Pattern**: standard per-row upsert + auth tags + dedicated selector
compiler.

Files:
- Projection: `FlowableExecutionProjection.cs`
- Feed: `FlowableExecutionPollingFeed.cs`
- AQL entity: `WorkflowExecutionsQueryEntity.cs`
- Selector compiler: `WorkflowExecutionCacheSelectorCompiler.cs`
- Cache table: `workflow_execution_cache`

The polling feed calls `IFlowableClient.GetWorkflowExecutionsAsync` on a
60-second interval (configurable) and emits one `ChangeEvent<WorkflowExecutionSummary>`
per instance. The projection translates each summary into a
`workflow_execution_cache` row keyed by `flowable_instance_id`, with
auth tags extracted into a JSONB column:

```csharp
var authTags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
{
    ["processkey"] = processKey,
    ["definitionkey"] = src.ProcessDefinitionId,
    ["startedby"] = src.StartUserId,
    ["status"] = status
};
```

These tags map to grant predicates like `[startedby=$me]` and
`[processkey=approval]`. `WorkflowExecutionCacheSelectorCompiler.Compile`
turns them into LINQ predicates that compile to SQL.

**This is the template to copy** for any external-system polling
projection. It demonstrates: idempotent upsert with explicit dedup
loop, auth tag extraction, status normalization, and the standard
shape for an AQL entity built on top of a cache table.

## 2. Flowable task cache

Mirror of the execution cache, one level deeper. Same pattern,
different columns (assignee, candidate users/groups arrays, etc.).

Files:
- Projection: `FlowableTaskProjection.cs`
- Feed: `FlowableTaskPollingFeed.cs` (pages through `GetRuntimeTasksAsync`)
- AQL entity: `WorkflowTasksQueryEntity.cs`
- Selector compiler: `WorkflowTaskCacheSelectorCompiler.cs` (supports
  `candidategroup` / `candidateuser` membership predicates via
  `text[].Contains` → PG `ANY()`)

## 3. Flowable variable cache

**Pattern**: per-parent-instance snapshot, transactional delete + insert.

Files:
- Source type: `FlowableInstanceVariables.cs` (full snapshot per instance)
- Projection: `FlowableVariableProjection.cs`
- Feed: `FlowableVariablePollingFeed.cs` (per-active-instance fan-out)
- AQL entity: `WorkflowVariablesQueryEntity.cs`

The projection takes a complete snapshot (dictionary of all variables
for one instance), opens a transaction, deletes existing rows for that
instance, then inserts the fresh set. This handles variable *deletions*
in Flowable correctly: a variable that was present in last tick's
snapshot but absent in this one disappears from the cache.

**Auth pattern**: variables don't have their own selector compiler.
The AQL entity filters the *parent* (`workflow_execution_cache`) for
visible instance IDs, then restricts variable rows to
`WHERE flowable_instance_id IN (visible_ids)`. This is the "parent
auth inheritance" pattern — use it whenever the child rows don't carry
their own permission tags.

## 4. Flowable history event log

**Pattern**: append-only with deterministic event IDs for replay
idempotency. Watermark-driven incremental polling.

Files:
- Source type: `FlowableHistoricActivityEvent` (in `Models/FlowableModels.cs`)
- Projection: `FlowableHistoryProjection.cs` — uses `ON CONFLICT DO NOTHING`
  (not `DO UPDATE`) because rows are immutable once written
- Feed: `FlowableHistoryPollingFeed.cs` — reads
  `IProjectionWatermarkStore`, fetches events after that timestamp,
  advances the watermark on success
- AQL entity: `WorkflowHistoryQueryEntity.cs`

The deterministic event ID is a SHA-256 over `(instance_id, activity_id,
task_id, event_type, time)`. This makes replay safe — re-emitting the
same source event from the polling feed always produces the same row PK,
and `ON CONFLICT DO NOTHING` makes the second insert a no-op:

```csharp
private static string BuildEventId(string instanceId, string? activityId,
                                    string? taskId, string eventType, DateTime time)
{
    var seed = $"{instanceId}|{activityId}|{taskId}|{eventType}|{time:O}";
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    // truncate to 32 hex chars — collision-safe for our row volumes
    return Convert.ToHexStringLower(bytes.AsSpan(0, 16));
}
```

**Copy this pattern** for any append-only event-log cache.

## 5. Record activity rollup (internal aggregate)

**The non-Flowable example.** Proves the framework lifts cleanly off
the Flowable domain.

Files:
- Source type: `RecordActivityRollupSnapshot.cs` (one rollup bucket =
  `(record_type_id, day, counts)`)
- Projection: `RecordActivityRollupProjection.cs`
- Feed: `RecordActivityRollupFeed.cs` (hourly recompute over the last
  N days via a single `FULL OUTER JOIN` SQL aggregate)
- AQL entity: `RecordActivityRollupQueryEntity.cs` (auth-gated via
  `RecordType:View` so users only see rollups for record types they
  can access)

The feed reads from the local `records` table — no external system
involved. It uses `IDbContextFactory<AutoNateDbContext>` directly to
run a `FULL OUTER JOIN` of three sub-aggregates (created / updated /
archived counts grouped by `record_type_id, date_trunc('day',
updated_at_utc)`) and emits one `ChangeEvent` per bucket.

**Copy this pattern** for any expensive internal aggregate you'd
otherwise recompute per dashboard request.

## 6. Flows (composite read entity over multiple caches)

**Pattern**: AQL entity that reads from several caches to compose a
single user-facing view, with derived columns and a parameterized row
function.

File: `WorkflowsQueryEntity` (raw debug view of executions, lower-level)
and `FlowsQueryEntity` (user-facing view) both live in
`Services/Query/Entities/`. The Flows entity:

- **Doesn't own a projection or a feed** — it reads existing caches.
- Auth-filters `workflow_execution_cache` through
  `IAuthorizer.FilterQueryAsync<WorkflowExecutionCache>` against
  `EntityKinds.WorkflowExecution` — the same grants gate it that gate
  `WorkflowExecutions`.
- Bulk-joins to `workflow_models.name` for `FlowName` (one query keyed
  by distinct process keys in the result, dictionary lookup per row).
- Bulk-loads `workflow_task_cache` for current open tasks **only if**
  `CURRENTSTEP()` is referenced in `COLUMNS` or `ORDER BY`.
- Bulk-loads `workflow_execution_errors` for the visible instance set
  to drive the **derived `Status` column** (see next section).
- Supports full `GROUP BY` + aggregates (`COUNT`, `MIN`, `MAX`, `AVG`,
  `MEDIAN`) plus the standard `WHERE` / `ORDER BY` / `LIMIT` shape.

### The derived `Status` column

The cache stores normalized lowercase statuses (`active`, `completed`,
`cancelled`, `suspended`, `terminated`). The Flows entity surfaces a
display label with a **precedence overlay** that matches what
`ExecutionEndpoints` does for the executions list page:

```
Cancelled  >  Errored  >  In-progress / Completed / Suspended / Terminated
```

- **Cancelled** wins over Errored. Operator intent supersedes a stale
  failure — a process the operator cancelled stays `Cancelled` even if
  it had errored before being cancelled.
- **Errored** is a derived overlay computed from
  `workflow_execution_errors`. If any error row exists for the
  instance and the base status isn't `Cancelled`, Status flips from
  the base label (`In-progress` / `Completed` / …) to `Errored`. A
  process with a failed job is no longer healthy even if Flowable
  still reports it as `active`.
- **In-progress / Completed / Suspended / Terminated** is the base
  label, mapped from the cache's lowercase form.

The errors lookup is one bulk `SELECT DISTINCT process_instance_id
FROM workflow_execution_errors WHERE process_instance_id IN (...)`
materialized into a HashSet. Per-row dictionary lookup; constant
overhead per query.

### `Status` WHERE input is normalized

Users can write any of these and they all match the same rows:

| Input literal | Matches the display status |
|---|---|
| `"In-progress"`, `"InProgress"`, `"Active"`, `"Running"` | `In-progress` |
| `"Completed"`, `"Complete"`, `"Done"`, `"Finished"` | `Completed` |
| `"Cancelled"`, `"Canceled"` | `Cancelled` |
| `"Suspended"`, `"Paused"` | `Suspended` |
| `"Terminated"` | `Terminated` |
| `"Errored"`, `"Error"`, `"Failed"` | `Errored` |

Case-insensitive. Anything not in the table passes through unchanged
— the comparison will likely fail, which is the right signal (the
user typed something the entity doesn't understand).

### The `CURRENTSTEP(arg)` parameterized row function

Row functions normally take no argument (`NUMNODES()`,
`COUNTCHILDREN()`). `CURRENTSTEP` takes one — the name of the task
field you want:

```
COLUMNS(FlowName, CURRENTSTEP(Name) AS Step,
                  CURRENTSTEP(Assignee) AS AssignedTo)
```

Args accepted: `Name`, `Assignee`, `ActivityId`, `TaskId`, `DueDate`,
`CreatedTime`, `Priority`. All return strings (or `Date` for the
two timestamp args, or `Number` for `Priority`).

Mechanically the AQL parser packs the arg into
`AqlSelectItem.AggregateField` — the same slot that holds the column
name for a true aggregate like `SUM(amount)`. The entity's
`SelectItemToProjection` checks `Entity.RowFunctions.Contains(fn)`
first, and if so dispatches to `EvalRowFunction(fn, arg, row)` rather
than the aggregate path.

The current-task lookup is one bulk query against `workflow_task_cache`
filtered to visible instance IDs and `Status = 'active' AND
CompletedTime IS NULL`. If the instance has multiple open tasks
(parallel gateway branches), the oldest one wins.

### Copy this pattern when…

- You want a user-facing AQL view that's friendlier than the raw cache
  columns (display labels, derived overlays, joined names).
- You need to combine data from 2+ caches into a single AQL entity
  without standing up a new projection.
- You want a parameterized row function (`FN(arg)` syntax, evaluated
  per row, no GROUP required).

### Example queries

```
FROM Flows
WHERE Status = "In-progress"
ORDER BY StartDate
COLUMNS(FlowName, Status,
        CURRENTSTEP(Name) AS Step,
        CURRENTSTEP(Assignee) AS AssignedTo)
```

```
FROM Flows
ORDER BY COUNT()
COLUMNS(Status, COUNT() AS Count)
GROUP(Status)
```

```
FROM Flows
WHERE Status = "Errored" AND ProcessKey = "invoice-approval"
```

## 7. Cold-tier analytics (composite read path)

Not a projection in itself — an AQL entity that reads from two
sources at query time:

- Hot: `workflow_event_log_cache` (Postgres, via EF)
- Cold: `var/projections/workflow_event_log/*.parquet` (DuckDB)

File: `WorkflowAnalyticsQueryEntity.cs` + `DuckDbAnalyticsRunner.cs`.

For each query:
1. Filter visible instance IDs from the hot cache.
2. Pull matching hot events from Postgres (auth-restricted).
3. Open a fresh per-request DuckDB in-memory connection.
4. Load hot rows into a staging table via `DuckDBAppender`.
5. Register a view over the cold Parquet glob.
6. Build a `UNION ALL` query with the user's `WHERE` / `GROUP BY`
   / aggregate, parameterized with positional `?` placeholders.
7. Execute, materialize, return.

This is the pattern when you need analytical aggregates spanning more
history than Postgres can comfortably store, but want the answer in
one logical SQL plan. Per-request DuckDB lifecycle keeps it simple
(no shared state, no connection pool).

## Tour: how an AQL query becomes cache rows

Walk through `FROM WorkflowExecutions WHERE startedby = $me LIMIT 50`:

1. **Parser** builds an `AqlQuery` AST: entity = `"WorkflowExecutions"`,
   where = `AqlCompare("startedby", "=", "$me")`, limit = 50.
2. **Validator** consults `WorkflowExecutionsQueryEntity.StaticSchema`
   to verify `startedby` is a real column.
3. **Executor** calls `entity.PrepareAsync(query)`, gets back a
   `WorkflowExecutionsPreparedQuery`.
4. `ExecuteAsync(actor, hardCap)` runs:
   a. Opens a fresh `AutoNateDbContext`.
   b. Builds `db.WorkflowExecutionCache.AsNoTracking().AsQueryable()`.
   c. Calls `IAuthorizer.FilterQueryAsync<WorkflowExecutionCache>(db,
      actor, EntityKinds.WorkflowExecution, Actions.View, queryable)`.
   d. The authorizer loads the actor's grants for that kind/action,
      compiles each grant's selector via
      `WorkflowExecutionCacheSelectorCompiler`, and AND-combines into a
      single `WHERE` clause.
   e. EF translates `IQueryable<WorkflowExecutionCache>` into a single
      Postgres `SELECT ... FROM workflow_execution_cache WHERE ...`.
5. The entity applies the user's `WHERE startedby = $me` (in-memory
   evaluator for now — translatable to SQL is a follow-up
   optimization), `ORDER BY`, and `LIMIT 50`.
6. Returns a `QueryResult` to the AQL executor, which forwards to the
   HTTP endpoint.

Total round trips: one Postgres query for the cache scan + however many
the authorizer needs to load the grant graph (cached per request).
Zero round trips to Flowable.

## Tour: how a Flowable event becomes a cache row

Push (NATS / event-bridge) is not wired yet; the current path is poll.
Walk through a new process instance:

1. Operator starts a process via the API → `FlowableClient.StartProcessInstanceAsync`.
2. Flowable creates the runtime instance.
3. *(After at most 60 seconds)* the next `FlowableExecutionPollingFeed`
   tick runs:
   a. Calls `_flowable.GetWorkflowExecutionsAsync(ct)`.
   b. Gets back a list of `WorkflowExecutionSummary` including the new
      instance.
   c. For each, calls `EmitAsync(new ChangeEvent<...>(Upsert, id, summary, ...))`
      into its channel.
4. `ProjectionWorker`'s drain loop pulls events off the channel, batches
   them, and calls `FlowableExecutionProjection.ApplyAsync(batch, db, ct)`.
5. The projection's `ON CONFLICT DO UPDATE` UPSERT writes the new row.
   `_health.RecordApply(...)` updates the health snapshot.
6. The next AQL query against `WorkflowExecutions` sees the new row.

If the polling tick happens to land while another tick is mid-flight,
both run concurrently — the channel buffers events safely. If Flowable
is down for one tick, the channel stays empty and the apply path is a
no-op; the next successful tick catches up.
