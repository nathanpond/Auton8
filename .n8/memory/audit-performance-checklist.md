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
**J. EF warnings** — `EF.Property`, deep `Include` chains, client-eval warnings in logs.

Verification: trace every N+1 back to a request handler; "missing index" on a 10-row table is not a finding; "unbounded" only if a list view actually calls it.
