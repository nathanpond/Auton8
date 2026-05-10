---
name: audit-performance
description: Codebase-wide performance + scaling audit for AutoNate. Looks for N+1 queries, load-all-then-filter patterns, missing/unused indexes, per-request DB calls that should be cached, sync-over-async, unbounded materialization, and threadpool starvation. Weights findings by hot-path frequency. Invoked by `/audit performance`; can also be invoked directly.
---

# Performance audit (whole codebase)

A focused performance/scaling pass. The bias is toward issues that look fine at small scale but bite under realistic growth (more rows, more groups, more concurrent users).

**Scope**: every project under `src/` and `plugins/`. Hot endpoints get extra scrutiny.

## Hot-path inventory (weight findings here higher)

These are called on every authenticated SPA mount, navigation, or poll — overheads here amplify with traffic:

- `GET /api/auth/me` — every `useMe` consumer; React Query refetches on focus.
- `GET /api/notifications/unread-count` — bell icon poll (already coalesced; verify it stays that way).
- `GET /api/pages/registry` (`ListPagesAsync`) — SPA mount.
- `GET /api/pages/by-path/{path}` (`GetPageByPathAsync`) — every dynamic-page navigation.
- `GET /api/menus/{key}/tree` — sidebar render on every navigation.
- `GET /api/permission-grants/check` (`/api/auth/check`) — batched per-row gating on list pages.
- `GET /api/event-catalog` — Events admin page; rare, but materializes a lot.
- Any endpoint the agent loop hits per turn.

When you grow this list, add it here so the next audit pass weights it correctly. (A separate `hot-paths.md` would be more durable; for now, this skill is the source of truth.)

## Strategy

Parallel `Explore` agents, one per pattern. After each agent reports, verify findings against the live code — agents grep, but the planner needs the surrounding context to be sure a pattern is actually hit on the request thread (vs. inside a startup-only `IHostedService`).

## Patterns to detect

### A. N+1 (loop-around-await DB calls)
- `foreach (... in collection) { await store.SomethingAsync(...); }` inside a request handler or hot service.
- Test: grep `foreach.*\n.*await.*Async` and inspect each hit. Most "N+1" findings are inside startup migrations or background workers — skip those; they don't matter.
- Remediation pattern: add a batch method to the store interface (`ListForXsAsync(IReadOnlyCollection<X> ids)`) that translates to `WHERE col = ANY(@ids)`. See `IRoleAssignmentStore.ListForPrincipalsAsync` for the canonical example in this codebase.

### B. Load-all-then-filter
- `ToListAsync()` followed by `.Where(...)` in C#. The `.Where` belongs in SQL.
- Especially bad on tables that grow without bound: `menu_items` (used by `ListPagesAsync` and `GetPageByPathAsync`), `notifications`, `audit_outbox`, `record_events`, `agent_messages`.
- Remediation: push the predicate into the EF query, or use `FromSqlInterpolated` for JSONB extractions that EF's translator can't express idiomatically.

### C. Missing or unused indexes
- Cross-check every `WHERE` predicate against `CREATE INDEX` statements in `Persistence/DatabaseSchemaInitializer.cs`. Predicates without a covering index that run on the request path are findings.
- Inverse check: indexes that no query references (the `ix_menu_items_page_path` partial index was unused for months before we noticed). Drop them — they cost on every write.
- Special attention to JSONB extractions (`config->>'path'`). EF won't generate a query that uses these unless the index is set up AND the query uses the right operator.

### D. Per-request fetches of slow-changing data
- Reading the same nearly-static data from DB on every request. Examples: role catalog, menu tree, page templates, plugin metadata, record-type schema.
- Remediation pattern: a singleton snapshot cache with a sliding TTL + explicit invalidation from the matching store mutations. See `RecordTypeShortCodeCache`, `AgentModelCatalog`, and `PageRegistrySnapshotCache` for the canonical shape.

### E. Sync-over-async
- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on a `Task` that wasn't already completed at the call site. (Even after `await Task.WhenAll(t1, t2)`, calling `.Result` on `t1` is a code-review-failure pattern even though it doesn't deadlock today — see the prior `FlowableClient` cleanup.)
- `async void` methods outside event handlers.
- `Microsoft.VisualStudio.Threading.Analyzers` would catch most of these at compile time; flag any added since the analyzer was wired (or note the analyzer isn't wired yet, as a separate finding).

### F. Unbounded materialization
- Endpoints that don't paginate or cap result counts: list endpoints without `take`/`limit`, DTO mappers that walk an unbounded collection, audit/log queries without a date filter.
- Especially on tables that grow: `audit_outbox`, `record_events`, `agent_messages`, `notifications`, `system_issues`.
- Remediation: a hard cap (e.g., `Math.Clamp(take ?? 100, 1, 500)` — see `SystemIssueEndpoints`).

### G. Threadpool starvation
- `Task.Run(...)` inside a request handler. Bursts spawn unbounded background tasks that compete with the request thread pool. The canonical remediation is a coalescing wake-signal on a singleton `IHostedService` (see `PeriodicIssueDetector.RequestImmediateScan`).
- Sync I/O on the request path (`File.ReadAllBytes`, `HttpClient.Send` non-async overload, blocking `WaitOne`).

### H. Per-request authorization fan-out
- Endpoints that walk a list and call `IAuthorizer.AuthorizeAsync` per row inside a tight loop. Each call is parameterized + cached per-request (see `IsAllowedAsync`'s `Dictionary<string, bool>`), but the cache rebuild + the underlying selector evaluation isn't free if the list is large.
- Remediation pattern: switch to `BuildRecordSqlFilter` or `FilterQueryAsync` so the authorizer pushes the predicate into SQL once.

### I. SPA-side amplifiers
- React Query cache config for hot endpoints. `staleTime: 0` with `refetchOnWindowFocus: true` means every tab focus hits the server again. Most `useX` hooks should have a `staleTime` of at least a few seconds for read-mostly data.
- Polling intervals tighter than 30s without coalescing on the server side.

### J. EF Core warnings worth checking
- `EF.Property(...)` calls that should be expression-tree.
- `Include(...)` chains that materialize too much (look for `.Include(x => x.Children).Include(x => x.Tags)` etc.).
- Client-side evaluation warnings — these typically show in the EF logs.

## Verification before reporting

- For "N+1" findings: trace the call path back to a request handler (vs. a startup or background-only path). A loop in `DatabaseSchemaInitializer` is fine; the same loop in `EfCoreXyzStore.GetAsync` is not.
- For "missing index" findings: confirm by running `EXPLAIN ANALYZE` mentally. A WHERE on a 10-row enum table doesn't need an index even if the predicate looks scary.
- For "unbounded materialization" findings: check whether the matching SPA hook actually hits the endpoint with a list view. A POST that always operates on a single record id isn't unbounded.

## Output

### 1. Hot-path findings (highest priority)
Findings on the hot-path-inventory endpoints, grouped by endpoint. Each entry:

```
**[H/M/L] /api/route — short title** (file/path.cs:NN)
- What: one-line description
- Cost shape: e.g. "O(N) per call where N = distinct user permissions" — make the scaling explicit
- Fix: one-line remediation; reference an existing pattern if one applies (e.g. "snapshot cache like PageRegistrySnapshotCache")
```

### 2. General findings
Same shape, grouped by concern (A–J above). Cap at top 10.

### 3. What I checked and found clean
Short bulleted list per concern so the user can see the surface that was actually examined.

### 4. Recommendations beyond findings
- Tools/analyzers that would catch the pattern automatically going forward (e.g. wire `Microsoft.VisualStudio.Threading.Analyzers` for sync-over-async). One-time setup, durable signal.
- Hot-path inventory updates: any endpoint discovered during the audit that should be added to the inventory at the top of this skill.

### 5. Out of scope
- PR-diff review → `/review`.
- Security issues → `/audit security`.
- Load testing / actual profiling → not something this skill can do; needs runtime instrumentation.
