# Performance audit checklist + hot-path inventory (AutoNate-specific)

Harvested from `.claude/skills/audit-performance` on 2026-08-30. Weight findings on the hot paths highest; grow the inventory when new per-mount/per-navigation endpoints appear.

## Hot-path inventory (called on every authenticated SPA mount, navigation, or poll)
- `GET /api/auth/me` — every `useMe` consumer; React Query refetches on focus.
- `GET /api/notifications/unread-count` — bell poll (coalesced server-side; keep it that way).
- `GET /api/pages/registry` (`ListPagesAsync`) — SPA mount.
- `GET /api/pages/by-path/{path}` (`GetPageByPathAsync`) — every dynamic-page navigation.
- `GET /api/menus/{key}/tree` — sidebar on every navigation.
- `GET /api/permission-grants/check` (`/api/auth/check`) — batched per-row gating on list pages.
- `GET /api/event-catalog` — rare but materializes a lot.
- Anything the agent loop hits per turn.

## Patterns
**A. N+1** — `foreach { await store.XAsync }` on the request path (startup migrations / background workers don't count). Fix: batch method on the store (`ListForXsAsync(ids)` → `WHERE col = ANY(@ids)`); canonical `IRoleAssignmentStore.ListForPrincipalsAsync`.
**B. Load-all-then-filter** — `ToListAsync()` then `.Where`; worst on unbounded tables `menu_items`, `notifications`, `audit_outbox`, `record_events`, `agent_messages`. Push into EF or `FromSqlInterpolated` for JSONB.
**C. Indexes** — cross-check request-path `WHERE` predicates vs. `CREATE INDEX` in `Persistence/DatabaseSchemaInitializer.cs`; inverse: unused indexes cost every write (`ix_menu_items_page_path` sat unused for months). JSONB extractions (`config->>'path'`) need index + matching operator.
**D. Per-request reads of slow-changing data** (role catalog, menu tree, page templates, plugin metadata, record-type schema). Fix: singleton snapshot cache with sliding TTL + explicit invalidation from store mutations — canonical `RecordTypeShortCodeCache`, `AgentModelCatalog`, `PageRegistrySnapshotCache`.
**E. Sync-over-async** — `.Result` / `.Wait()` / `GetAwaiter().GetResult()`, `async void` outside handlers (the old `FlowableClient.Result` cleanup). VSTHRD analyzers are wired; anything new is a finding.
**F. Unbounded materialization** — list endpoints without a cap; fix `Math.Clamp(take ?? 100, 1, 500)` as in `SystemIssueEndpoints`.
**G. Threadpool starvation** — `Task.Run` in request handlers; fix with a coalescing wake-signal on a singleton `IHostedService` (`PeriodicIssueDetector.RequestImmediateScan`). Sync I/O on request path.
**H. Per-row `IAuthorizer.AuthorizeAsync` loops** — per-request cached (`IsAllowedAsync` dictionary) but selector evaluation isn't free; switch to `BuildRecordSqlFilter` / `FilterQueryAsync` to push the predicate into SQL once.
**I. SPA amplifiers** — `staleTime: 0` + `refetchOnWindowFocus` on hot hooks; polling < 30s without server coalescing.
**K. Write-through gaps — a write that never reaches the projection it is read back from.** The inverse of D, and the more dangerous half: D is a cache that goes stale slowly, this is one that is *wrong the instant the caller looks*. Find it by pairing each **write** endpoint with the **read** that serves the same data, and asking whether the caller's next read reflects what they just did. Three shipped in M5 alone: completing a task never touched `workflow_task_cache` (#604), nor did any task the engine created on its own, and starting/cancelling/deleting an execution never touched `workflow_execution_cache` (#609). Each was a minute of staleness on the one surface somebody was watching for change, and together they were **24 of 493** full-local specs. Three notes for whoever sweeps this:
- **The poll interval is not the fix.** Shortening it hides the defect and costs a round trip per tick. The write should project; the poll stays the safety net.
- **The right mechanism differs per write, and guessing costs data.** In #609, start reuses the read-through, cancel must *not* (a cancelled instance is gone from the runtime, so a read-back finds nothing and DELETES the row the list must keep showing), and delete removes it. One mechanism for all three would have silently dropped every cancelled execution.
- **Never infer absence.** Marking something finished because it stopped appearing invents a timestamp nobody measured (#586). Mark from the positive fact, or filter the response and leave the row alone.

**J. EF warnings** — `EF.Property`, deep `Include` chains, client-eval warnings in logs.

Verification: trace every N+1 back to a request handler; "missing index" on a 10-row table is not a finding; "unbounded" only if a list view actually calls it.
