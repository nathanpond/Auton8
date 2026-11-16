# Hot-path inventory (performance audits weight these highest)

Endpoints hit on every authenticated SPA mount, navigation, poll, or agent turn. Grow this file each `/n8-audit performance` run. Last updated 2026-11-16 (second audit; first was 2026-08-30).

Round-trip counts marked **measured** come from a scratch instance on 2026-11-16 (fresh database, 40 records, a limited user holding 22 grants, SQL command logging on). They are counts, not latencies: every measured request had a median under 10 ms on that tiny database. Re-measure before trusting them after a change to the authorizer or the audit publisher.

## Cross-cutting costs (these multiply everything below)
- **Every audited read writes an `audit_outbox` row inline**, then awaits plugin hooks, before responding. Only `/api/auth/me` and `/api/notifications/unread-count` go through `ViewEventCoalescer`. #801.
- **The authorizer re-derives the actor on every request** (2 queries) and loads grants once per (kind, action), although the query filters by action only. Measured on `POST /api/auth/check`: 4 round trips for one kind, 7 for four, 15 for twelve. #802.
- **Super-admins skip instance checks**, so measuring as the bootstrap admin under-counts. Measure as a limited user.

## Per mount (the shell)
The shell requests these in parallel on every mount: `/api/appearance`, `/api/auth/me`, `/api/site-settings`, three public menus, `/api/record-types`, `/api/page-templates`, `/api/notifications?limit=10`, and two WebSockets (`/ws/bus-watcher`, `/ws/agent-model-default`). `refetchOnWindowFocus` is false globally, so nothing here refetches on focus.

- `GET /api/auth/me` — 2 round trips (measured); audit event coalesced. Clean.
- `GET /api/appearance` — 0 round trips (measured); `SiteAppearanceSnapshotCache`. Clean.
- `GET /api/site-settings` — 1 round trip (measured); anonymous, not audited, `staleTime` 5 min.
- `GET /api/menus/{key}` — the public menu route, called three times per mount. 3 round trips each (measured): 2 reads plus the outbox INSERT. Not cached. (There is no `/api/menus/{key}/tree` GET; `/tree` is an admin PUT.)
- `GET /api/record-types` — 6 round trips (measured), uncoalesced audit event.
- `GET /api/page-templates` — uncoalesced audit event.
- `GET /api/users/directory` — 0 round trips when warm (measured); snapshot cache with invalidation on local-user writes. Clean since #9.

## Per dynamic-route navigation
- `GET /api/pages` — the page registry (this file used to call it `/api/pages/registry`, which does not exist). 2 round trips (measured); `PageRegistrySnapshotCache` for menu items, `page_templates` read live.
- `GET /api/pages/lookup?path=` — page by path (formerly listed as `/api/pages/by-path/{path}`). One indexed `FromSql` plus authorization and an outbox row.
- `POST /api/auth/check` — the permission check (there is no `/api/permission-grants/check`). Batched for records only: 5 round trips for 1, 5, 25 or 50 record items (measured). Every other kind falls back to one check per id; for workflow executions each check is a cache read plus an edge load. #804.

## Polls
- `GET /api/notifications?limit=10` — **the real bell poll**: on mount, every 60 s per open tab (a raw `setInterval`, does not pause in the background), and on every notification event. 2 reads plus an uncoalesced outbox row. #801.
- `GET /api/notifications/unread-count` — has no SPA caller (`getUnreadCount` is unused). Its coalescer protects nothing.
- `GET /api/health/system` — 5 s poll with `staleTime: 0`, admin page only; probes run in sequence and open a new NATS connection each time. #822.
- Form dev preview — `refetchInterval: 1000`, pauses in the background; highest-frequency poll in the SPA.
- Executions freshness — polls at the server's interval (60 s, floor 5 s), pauses in the background.

## Bus-driven refetches
The WebSocket invalidation layer has no debounce: one server event is one refetch per matching mounted query per open tab. #810.
- `records:visible` → Home's watched-records panel reloads its list on any visible record change.
- `workflow-executions:visible` → invalidates the whole `["executions"]` prefix. #804.
- `page` events → every notebook's page tree in the cabinet.

## Per agent turn
- `POST /api/agent/conversations/{id}/messages` — SSE turn; up to 25 tool rounds, each re-sending the full prompt with no prompt caching. #809.
- `GET /api/agent/conversations/{id}` — invalidated after every turn; reads the whole message and tool-call history. #7.
- `GET /api/agent/conversations` — refetched on every conversation create or delete.
- Per-tool-call `INSERT` into `agent_tool_call`. #14.
- SPA: every streamed token re-renders the whole thread. #815.

## List views
- `GET /api/records` — 7 round trips for a page of 25 (measured); page size clamped, authorization pushed into SQL. Search and custom-field filters cannot use an index. #812.
- `GET /api/records/{id}` — 6 round trips (measured), one of them the outbox row.
- `GET /api/executions/page` — 6 round trips (measured); served from `workflow_execution_cache` with authorization in SQL (#108). No index provides its default sort. #813.
- `GET /api/executions` — unpaged; the executions page downloads it twice on mount. #790, #804.
- `GET /api/tasks/assigned-to-me`, `/assigned-to-team` — Home's task panels; one Flowable call per process instance. #805.
- `GET /api/content/notebooks/{id}/page-tree` and the content `/page` lists — cost grows with everything the user can reach, not the page listed. #806.
- `GET /api/notifications/page` — filters, count, sort and paging stay in SQL. Clean.
- `DataTable mode="auto"` — the count probe from #10 is gone; auto mode is now a deliberate two-step (page 0, then `loadAll` for small sets).
- `GET /api/event-catalog` — rare, materialises a lot.

## Background, with nobody signed in
Measured at 1.1 to 2.2 database commands a second on an idle instance. About half were cache upserts: the workflow projection rewrites every polled row each minute whether or not it changed. #807. The rest is polling: audit outbox (every 2 s), pipeline runs (5 s), system issues (10 s).

## Unbounded tables (any new per-request read here is a finding by default)
`audit_outbox` (dispatched rows are never deleted, #787), `record_events` / `record_field_changes`, `agent_message` / `agent_tool_call`, `notifications`, `menu_items`, `system_issues`, `workflow_execution_cache`, `workflow_event_log_cache`, `workflow_execution_errors` (nothing deletes from it), `page_versions` (a full body per autosave version, #808).
