# Operations

How to run and observe the projection framework in a deployed environment.

## Admin endpoints

All admin endpoints are gated by `SiteConfig:Edit` (the platform-config
permission). Authenticated users without that grant get 403. They live
under `/api/admin/projections/` and the SPA equivalent is at
`/admin/config/projections`.

### `GET /api/admin/projections/`

Returns an array of `ProjectionHealthSnapshot` — one entry per
registered projection (host + plugin).

Each snapshot:

```json
{
  "name": "flowable.workflow_execution_cache",
  "version": 1,
  "sourceType": "AutoNate.Web.Models.WorkflowExecutionSummary",
  "paused": false,
  "eventsAppliedTotal": 4823,
  "eventsAppliedSinceStart": 4823,
  "applyFailuresTotal": 0,
  "lastAppliedAtUtc": "2026-05-23T15:42:11.123Z",
  "lastFailureAtUtc": null,
  "lastFailureMessage": null,
  "feeds": [
    {
      "feedName": "flowable.exec.poll",
      "eventsObservedTotal": 4823,
      "lastEventObservedAtUtc": "2026-05-23T15:42:11.123Z",
      "watermarkUtc": null
    }
  ]
}
```

Counters reset on process restart (`eventsAppliedSinceStart`) but
`eventsAppliedTotal` is also reset because the framework keeps it in
memory — multi-process deployments would need a shared store to make
the "total" meaningful across restarts. For long-term metrics, scrape
the Prometheus counters instead.

### `GET /api/admin/projections/{name}`

Single snapshot for a named projection. 404 if unknown.

### `POST /api/admin/projections/{name}/pause`

Flips the in-memory pause flag. The worker honors it on the next batch.
Already-buffered events in a feed's channel may still apply (they're
in flight at the time of the call); the pause takes full effect on the
next channel read.

Pause is per-instance — in a multi-instance deployment behind a load
balancer, you'd need to hit the endpoint on every instance, or wait for
cross-instance signaling (planned).

### `POST /api/admin/projections/{name}/resume`

Clears the pause flag. Next batch flushes normally.

### `POST /api/admin/projections/{name}/rebuild`

Runs `BackfillRunner.RunAsync(name)`. Requires that an
`IProjectionBackfillSource<TSource>` be registered for the projection's
source type. If no backfill source is registered, returns
`400 Bad Request` with the message `No IProjectionBackfillSource<...>
registered for projection '{name}'.`

The Flowable projections don't ship a backfill source yet — historical
data comes in via the live polling feeds as they page through. For a
plugin or internal-aggregate projection, register an
`IProjectionBackfillSource<TSource>` that re-enumerates everything from
the source of truth.

### `POST /api/admin/projections/feeds/{feedName}/reset-watermark`

Deletes the feed's row from `projection_watermarks`. On its next tick,
the feed will page from the beginning (or whatever the feed's "no
watermark" behavior is — typically equivalent to "since the dawn of
time"). Use when a feed has gone stuck on a bad watermark, or after a
manual data-fix and you want to re-process recent history.

## SPA page

`/admin/config/projections` renders the same data as the GET endpoint,
auto-refreshing every 5 seconds. Each row shows:

- Projection name (Code-styled).
- Version (e.g. `v1`).
- Running / Paused state badge.
- Total events applied.
- Failure count (red if > 0).
- Last apply timestamp (or "never").
- Number of observed feeds (tooltip with per-feed counts).
- Action buttons: Pause/Resume, Rebuild.

The page is not linked from the admin sidebar by default. Add a menu
item from `/admin/config/pages-menus` pointing at template
`configProjections` to surface it. Direct navigation always works.

## Prometheus / OpenTelemetry

The framework registers a `Meter` named `AutoNate.Projections` with
three instruments:

- **`projection.events_applied_total`** (counter, tags: `projection`,
  `feed`) — every batch that successfully applies contributes its
  `eventCount`.
- **`projection.batch_failures_total`** (counter, tags: `projection`,
  `feed`) — increments once per batch that fails. The framework retries
  with backoff, so one batch can contribute many failures before either
  succeeding or hitting `MaxAttempts`.
- **`projection.lag_seconds`** (observable gauge, tag: `projection`) —
  seconds since the projection's last successful apply. Useful for
  alerting (e.g. page if any projection's lag exceeds 5 minutes).
- **`projection.reconcile_drift_total`** (counter, tags: `projection`,
  `kind`) — reserved for a future reconciliation pass that diffs the
  cache against the source of truth and increments when the two
  diverge. Not yet wired by any built-in projection.

These are picked up by whatever OpenTelemetry exporter the rest of
AutoNate uses.

## Retention janitor

`WorkflowCacheRetentionService` runs on `RetentionSweepInterval`
(default 6 hours). On each sweep:

1. Reads `process_retention_config` for per-process overrides
   (`(process_definition_key, retain_days)` pairs).
2. Iterates distinct `process_definition_key` values found in
   `workflow_execution_cache`.
3. For each key, computes the cutoff = `now - retain_days` (overrides
   first, falling back to `FlowableCache:DefaultRetentionDays` = 2555).
4. Deletes from `workflow_event_log_cache`, `workflow_variable_cache`,
   `workflow_task_cache`, and `workflow_execution_cache` in that order
   (events first to respect the implicit dependency graph).

To set a custom retention for a process:

```sql
INSERT INTO process_retention_config (
    process_definition_key, retain_days, updated_at_utc, updated_by)
VALUES (
    'invoice-approval', 90, NOW(), '00000000-0000-0000-0000-000000000000')
ON CONFLICT (process_definition_key) DO UPDATE
  SET retain_days = EXCLUDED.retain_days,
      updated_at_utc = NOW();
```

The janitor will pick this up on its next sweep. There is no admin UI
for this yet — manual SQL is the workflow.

Disable entirely via `FlowableCache:RetentionEnabled = false`.

## Cold tier

The cold tier moves aged `workflow_event_log_cache` rows out of Postgres
and into Parquet files on disk. Reads of cold data go through DuckDB
(in-process, no extra service to run).

### Enabling

```json
"FlowableCache": {
  "ColdTier": {
    "Enabled": true,
    "ArchiveSweepInterval": "1.00:00:00",
    "ArchiveAfterDays": 90,
    "Root": "/var/lib/autonate/projections",
    "MaxRowsPerArchivePass": 500000,
    "MinimumRowAge": "1.00:00:00"
  }
}
```

`Root` should point at persistent storage. The default
`var/projections` is relative to the app's working directory — fine for
dev, not for production. In a container, mount a volume.

### File layout

```
{Root}/workflow_event_log/{YYYY}-{MM}.{timestamp}.parquet
```

Multiple files per month are normal — each archival sweep writes a
new file with a unique timestamp suffix. DuckDB's `read_parquet` glob
picks them all up; the analytics entity dedupes via `event_id` if the
same event lands in two files (which can happen if a sweep crashed
between Parquet write and Postgres delete).

A future compaction step would rewrite multi-file months into single
files. Not implemented; not yet needed.

### Crash safety

Per archival pass:
1. Write the Parquet file (DuckDB `COPY` is atomic per file).
2. Build the list of event_ids actually written.
3. Delete those rows from Postgres in chunked `DELETE` statements.

If a crash happens between step 1 and step 3, the rows exist in both
tiers. The next archival sweep re-archives them to a new Parquet file
with a different timestamp suffix. The analytics reader's UNION ALL
sees them in both files but dedupes by `event_id`.

### Disabling

Set `FlowableCache:ColdTier:Enabled = false`. Existing Parquet files on
disk remain queryable by the analytics entity; just no new archival
happens.

### Monitoring cold tier health

There's no dedicated dashboard yet. Check:
- Disk usage of `{Root}/workflow_event_log/` — grows monotonically.
- Postgres `workflow_event_log_cache` row count — should stabilize
  around 90 days of recent activity once the archiver catches up.
- Service logs: `Cold-tier archiver wrote N rows across M month(s) into ...`
  on each successful sweep.

## Tuning the worker

Most installs never need to touch these, but they exist for the rare
case of a hot, high-volume projection:

| Setting | Default | Notes |
|---|---|---|
| `Projections:MaxBatchSize` | 250 | Bigger = fewer transactions but higher per-event latency. |
| `Projections:MaxBatchWindow` | 500ms | Flush even if batch isn't full after this long. |
| `Projections:BaseRetryDelay` | 2s | First retry after a failure. |
| `Projections:MaxRetryDelay` | 5m | Backoff cap. |
| `Projections:MaxAttempts` | 20 | After this many failures, the batch is logged + dropped. |
| `Projections:WorkerEnabled` | true | Set to false in tests to silence the loops. |

## Troubleshooting

### "Cache is empty / nothing's happening"

Check `/api/admin/projections/` — does the projection appear at all?
- **No**: the projection didn't register. Verify the `AddProjection<>`
  call in `Program.cs` ran.
- **Yes, `paused: true`**: someone hit pause. Resume it.
- **Yes, but `lastAppliedAtUtc` is null**: no feed has emitted yet.
  Check feed registration; check the source system is reachable.

### "Failures are spiking"

Inspect `lastFailureMessage` in the snapshot — the framework records
the most recent exception text. Common causes:

- Source-system unavailable (Flowable down, plugin DB connection lost).
  Wait for it to come back; the next retry will succeed.
- Schema drift between source and cache (you added a field to the
  projection but forgot to migrate the table). Add the column;
  the next apply will succeed.
- Permission grants changed mid-flight and the worker is now writing
  rows that violate a constraint. Rare; usually requires a manual fix.

### "AQL query returns no rows but I know the data is there"

The selector / parent-auth filter denied access. Try the same query as
an admin user — if it works, an `EntityKinds.XYZ:View` grant is missing
for the user.

### "A feed is paged through everything every tick"

Watermark store isn't being updated. Either:
- The feed forgot to call `IProjectionWatermarkStore.SetAsync` after
  advancing.
- The watermark row was deleted (look for a recent
  `POST /api/admin/projections/feeds/{name}/reset-watermark`).
