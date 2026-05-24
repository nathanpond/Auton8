# Configuration reference

Every projection-framework setting, where it lives in
`appsettings.json`, and what it does.

## `Projections` (framework-wide)

```json
"Projections": {
  "MaxBatchSize": 250,
  "MaxBatchWindow": "00:00:00.500",
  "BaseRetryDelay": "00:00:02",
  "MaxRetryDelay": "00:05:00",
  "MaxAttempts": 20,
  "WorkerEnabled": true
}
```

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxBatchSize` | int | 250 | Max events per `IProjection.ApplyAsync` call. |
| `MaxBatchWindow` | TimeSpan | 500ms | Soft flush window (currently advisory; full enforcement is a v2 enhancement). |
| `BaseRetryDelay` | TimeSpan | 2s | First retry after a failed batch. |
| `MaxRetryDelay` | TimeSpan | 5m | Cap on exponential backoff. |
| `MaxAttempts` | int | 20 | Drop the batch (with an error log) after this many failures. Prevents poison messages from wedging a feed. |
| `WorkerEnabled` | bool | true | When false, `ProjectionWorker` returns immediately without starting any drain loops. Tests set this to false to avoid spinning up dozens of polling feeds across simultaneous test factories. |

## `FlowableCache` (Flowable projections)

```json
"FlowableCache": {
  "ExecutionPollInterval": "00:01:00",
  "TaskPollInterval": "00:01:00",
  "VariablePollInterval": "00:05:00",
  "HistoryPollInterval": "00:01:00",
  "TaskPageSize": 200,
  "HistoryPageSize": 500,
  "VariableInstancesPerTick": 100,
  "ReadThroughFreshness": "00:00:30",
  "CurrentProjectionVersion": 1,
  "RetentionEnabled": true,
  "DefaultRetentionDays": 2555,
  "RetentionSweepInterval": "06:00:00",
  "ColdTier": {
    "Enabled": false,
    "ArchiveSweepInterval": "1.00:00:00",
    "ArchiveAfterDays": 90,
    "Root": "var/projections",
    "MaxRowsPerArchivePass": 500000,
    "MinimumRowAge": "1.00:00:00"
  }
}
```

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ExecutionPollInterval` | TimeSpan | 60s | How often `FlowableExecutionPollingFeed` ticks. |
| `TaskPollInterval` | TimeSpan | 60s | How often `FlowableTaskPollingFeed` ticks. |
| `VariablePollInterval` | TimeSpan | 5m | How often `FlowableVariablePollingFeed` ticks. Longer than executions because per-instance fan-out is expensive. |
| `HistoryPollInterval` | TimeSpan | 60s | How often `FlowableHistoryPollingFeed` ticks. Uses Flowable's global history endpoint, so it's cheap. |
| `TaskPageSize` | int | 200 | Page size for `IFlowableClient.GetRuntimeTasksAsync`. |
| `HistoryPageSize` | int | 500 | Page size for `IFlowableClient.GetHistoricActivityEventsAsync`. |
| `VariableInstancesPerTick` | int | 100 | Cap on number of active instances the variable feed fetches per tick. Instances rotate by `start_time DESC`. |
| `ReadThroughFreshness` | TimeSpan | 30s | `FlowableReadThrough` considers a cached row stale beyond this and refetches from Flowable on detail-endpoint reads. |
| `CurrentProjectionVersion` | int | 1 | Bumping triggers reprojection (substrate ready; not auto-triggered yet). |
| `RetentionEnabled` | bool | true | When false, `WorkflowCacheRetentionService` exits at startup. |
| `DefaultRetentionDays` | int | 2555 | ~7 years. Per-process overrides in `process_retention_config`. |
| `RetentionSweepInterval` | TimeSpan | 6h | How often the retention janitor sweeps. |

### `FlowableCache:ColdTier`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | bool | false | Master switch. Disabled by default; enable only when you've sized your Postgres install and decided to offload aged history. |
| `ArchiveSweepInterval` | TimeSpan | 24h | How often `ColdTierArchiverService` looks for rows to archive. |
| `ArchiveAfterDays` | int | 90 | Rows older than this are eligible for archival. |
| `Root` | string | `var/projections` | Filesystem root for Parquet files. Should be a persistent volume in production. |
| `MaxRowsPerArchivePass` | int | 500_000 | Cap on rows pulled into one Parquet file per pass. Catch-ups roll over to the next sweep. |
| `MinimumRowAge` | TimeSpan | 24h | Safety floor — never archive rows newer than this even if `ArchiveAfterDays` would otherwise allow it. Defends against clock drift. |

## `RecordActivityRollup` (internal aggregate)

```json
"RecordActivityRollup": {
  "PollInterval": "01:00:00",
  "RecentDayWindow": 7,
  "CurrentProjectionVersion": 1
}
```

| Key | Type | Default | Meaning |
|---|---|---|---|
| `PollInterval` | TimeSpan | 1h | How often the rollup feed recomputes. |
| `RecentDayWindow` | int | 7 | Only recompute the last N days on each tick. Older days are stable (records aren't normally backdated) so recomputing them every hour is wasted work — they're picked up only by a manual rebuild. |
| `CurrentProjectionVersion` | int | 1 | Bump to trigger reprojection. |

## Test-only overrides

`AutoNateWebApplicationFactory` (in `tests/AutoNate.Web.Tests/`) applies
these by default so xUnit parallelism stays sane:

```json
"Projections:WorkerEnabled": "false"
"FlowableCache:RetentionEnabled": "false"
```

Tests that want to exercise the drain loop or the retention janitor opt
in by passing the opposite value via `extraConfig`:

```csharp
await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
    extraConfig: new Dictionary<string, string?>
    {
        ["FlowableCache:RetentionEnabled"] = "true",
        ["FlowableCache:DefaultRetentionDays"] = "7"
    });
```

## Per-process retention overrides

Not in `appsettings.json` — in the database:

```sql
CREATE TABLE process_retention_config (
    process_definition_key TEXT PRIMARY KEY,
    retain_days INT NOT NULL CHECK (retain_days > 0),
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NULL
);
```

Insert a row per process key that should diverge from
`DefaultRetentionDays`. The janitor reads this on every sweep — no
restart needed.
