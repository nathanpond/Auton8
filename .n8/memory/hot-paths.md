# Hot-path inventory (performance audits weight these highest)

Endpoints hit on every authenticated SPA mount, navigation, poll, or agent turn. Grow this file each `/n8-audit performance` run. Last updated 2026-08-30 (first n8SDLC audit).

## Per mount / navigation
- `GET /api/auth/me` — every `useMe` consumer; React Query refetches on focus. **Clean** (batched `ListForPrincipalsAsync` + `ListByIdsAsync`).
- `GET /api/pages/registry` (`ListPagesAsync`) — SPA mount. **Clean** (`PageRegistrySnapshotCache`).
- `GET /api/pages/by-path/{path}` (`GetPageByPathAsync`) — every dynamic-page navigation. **Clean** (`FromSqlInterpolated` on indexed JSONB paths).
- `GET /api/menus/{key}/tree` — sidebar on every navigation. Clean today (2 indexed queries), but re-reads `menus`+`menu_items` every 30 s stale window — natural second consumer of `PageRegistrySnapshotCache`.
- `GET /api/site-settings` — `useSiteSettings` mounted at shell level (`staleTime` 5 min).
- `GET /api/users/directory` — every collaborative-editor mount, assignee picker, comment render (16 `useUsers()` sites); whole `local_users` table, uncached. **Finding filed 2026-08-30.**
- `POST /api/auth/check` (`/api/permission-grants/check`) — batched per-row gating on list pages; one DB round-trip per check item. **Finding filed 2026-08-30.**

## Polls
- `GET /api/notifications/unread-count` — bell poll; coalesced server-side via `ViewEventCoalescer`. **Clean.**
- `GET /api/health/system` — 5 s poll with `staleTime: 0` (`useSystemHealth.ts:13`); admin-page-scoped, probes external components per call, no server coalescer.
- Form dev preview — `refetchInterval: 1000` (`useForms.ts:65`), `refetchIntervalInBackground: false`; highest-frequency poll in the SPA.

## Per agent turn
- `POST /api/agent/conversations/{id}/messages` — SSE turn; does a `site_settings` `GetBoolAsync` (fresh DbContext) and a provider resolve per turn.
- `GET /api/agent/conversations/{id}` — invalidated after every turn (`AgentSidebar.tsx:339`); reads the whole message + tool-call history. **Finding filed 2026-08-30.**
- `GET /api/agent/conversations` — `conversationsQuery` + `elsewhereQuery`, refetched on every conversation create/delete.
- Per-tool-call `INSERT` into `agent_tool_call` (`AgentSession.cs:416`). **Finding filed 2026-08-30.**

## List views
- `GET /api/executions/page` and `GET /api/executions` — load every execution from Flowable, authorise in memory, page client-side. **Finding filed 2026-08-30.**
- Any `DataTable mode="auto"` route is called twice per mount (count probe with `pageSize=0` + the real fetch): ManageUsers, WatchedRecordsPanel (home page), WorkflowExecutions, Grants, Hierarchy, AllProjects. **Finding filed 2026-08-30.**
- `GET /api/records` — page size clamped at the store (1..1000), authorisation pushed into SQL via `BuildRecordSqlFilter`. **Clean.**
- `GET /api/notifications/page` — all filters/count/sort/paging stay on `IQueryable`. **Clean.**
- `GET /api/event-catalog` — rare, materialises a lot.

## Unbounded tables (any new per-request read here is a finding by default)
`audit_outbox`, `record_events` / `record_field_changes`, `agent_messages` / `agent_tool_call`, `notifications`, `menu_items`, `system_issues`, `workflow_execution_cache`.
