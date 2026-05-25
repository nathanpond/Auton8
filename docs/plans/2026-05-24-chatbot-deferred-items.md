# Chatbot Expansion — Deferred Items

## Context

Phases 1–5 of the chatbot capability expansion (planned in `humble-riding-hollerith.md`) shipped end-to-end: 45 new tests, ~8,300 lines, 1252/1252 tests passing, zero regressions. Several items were intentionally deferred during that work — some because they're substantial follow-ups worth their own focused effort, others because they fall outside the chatbot's appropriate scope.

This plan catalogs them in three buckets:
1. **Phase 6** — items worth picking up next, with implementation sketch and effort estimate.
2. **Out of scope by design** — items that should stay deferred indefinitely, with rationale.
3. **Technical debt** — improvements worth scheduling once usage data or a refactor brings them onto the path.

The chatbot architecture, skill conventions, plugin abstractions, and page-context contract from the prior plan all stand — this plan only describes additions, not rework.

---

## Phase 6a — BlockNote block editing on open pages (HIGH VALUE)

**Outcome:** when a user has a BlockNote page open in the SPA, the chatbot can insert / replace / append blocks via the live editor (which writes through Yjs naturally, just like a user typing). Unlocks "summarize this conversation and add it to the page I'm reading" and "rewrite this paragraph to be shorter." Today the chatbot can only create new pages from markdown via the backend skill — existing-page body edits are blocked.

### Why it's deferred

Existing-page bodies are owned by the Yjs collab session. `YjsManagedContentGuard.RejectPageBodyWrite` in `src/AutoNate.Web/Endpoints/YjsManagedContentGuard.cs` returns 409 Conflict on any direct `/api/content/pages` PATCH that touches `bodyJsonb` — by design, so the chatbot can't race Hocuspocus snapshots and corrupt the CRDT. The Phase 3 `MarkdownToBlockNoteConverter` is therefore wired backend-only (page creation). Edits on open pages must route through the SPA-side BlockNote editor instance whose changes are picked up by the Yjs binding.

### Architecture

1. **Expose the active editor ref.** `src/AutoNate.Spa/src/pages/notes/EditorPane.tsx` mounts the BlockNote editor via `useBlockNoteWithYjs`. The page-context provider needs access to the *active tab's* editor handle for the currently-active page. Cleanest path: a small `ActiveEditorRegistry` (React context) NotesPage owns and EditorPane writes its handle into; the page-context hook reads from it.
2. **Add actions to `useNotesPagePageContext.ts`.** Three new mutating actions:
   - `replace_blocks_from_markdown({ markdown })` — replaces every block in the active editor
   - `append_blocks_from_markdown({ markdown })` — inserts at end
   - `insert_blocks_from_markdown({ markdown, afterBlockId? })` — inserts after a specific block (or at start when `afterBlockId` omitted)
3. **Markdown → blocks on the SPA side.** `@blocknote/core` ships `tryParseMarkdownToBlocks` (v0.51 API). The handler calls it, gets the block array, then uses `editor.replaceBlocks` / `editor.insertBlocks` to apply. Writes flow through Yjs from there.
4. **Confirmation flow** is automatic — the existing `apply_page_action` contract handles the two-call gate (`confirmed=false` returns a structured proposal; the model narrates; user agrees; `confirmed=true` reaches the handler).
5. **Note kinds.** Only `richtext` (BlockNote) is in scope. `drawing` (Excalidraw) and `diagram` (draw.io) are Yjs-managed too but driven by different editors; mark the action handlers as `unsupported_type` when the active tab is non-richtext so the model gets a precise error rather than a silent miss.

### Critical files

- `src/AutoNate.Spa/src/pages/notes/EditorPane.tsx` — write the editor handle into the registry on mount
- `src/AutoNate.Spa/src/pages/notes/NotesPage.tsx` — provide the registry context
- `src/AutoNate.Spa/src/pages/notes/useNotesPagePageContext.ts` — add `MY_PAGE_ACTIONS` entries + `onPageAction` switch cases; read editor handle from the registry
- New: `src/AutoNate.Spa/src/pages/notes/ActiveEditorRegistry.tsx` — React context + per-tab editor handle store
- Optional helper: `src/AutoNate.Spa/src/lib/blocknote/markdownToBlocks.ts` if `tryParseMarkdownToBlocks` needs any wrapping (error normalization, async fallback, etc.)

### Verification

- SPA-level test: mount the registry context with a fake editor handle, dispatch `insert_blocks_from_markdown` via the registry's `dispatchPageAction`, assert the fake editor's `insertBlocks` was called with the parsed blocks.
- Manual: open a page in `/notes`, ask the chatbot "add a paragraph at the end saying X" — expect a confirmation card, accept, see the paragraph appear, and observe Yjs propagation to a second open tab on the same page.

### Effort

Medium. ~1–2 days. The converter is established, the page-context plumbing is in place, and the BlockNote API for inserting blocks is well-documented. The trickiest piece is exposing the editor ref through the registry without leaking it broadly.

---

## Phase 6b — Rich page-context providers for design surfaces (MEDIUM VALUE)

**Outcome:** when a user is on the Dashboard, Form designer, or SiteAppearance pages, the chatbot can read live selection / dirty state via `inspect_page` and propose small mutations via `apply_page_action` ("set this widget's title to X", "add a chart filter for status=open", "switch the form's primary color to blue"). Complements Phase 5b's read-only `DesignSurfacesLookupSkill`.

### Why it's deferred

Phase 5b shipped the lookup skills + added `usePageKey.ts` mappings for `/dashboard`, `/admin/config/forms/:id`, and `/admin/config/appearance`, so the framework's auto form-fill discovery already gives the chatbot a usable surface on these pages. Rich providers exposing selection / per-element state are higher-value but each is a few hours of per-page wiring; deferred to focus Phase 5 on the broader admin operate gaps.

### Per-page sketches

**Dashboard** (`src/AutoNate.Spa/src/pages/dashboard/useDashboardPageContext.ts`)
- Snapshot: active dashboard id, widget list (id + title + type, lean), selected widget id, layout array, dirty flag (any unsaved layout change)
- Query topic: `widget.byId` for full widget config (the snapshot keeps it lean to stay under 64KB)
- Actions: `set_widget_title({ widgetId, title })`, `update_widget_config({ widgetId, patch })`, `move_widget({ widgetId, x, y, w, h })`

**Form designer** (`src/AutoNate.Spa/src/pages/admin/config/forms/useFormEditorPageContext.ts`)
- Snapshot: active form id, version, sections, selected component id, dirty flag, draft vs published status
- Actions: `select_component({ componentId })`, `set_component_label({ componentId, label })`, `set_component_required({ componentId, isRequired })`

**SiteAppearance** (hook adjacent to `src/AutoNate.Spa/src/pages/admin/config/SiteAppearance.tsx`)
- Snapshot: current theme draft, original snapshot for diff, dirty fields, color preview availability
- Actions: `set_appearance_field({ field, value })` typed against the SiteAppearance DTO

Each follows the `use<Page>PageContext.ts` template established by `useQueryPagePageContext.ts` and `useNotesPagePageContext.ts`. The framework owns the confirmation gate; per-page handlers only validate args and mutate React state. No persistence happens in the handler — the user still clicks Save in the SPA.

### Verification per page

Snapshot/provider unit test (no rendering, just register + getSnapshot + dispatch) plus manual exploration with `admin/admin` per the `add-page-context-provider` skill's section 9 checklist.

### Effort

Medium. ~1 day per page (~3 days total). Mechanical work — the patterns are settled and the auth model needs nothing new because handlers mutate in-memory React state only.

---

## Phase 6c — Markdown → BlockNote converter improvements (LOW–MEDIUM VALUE)

The Phase 3 converter (`src/AutoNate.Web/Services/Notes/MarkdownToBlockNoteConverter.cs`) ships with documented limitations:
- Nested lists flatten to top-level items with an indent prefix
- Tables degrade to pipe-joined paragraph rows
- Images ignored
- HTML blocks rendered as plain-text tag names
- No `checkListItem` support

Incremental enhancements, each independently shippable and testable:

1. **Native BlockNote table block mapping** (most-requested per typical markdown). Walk `Markdig.Extensions.Tables.Table` → BlockNote `table` block with `tableContent.rows[].cells[].content` inline arrays.
2. **Nested-list support.** Recurse list children into `block.children` rather than flattening with indent prefixes. BlockNote renders nested lists out of the box once the children array is populated.
3. **Image blocks.** BlockNote has an `image` block accepting `url` + `caption`. Map `LinkInline` with `IsImage = true` (Markdig's image variant) onto it.
4. **Check-list items.** GFM `- [ ]` / `- [x]` → BlockNote `checkListItem` with the `checked` prop. Markdig's `TaskList` extension exposes this if enabled in the pipeline builder.

Each item is half-day work. `MarkdownToBlockNoteConverterTests.cs` already covers the existing block + inline mark surface; extend per addition with a few targeted assertions.

### When to schedule

Watch the audit log for `create_page_from_markdown` commits and inspect a sample of input markdown the LLM is generating. Schedule the most-frequent missing constructs first. If users start asking the chatbot for tables and getting pipe-joined paragraphs, that's the signal for #1.

---

## Out of scope by design

These were called out as deferred but should remain deferred unless requirements change. Each has a substantive reason — they're not "we ran out of time."

### External-connection secret rotation through the chatbot

The Phase 5a `ExternalConnectionsSkill` deliberately excluded `set_credentials` / API-key writes. Tool-call arguments flow through the model provider's inference pipeline; submitting an API key as a tool argument means it transits an external service in plaintext (and is potentially logged by that provider). The admin UI's connection form uses the existing `/api/admin/external-connections` PATCH endpoint, which is the appropriate path for secret rotation.

Reconsider only if a future architecture isolates tool arguments from model providers — e.g. host-side parameter substitution that replaces opaque tokens with secrets *after* the LLM returns its tool call and *before* the host invokes the handler.

### Plugin upload-from-zip

Plugin distribution is a multipart binary upload. The chatbot has no clean shape for "attach this zip file":
- Encoding base64 inside a tool argument means multi-MB payloads bloating every transcript message.
- Pulling a URL introduces supply-chain risk and SSRF surface.
- Asking the model to *generate* plugin source is dangerous and out of scope.

The SPA's `/api/admin/plugins` POST is the right path. Phase 4's `PluginsAdminSkill` covers enable / disable / delete, which is the operate surface the chatbot adds value on.

### Deep authoring tools

"Build me a BPMN that approves invoices over $10k," "lay out this dashboard with 4 widgets summarizing X," "design a form for onboarding requests." Phase 5 plan explicitly scoped this as out-of-v1. The Phase 6b page-context providers above ship *targeted micro-actions*; full design generation needs careful prompt engineering, a much larger schema (BPMN element vocabulary, dashboard widget registry, form component types) in the system prompt, and quality gates the chatbot framework doesn't have today.

Defer until users explicitly request it AND there's bandwidth to ship it well. Premature shipping here produces dashboards / forms / workflows the user spends more time fixing than they would have spent authoring directly.

---

## Technical debt worth scheduling

These don't block any user-visible capability but pay off over time.

### Per-page tool filter on `SkillRegistry.ChatTools`

By end of Phase 5 the chatbot exposes ~80 tools. The full catalog goes into every request's `tools` array; system-prompt token cost is noticeable. The original plan flagged a per-page/permission filter: only advertise skills relevant to the user's current `pageKey` and their grants.

Implementation sketch:
- `SkillRegistry` gains `ChatToolsFor(pageKey, principal, IAuthorizer)` that consults each `IAgentSkill` for an optional `IsRelevantForPage(pageKey)` and `RequiresPermission(...)` declaration.
- `SystemPromptBuilder` calls the filtered variant when assembling per-turn tools.
- Each skill can opt into restrictions; default behavior (no opt-in) preserves today's "always available."

Critical files:
- `src/AutoNate.Web/Services/Agent/Skills/SkillRegistry.cs`
- `src/AutoNate.Web/Services/Agent/Skills/IAgentSkill.cs`
- `src/AutoNate.Web/Services/Agent/Loop/SystemPromptBuilder.cs`

Effort: 1–2 days. Schedule when token cost shows up as a measured concern (audit the per-turn input-token count and decide).

### Deeper test coverage for Phase 1 / 3 / 5 manage skills

The phase-test suites (`Phase1ReadSkillsTests.cs`, `Phase3ManageSkillsTests.cs`, `Phase5SkillsTests.cs`) focus on catalog assembly, auth-gate shortcuts, and missing-arg rejections. Heavier per-skill tests against fake stores — full happy-path commit, audit-event publication assertions, dry-run preview shape — would mirror what `ManageRecordsSkillTests.cs` and `ManageRecordTypesSkillTests.cs` do today.

Not urgent (the underlying stores have their own tests) but worth doing per-skill the next time one of them needs to change. The pattern is established in `ManageRecordsSkillTests.cs` and is straightforward to follow.

### Extract shared skill helpers

Every Phase 1+ skill duplicates `ParseSchema`, `Error`, `TryReadGuid`, `ReadString`. Extracting to `src/AutoNate.Web/Services/Agent/Skills/Internal/SkillSchemaHelpers.cs` would save ~400 lines and concentrate the JSON-element parsing conventions in one place.

Cosmetic — defer until someone is touching multiple skill files anyway; a focused refactor PR right before adding a new skill batch is ideal timing.

### AQL grammar / schema response caching

`LookupAqlSkill.get_aql_grammar` computes its response per call. The grammar is effectively static (clause keywords, operators, aggregates); the per-entity schema rarely changes within a session. A per-process cache invalidated by `IQueryEntityRegistry` changes would cut token cost and latency.

Defer until usage profiling shows this on the hot path. The lookup-aql tool typically fires once per AQL-help conversation, so the win is small.

### Plugin agent-skill catalog churn telemetry

`PluginContributedSkill` snapshots `PluginAgentSkillRegistry` on each per-request construction. When many plugins are installed and frequently enabled/disabled, the snapshot churn could become visible. Today there's no observability into this. A simple counter on enable/disable + skill-count histogram in metrics would surface a problem before users see it.

Effort: half a day. Schedule when the host gets a meaningful set of plugin-contributed skills (currently just the sample `hello_echo`).

---

## Critical files (cross-reference)

- **Phase 6a:** `src/AutoNate.Spa/src/pages/notes/EditorPane.tsx`, `useNotesPagePageContext.ts`, `NotesPage.tsx`; new `ActiveEditorRegistry.tsx`
- **Phase 6b:** `src/AutoNate.Spa/src/pages/dashboard/`, `src/AutoNate.Spa/src/pages/admin/config/forms/`, `src/AutoNate.Spa/src/pages/admin/config/SiteAppearance.tsx`
- **Phase 6c:** `src/AutoNate.Web/Services/Notes/MarkdownToBlockNoteConverter.cs`, `tests/AutoNate.Web.Tests/MarkdownToBlockNoteConverterTests.cs`
- **Per-page tool filter:** `src/AutoNate.Web/Services/Agent/Skills/SkillRegistry.cs`, `IAgentSkill.cs`, `SystemPromptBuilder.cs`
- **Original plan:** `/Users/npond/.claude/plans/humble-riding-hollerith.md`
