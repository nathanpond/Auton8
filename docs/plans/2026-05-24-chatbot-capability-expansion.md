# Chatbot Capability Expansion — Phased Plan

## Context

AutoNate's in-app AI agent ships with 8 skills (records read/write, record-types write, explain-workflow, analyze-system-issue, page-awareness inspect/query/apply, web-fetch, web-search). Since that surface was built, the SPA has grown significantly: notes/pages/projects (BlockNote), AQL queries with a help modal and saved queries, dashboards/widgets, workflow execution + tasks, forms, content sharing, fine-grained permission grants, notifications, user/group/role management, site appearance + status appearance, plugins admin, projections, external connections, site settings, event catalog, and record edges — **none of which the chatbot can see or operate today**.

Goal: bring chatbot coverage to parity with the current product surface, phase by phase. Decisions baked into this plan from the planning conversation:

- **Write posture:** read tools everywhere; write tools only on selected domains, mirroring the existing `LookupRecordsSkill` vs `ManageRecordsSkill` split.
- **UI bridging:** backend skills are primary; new `add-page-context-provider` work only where it adds clear leverage (chiefly QueryPage AQL-assist, NotesPage, executions/tasks, grants).
- **AQL-assist is a Phase 2 priority:** users describe a query in English; the chatbot drafts AQL, validates it, optionally runs it, and offers to insert into the QueryPage editor via `apply_page_action`.
- **Notes write capability is full-featured:** a Markdown→BlockNote converter lets the LLM emit markdown and have it land cleanly in BlockNote pages (e.g. "summarize this conversation and save it as a note here").
- **Plugins gain a skill-registration API in Phase 4**, after Phase 3 has settled the confirm-gate envelope pattern.
- **Phase 5 is split** into operate (`5a`) and design (`5b`) so the higher-leverage operate work doesn't block on the richer page-context work.

The existing architecture is reused everywhere: `IAgentSkill` + `AgentTool` registered via DI, `SystemPromptBuilder` aggregating per-skill fragments, `AgentSession` driving the LLM loop, `InspectPageSkill`'s existing `apply_page_action` two-call confirm gate for SPA mutations, audit publishing per tool call.

---

## Phase 1 — Read coverage for all Phase 1 domains

**Outcome:** the chatbot can answer questions about notes, AQL, executions, permissions, users, groups, and notifications without being able to change anything.

**New skills** (all under `src/AutoNate.Web/Services/Agent/Skills/`, template = `LookupRecordsSkill.cs`):

| Skill file | Tools | Underlying stores (already auth-gated) |
|---|---|---|
| `LookupNotesSkill.cs` | `list_projects`, `get_project`, `list_cabinets`, `list_notebooks`, `list_pages`, `get_page`, `find_note` | `IProjectStore`, `INotebookStore`, `IPageStore`, `INoteStore` (see `ProjectEndpoints.cs`, `PageEndpoints.cs`, `ContentPageEndpoints.cs`) |
| `LookupAqlSkill.cs` | `list_saved_queries`, `get_saved_query`, `get_aql_grammar`, `get_aql_schema`, `describe_aql_entity` | `ISavedQueryStore`, the schema/grammar registry behind the new AQL help modal (commit `cd19d8f6`) |
| `LookupWorkflowExecutionsSkill.cs` | `find_execution`, `get_execution`, `list_execution_history`, `get_execution_variables`, `list_pending_tasks`, `get_task` | execution + task stores (see `ExecutionEndpoints.cs`, `WorkflowExecutions.tsx` data path) |
| `LookupPermissionsSkill.cs` | `list_permission_grants`, `describe_entity_kind`, `who_can`, `explain_authorization` | authorization registry, `PermissionGrantEndpoints.cs`, `AuthorizationExplainEndpoints` |
| `LookupDirectorySkill.cs` | `find_user`, `get_user`, `list_groups`, `get_group`, `list_group_members`, `list_roles` | `IUserStore`, `IGroupStore`, `IRoleStore` |
| `LookupNotificationsSkill.cs` | `list_notifications`, `get_notification` | `INotificationStore` |

Each skill: synchronous JSON schemas as const strings, `Invoke` resolves stores from `context.Services`, returns the existing `{kind, source, data}` envelope, fragment string for `SystemPromptBuilder`. DI registration in the same spot as the current 8 skills (search for `AddScoped<IAgentSkill, LookupRecordsSkill>` to find it).

**No page-context providers in this phase** — the read tools take ids/keys directly.

**Verification:**
- xUnit per skill in `tests/AutoNate.Web.Tests/Agent/Skills/` (use the in-memory authorizer harness; `LookupRecordsSkillTests` is the template).
- Run `/audit authorization` to confirm no new ungated reach.
- Manual: `admin/admin` login, chat "list my notebooks", "describe the records AQL entity", "who can manage_records on incidents", "show my notifications".

---

## Phase 2 — AQL-assist (write-help on QueryPage)

**Outcome:** on `/query`, a user can say "show me all open incidents assigned to me this week" and the chatbot drafts an AQL query, validates it, can preview-run it, and offers to insert it into the editor via confirmed `apply_page_action`.

**Page-context provider** (`src/AutoNate.Spa/src/pages/query/QueryPage.tsx`, invoke `add-page-context-provider` skill).

Snapshot shape:
```ts
{
  schemaVersion: 1,
  summary: "QueryPage: editing AQL for entity 'records', N results, last run OK/error",
  data: {
    editor: { aqlText, cursorOffset, selectionRange, entity },
    lastResult: { ok, rowCount, columns, errorMessage?, ms } | null,
    savedQuery: { id, name, dirty } | null,
    availableEntities: string[],
    actions: ["set_aql_text", "append_aql", "run_query", "save_query"]
  }
}
```

Page-action handlers (in `QueryPage.tsx` `onPageAction`):
- `set_aql_text({ text })` — replace editor contents
- `append_aql({ text })` — insert at cursor
- `run_query()` — trigger the run button
- `save_query({ name })` — open save modal pre-filled

**New skill** `AqlAssistSkill.cs`:

| Tool | Purpose | Mutating? |
|---|---|---|
| `suggest_aql` | Draft AQL from a natural-language description + entity. Returns text only. | No |
| `validate_aql` | Parse + type-check without execution; returns line/col errors. | No |
| `run_aql` | Execute through the gated AQL executor that backs `QueryEndpoints.cs`. Caps row count. | No (read-only) |

System-prompt fragment (returned by `AqlAssistSkill.SystemPromptFragment`):
> "To help with AQL: (1) call `get_aql_schema` for entity shape and `get_aql_grammar` for syntax — these tools are the source of truth, do not improvise. (2) Draft with `suggest_aql`, then `validate_aql`, optionally preview with `run_aql`. (3) When the user is on QueryPage, propose insertion via `apply_page_action set_aql_text` with `confirmed:false` first; only commit after explicit approval. Never run a query as a side effect of drafting."

The grammar/schema text is **never** baked into the system prompt — the agent always fetches via tools so it stays in lockstep with the live registry.

**Verification:**
- Snapshot/provider unit test in `src/AutoNate.Spa/src/pages/query/__tests__/`.
- `AqlAssistSkillTests` — assert `run_aql` cannot bypass authorizer (deny test).
- Manual on `/query` (admin/admin): "List incidents created this week", confirm chat narrates → click confirm → editor populates → result table renders.

---

## Phase 3 — Selected write capabilities (mirrors lookup/manage split)

**Outcome:** the chatbot can change state in the Phase 1 domains, with mandatory `confirmed:false` dry-run envelopes before commit. Includes the Markdown→BlockNote converter that unlocks "summarize this and save it as a note".

**New skills (template = `ManageRecordsSkill.cs`):**

| Skill file | Tools |
|---|---|
| `ManageNotesSkill.cs` | `create_page_from_markdown`, `append_markdown_to_page`, `replace_page_with_markdown`, `update_page` (name/parent/icon/share state), `move_page`, `create_notebook`, `create_project`, `share_project` |
| `ManageSavedQueriesSkill.cs` | `save_query`, `update_saved_query`, `delete_saved_query` |
| `OperateWorkflowExecutionsSkill.cs` | `cancel_execution`, `terminate_execution`, `reassign_task`, `change_task_due_date`, `complete_task` (use `add-workflow-execution-action` skill for each new action — it walks the full backend→permission→UI→tests path) |
| `ManagePermissionsSkill.cs` | `grant_permission`, `revoke_permission`, `add_user_to_group`, `remove_user_from_group`, `assign_role`, `unassign_role` |
| `SendNotificationsSkill.cs` | `send_notification`, `mark_notification_read`, `dismiss_notification` |

**Markdown → BlockNote converter** (the load-bearing piece for notes write):

- New service `src/AutoNate.Web/Services/Notes/MarkdownToBlockNoteConverter.cs`. Uses `Markdig` (already a transitive dep — verify; add if not) to parse markdown into AST, walks the AST to emit BlockNote-shaped JSON (paragraphs, headings 1–3, bullet/numbered/checked lists, code blocks with language, blockquotes, tables, inline marks: bold/italic/code/strikethrough/link, images). Targets `@blocknote/core ^0.51`'s block JSON schema.
- Registered as `IMarkdownToBlockNoteConverter` in DI. Pure (no I/O, no auth) — auth lives in the page store the skill writes through.
- SPA mirror: when the agent is editing an already-open page via `apply_page_action`, call BlockNote's own `tryParseMarkdownToBlocks` from `@blocknote/core` instead of round-tripping through the backend converter. The page-action handler picks the right path. (Adds a `replace_blocks_from_markdown` action to the NotesPage provider.)
- Round-trip test: a curated markdown corpus → backend converter → SPA renderer → exported markdown should match within a tolerance (whitespace + canonical inline mark ordering). Document any lossy edges (e.g. nested blockquotes) as known limitations.

**Confirm-gate helper** — extract `Skills/Internal/ConfirmGate.cs` so every manage-skill shares one envelope shape (action name, args summary, dry-run preview, confirmation token). This drift risk was called out as a Phase 4 dependency.

**New page-context providers** (invoke `add-page-context-provider` per page):
- `NotesPage.tsx` — current project/cabinet/notebook/page selection, dirty state, share-modal presence
- `WorkflowExecutions.tsx` + execution detail — current execution, selected task, current filter
- `Grants.tsx` — selected grant, target principal
- `manage-users/` — current user + group memberships
- `Notifications.tsx` — current filter, selected notification

These let manage tools default args from page state ("cancel this execution" without re-stating the id).

**Verification:**
- Per-skill commit + dry-run tests.
- `MarkdownToBlockNoteConverterTests` covers the full block + inline mark surface.
- Confirm every commit emits its existing domain audit event (use `add-audit-event` for any new event types) and shows up in EventCatalog.
- `/audit authorization` rerun.
- Manual: on NotesPage, "summarize this conversation and create a note here" → page appears with formatted content; on `/executions`, "cancel this execution" → dry-run shown → confirm → cancelled; on `/admin/grants`, "give Alice manage_records on Incidents" → confirm flow.

---

## Phase 4 — Plugin-contributed skills

**Outcome:** a plugin can register one or more `IAgentSkill` instances during `Configure(IPluginContext)`, and they appear in the chat's tool catalog scoped to the plugin's authorization.

**Architecture (mirrors `IPluginProjections`):**

1. New `src/AutoNate.Plugin.Abstractions/IPluginAgentSkills.cs`:
   ```csharp
   public interface IPluginAgentSkills {
       void Register(string name, string description, IReadOnlyList<PluginAgentTool> tools, Func<PluginAgentSessionContext, string?>? promptFragment = null);
       int RemoveAll();
   }
   ```
2. DTOs in the abstractions assembly: `PluginAgentTool { Name, Description, JsonSchema, Func<JsonElement, PluginAgentToolContext, CancellationToken, Task<JsonElement>> Invoke }`, `PluginAgentToolContext`, `PluginAgentSessionContext`. **Only types in the abstractions package + `System.Text.Json.JsonElement` + primitives may cross the ALC boundary.**
3. Add `IPluginAgentSkills AgentSkills { get; }` to `IPluginContext.cs`.
4. Host adapter `src/AutoNate.Web/Services/Agent/Skills/PluginContributedSkill.cs` — wraps each registered plugin tool, translates DTOs to the host's `AgentTool`. Implements `IAgentSkill` so `SkillRegistry` picks it up via a DI-registered `PluginSkillCollector` singleton fed by the plugin host.
5. Wire in `src/AutoNate.Web/Plugins/PluginHostedService.cs` (or wherever `Configure` is invoked): after each plugin's `Configure`, drain its `PluginAgentSkillsImpl` into the collector; on disable, `RemoveAll`.

**AssemblyLoadContext discipline (per `plugin-creator` skill):**

- Plugins live in their own collectible ALC. Passing host-private types (e.g. `AgentSessionContext`, `IAuthorizer`, EF entities) would fail `is`/`as` checks across the boundary. The DTOs above guarantee only abstraction-layer types and `JsonElement` cross.
- `PluginAgentSessionContext` exposes `Guid UserId`, `string[] Roles`, and `Task<bool> CanAsync(string kind, string action, Guid? entityId)` delegating to the host `IAuthorizer`. The plugin never touches `ClaimsPrincipal` (drifts across ALC unload).
- Data reads inside plugin tools route through `IPluginContext.Data` (already authorized as the per-plugin Postgres role).

**Verification:**
- Add a sample `hello_echo` skill to the existing `HelloPlugin`.
- Enable→assert tool catalog grew; disable→assert it shrank.
- Multi-cycle test: enable/disable 10× — no `TypeLoadException`, no leaked tools.
- Authorization test: plugin calls `CanAsync` and gets a correct deny for a principal without permission.

---

## Phase 5a — Operate gaps (admin write coverage for remaining domains)

**Outcome:** chatbot can perform admin operate actions across the rest of the surface. Page-awareness work is minimal; tools call services directly.

New skills (file pattern as before; pair lookup + operate where useful):
- `LookupProjectionsSkill` / `OperateProjectionsSkill` (pause, resume, rebuild — see `add-projection` skill for the existing operate contract)
- `LookupPluginsSkill` / `OperatePluginsSkill` (enable, disable, reload)
- `LookupExternalConnectionsSkill` / `OperateExternalConnectionsSkill` (test, set credentials with confirm gate)
- `LookupSiteSettingsSkill` / `ManageSiteSettingsSkill`
- `LookupEventCatalogSkill` (read only)
- `ManageRecordEdgesSkill` (lookup + create/update/delete with confirm)

**Verification:** per-skill tests; `/audit authorization` rerun; manual smoke per area.

---

## Phase 5b — Design gaps (richer UIs)

**Outcome:** chatbot supports the design-time UIs (dashboard authoring, form builder, workflow studio, theme editor) primarily by being page-aware.

New page-context providers (invoke `add-page-context-provider`):
- Dashboard canvas + WidgetPicker — current dashboard, selected widget, layout
- Form designer — current form, selected component
- Workflow studio — current model, selected element
- SiteAppearance — current theme draft, dirty state

New read skills: `LookupDashboardsSkill`, `LookupFormsSkill`, `LookupWorkflowModelsSkill`, `LookupAppearanceSkill`.

Deep authoring tools (e.g. "build me a BPMN", "lay out this dashboard") are explicitly **out of scope** for v1 — page-awareness + targeted micro-actions only (e.g. "set widget title", "add filter for status=open"). Deep generation waits for a follow-up.

**Verification:** snapshot/provider unit tests per page; manual exploration with admin/admin.

---

## Risks & ordering trade-offs

1. **Tool catalog bloat.** By end of Phase 3 the chatbot exposes ~40 tools. Watch system-prompt token cost. Mitigation: add a per-page/permission filter in `SkillRegistry.ChatTools` (advertise only relevant skills given the user's grants + current `pageKey`).
2. **Confirm-gate drift.** Several manage-skills replicate the dry-run envelope. The `Skills/Internal/ConfirmGate.cs` helper added in Phase 3 is what makes Phase 4 safe for plugin authors to copy.
3. **Phase 2 needs Phase 1's `LookupAqlSkill` shipped.** The AQL-assist fragment instructs the model to call `get_aql_schema` / `get_aql_grammar` first — those live in Phase 1.
4. **Markdown→BlockNote fidelity.** Tables, code blocks with language, and nested lists are the high-risk areas. Have an explicit known-limits doc in the converter file; round-trip tests are mandatory.
5. **Page-context schema versioning.** Every new provider declares `schemaVersion`. Bump on shape change; let skill prompt fragments branch.
6. **Plugin ALC type identity.** The single largest correctness risk in Phase 4. The "abstractions + `JsonElement` only" rule is non-negotiable.
7. **Existing project skills already encode the right patterns** — invoke them rather than hand-rolling: `add-page-context-provider` (SPA wiring), `add-workflow-execution-action` (Phase 3 executions), `add-permission-gate` (Phase 3 permissions writes if any new actions are needed), `add-audit-event` (new event types), `plugin-creator` (Phase 4), `audit-authorization` (after Phases 1, 3, 5a).

---

## Critical files

- `src/AutoNate.Web/Services/Agent/Skills/IAgentSkill.cs` — contract for every new skill
- `src/AutoNate.Web/Services/Agent/Skills/SkillRegistry.cs` — DI aggregation; per-page filter lands here
- `src/AutoNate.Web/Services/Agent/Loop/SystemPromptBuilder.cs` — system prompt + fragment composition
- `src/AutoNate.Web/Services/Agent/Skills/ManageRecordsSkill.cs` — confirm-gate template
- `src/AutoNate.Web/Services/Agent/Skills/LookupRecordsSkill.cs` — read-skill template
- `src/AutoNate.Web/Services/Agent/Skills/InspectPageSkill.cs` — `apply_page_action` two-call confirm gate (reused, not modified)
- `src/AutoNate.Spa/src/agent/PageContextRegistry.tsx` — provider registration entry point
- `src/AutoNate.Spa/src/pages/query/QueryPage.tsx` — Phase 2 page provider
- `src/AutoNate.Spa/src/pages/notes/NotesPage.tsx` — Phase 3 page provider + BlockNote markdown handler
- `src/AutoNate.Plugin.Abstractions/IPluginContext.cs`, `IPluginProjections.cs` — Phase 4 prior art + extension point
- `tests/AutoNate.Web.Tests/Agent/Skills/` — test patterns to copy
