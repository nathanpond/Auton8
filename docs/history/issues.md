# Issue archive

> Archived from `nathanpond/AutoNate` (later `Auton8`) before the repository was
> migrated on 2026-09-02. GitHub cannot transfer pull requests between
> repositories, and the pre-migration repository is private permanently because
> its git history contains a commercially-licensed theme. This file exists so
> the reasoning behind the work survives in the open.
>
> **Numbers here are pre-migration.** A `#N` in a commit message written before
> 2026-09-02 refers to this register, not to the current one.

121 issues. Open issues were carried over to the new repository and renumbered; closed ones are history.

---

## archived-7 — SiteAppearance: default sidebarSectionColor #adb5bd fails contrast at 2.07:1 and is not in CONTRAST_CHECKS

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:spa`

## What
`DEFAULT_SITE_APPEARANCE.sidebarSectionColor` is `#adb5bd` on `sidebarBg` `#ffffff` (siteAppearance.ts:35). Computed WCAG contrast is **2.07:1** (needs 4.5:1 — 0.78rem bold uppercase is not large text). `CONTRAST_CHECKS` (siteAppearance.ts:297-309) lists 9 pairs and omits this one, so the admin editor never warns.

## Where
`src/AutoNate.Spa/src/lib/siteAppearance.ts:35 (default) and :297-309 (CONTRAST_CHECKS); rendered by src/AutoNate.Spa/src/pages/admin/config/ConfigLayout.css:43`

## Why it matters
Every Site-Configuration nav group heading (SITE, SECURITY, …) is effectively invisible to low-vision users — they lose the grouping that makes a 30-item admin nav navigable. WCAG 1.4.3 / 508 §501.

## Evidence
```
35:  sidebarSectionColor: "#adb5bd",
```
Contrast computed with the WCAG relative-luminance formula: `#adb5bd` on `#ffffff` = 2.07:1; `#5c636a` on `#ffffff` = 6.09:1. All 15 other default pairs pass (body 15.43, dimmed 4.69, top-menu link 6.60, primary button 4.77, …).

## Suggested fix
Change the default to `#5c636a` (6.09:1) and add `{ fgKey: "sidebarSectionColor", bgKey: "sidebarBg", pairLabel: "Sidebar section label", required: 4.5, reason: "text" }` to `CONTRAST_CHECKS`. Add a unit test asserting `checkContrastWarnings(DEFAULT_SITE_APPEARANCE)` returns `[]` so the default theme can never regress.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: contrast-fail|src/AutoNate.Spa/src/lib/siteAppearance.ts|sidebarSectionColor -->

---

## archived-8 — Notes: twelve hand-rolled modals have no dialog role, focus trap, or focus return

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
Every Notes dialog is a raw `<div onClick={onClose}>` overlay wrapping an inner `<div onClick={stopPropagation}>`. `grep -rn 'role="dialog"\|aria-modal' src` returns 0 hits across the SPA. Same shape in NewNotebookModal, NewCabinetModal, NewPageModal, NewProjectModal, EditCabinetModal, EditNotebookModal, EditPageModal, MoveCopyModal, HistoryModal, ConfirmDialog, and EditorPane.tsx:783.

## Where
`src/AutoNate.Spa/src/pages/notes/NewNoteModal.tsx:27-42 (+11 siblings)`

## Why it matters
A screen-reader user gets no dialog announcement and no boundary — Tab walks out of the modal into the page behind it; on close focus is lost to `<body>`. Blocks creating/renaming/moving/deleting notes without a mouse. WCAG 4.1.2 + 2.4.3 / 508 §502.

## Evidence
```
27:    <div
28:      onClick={onClose}
29:      style={{
30:        position: "fixed",
41:      <div
42:        onClick={(e) => e.stopPropagation()}
```

## Suggested fix
Replace each outer/inner div pair with Mantine `<Modal opened onClose title="…">` — it supplies role, `aria-modal`, FocusTrap, Escape handling and focus return. `components/ConfirmModal.tsx` is the in-repo pattern.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: custom-dialog-no-focus-trap|src/AutoNate.Spa/src/pages/notes/NewNoteModal.tsx|overlay-div -->

---

## archived-9 — Notes modals: raw <input> labelled by a presentational <div> has no accessible name

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
`Label` (NewNoteModal.tsx:247) renders a styled `<div>`; the adjacent `<input>` (line 116) has no `id`/`htmlFor`/`aria-label`. Repeated across the six New*/Edit* notes modals.

## Where
`src/AutoNate.Spa/src/pages/notes/NewNoteModal.tsx:115-118, :247-249`

## Why it matters
NVDA/VoiceOver announce "edit, blank" — the user cannot tell what to type; blocks note creation for screen-reader users. WCAG 1.3.1 + 3.3.2 / 508 §502.

## Evidence
```
115:          <Label>Name</Label>
116:          <input
117:            value={name}
118:            autoFocus
247: function Label({ children }: { children: React.ReactNode }) {
248:   return (
249:     <div
```

## Suggested fix
Swap the raw `<input>` for Mantine `<TextInput label="Name" />`, which wires `<label htmlFor>` automatically. Do it together with the Modal migration.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: raw-input-no-label|src/AutoNate.Spa/src/pages/notes/NewNoteModal.tsx|Label -->

---

## archived-10 — Notes Explorer: page, notebook and cabinet rows are clickable <div>s with no keyboard path

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
Page rows (Explorer.tsx:706), notebook rows (:563) and cabinet rows (:816) are `<div onClick>` with no `tabIndex`, `role`, or key handler, and there is no alternate link.

## Where
`src/AutoNate.Spa/src/pages/notes/Explorer.tsx:706-711 (also :563, :816)`

## Why it matters
A keyboard-only user can reach the Notes page but cannot open a single note — the module's primary task is unreachable. WCAG 2.1.1 + 4.1.2 / 508 §502.

## Evidence
```
706:        <div
707:          draggable={dropAllowed}
708:          onClick={(e) => {
709:            e.stopPropagation();
710:            onPagePick(page.id);
711:          }}
```

## Suggested fix
Render each row as Mantine `<UnstyledButton>` (or `<NavLink>`) — native button semantics restore Tab + Enter/Space without changing the drag behaviour.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: clickable-div|src/AutoNate.Spa/src/pages/notes/Explorer.tsx|page-row -->

---

## archived-11 — MenuTreeEditor: menu rows are <li onClick>; selecting an item to edit is mouse-only

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
Both the separator branch (:332) and the item branch (:384) put `onClick={onSelect}` on a bare `<li>`. The nested `UnstyledButton`s all `stopPropagation`, so no child can select the row.

## Where
`src/AutoNate.Spa/src/pages/admin/config/MenuTreeEditor.tsx:384-390 (and :332)`

## Why it matters
An admin using a keyboard or switch device can expand/hide/delete rows but never select one to open the detail editor — menu configuration is unreachable. WCAG 2.1.1 / 508 §502.

## Evidence
```
384:    <li
385:      ref={setNodeRef}
386:      style={{ ...style, ...rowStyle }}
387:      className={isSelected ? "active" : undefined}
388:      onClick={onSelect}
389:    >
390:      <UnstyledButton
```

## Suggested fix
Wrap the row label in `<UnstyledButton onClick={onSelect} aria-current={isSelected}>` and drop `onClick` from the `<li>`.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: clickable-li|src/AutoNate.Spa/src/pages/admin/config/MenuTreeEditor.tsx|row-li -->

---

## archived-12 — DataTable: onRowClick rows have no keyboard affordance; Notifications rows cannot be opened without a mouse

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
`DataTable` forwards `onRowClick` straight to mantine-datatable, which renders `<tr onClick>` with no `tabIndex`/`role`. `getRowAriaLabel` is accepted and explicitly discarded (:515-517). `Notifications.tsx:125` relies solely on `onRowClick` — no link or button in any cell. (`RecordList` survives only because its Key column has a `<Link>`.)

## Where
`src/AutoNate.Spa/src/components/data-table/DataTable.tsx:511-517; src/AutoNate.Spa/src/pages/notifications/Notifications.tsx:125`

## Why it matters
Keyboard and AT users cannot open any notification. WCAG 2.1.1 + 4.1.2 / 508 §502.

## Evidence
```
511:        onRowClick={onRowClickAdapter}
512:        rowClassName={rowClassNameAdapter}
513:      />
515:      {/* getRowAriaLabel is accepted for API parity; mantine-datatable doesn't
516:          expose per-row aria props directly. Suppress unused-var by reading. */}
517:      {getRowAriaLabel ? null : null}
```

## Suggested fix
In `DataTable`, translate `onRowClick`/`getRowAriaLabel` into mantine-datatable's `rowAttributes` (`tabIndex: 0`, `role: "button"`, `aria-label`, `onKeyDown` for Enter/Space) — fixes every consumer at once.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: datatable-row-click-no-keyboard|src/AutoNate.Spa/src/components/data-table/DataTable.tsx|onRowClick -->

---

## archived-13 — AgentSidebar: no focus on open, no focus return on close, Escape does not dismiss

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
`AgentChatTrigger` toggles `isOpen`; the `<aside>` (:356-367) has no FocusTrap, no open-time `focus()`, no Escape handler (`grep Escape src/agent` → 0), and no focus restore to the trigger. The only `focus()` in the file (:306) fires on a streaming true→false transition.

## Where
`src/AutoNate.Spa/src/agent/AgentSidebar.tsx:356-367`

## Why it matters
After activating the assistant, a screen-reader user's focus stays on the header button; reaching the composer means tabbing blind through the whole page; on close focus is orphaned. The AI assistant is unusable without a mouse. WCAG 2.4.3 + 2.1.2 / 508 §502.

## Evidence
```
356:  return (
357:    <aside
358:      className={[
359:        "agent-sidebar",
360:        isOpen ? "agent-sidebar--open" : "",
366:      aria-hidden={!isOpen}
367:    >
```
`grep -n 'focus()\|Escape\|FocusTrap' agent/AgentSidebar.tsx` → only line 306.

## Suggested fix
Wrap `agent-sidebar__inner` in Mantine `<FocusTrap active={isOpen}>`, add `useHotkeys([["Escape", close]])`, and restore focus to the trigger on close.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: no-focus-management|src/AutoNate.Spa/src/agent/AgentSidebar.tsx|aside -->

---

## archived-14 — badgeTextColor picks button/pill text by YIQ brightness, not WCAG luminance — feeds --mantine-primary-color-contrast

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:spa`

## What
`badgeTextColor` (statusAppearance.ts:17-25) chooses `#111111` vs `#ffffff` from `(0.299R+0.587G+0.114B) > 160`. Its result becomes `--mantine-primary-color-contrast` via siteAppearance.ts:357 — the text colour of every filled primary Button — and every status pill.

## Where
`src/AutoNate.Spa/src/lib/statusAppearance.ts:17-25; src/AutoNate.Spa/src/lib/siteAppearance.ts:357`

## Why it matters
For a mid-tone accent an admin picks, the heuristic returns white on a colour that computes below 4.5:1 (e.g. `#00acac` → white → 2.80:1). The current default `#008080` passes at 4.77:1, hence medium. WCAG 1.4.3 / 508 §501.

## Evidence
```
23:  const luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
24:  return luminance > 160 ? "#111111" : "#ffffff";
```

## Suggested fix
Reuse `relativeLuminance`/`contrastRatio` from siteAppearance.ts and return whichever of `#111111`/`#ffffff` yields the higher ratio; surface a warning in the appearance editor when neither reaches 4.5:1.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: contrast-heuristic-not-wcag|src/AutoNate.Spa/src/lib/statusAppearance.ts|badgeTextColor -->

---

## archived-15 — No route-change focus management: SPA navigation leaves focus on the consumed nav link

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:spa`

## What
`#content` has `tabIndex={-1}` for the skip link (AppShell.tsx:30) but nothing calls `focus()` on navigation: `grep -rn 'focus()' src/routes src/shell` → 0.

## Where
`src/AutoNate.Spa/src/shell/AppShell.tsx:29-33; src/AutoNate.Spa/src/routes/appRoutes.tsx`

## Why it matters
On every navigation the screen reader stays where it was and announces nothing; Tab resumes in the header instead of the new page. WCAG 2.4.3 / 508 §502 (2.4.11 in 2.2 is a cheap add-on).

## Evidence
```
29:            <MantineAppShell.Main>
30:              <div id="content" className="app-shell-content" tabIndex={-1}>
31:                <Outlet />
32:              </div>
33:            </MantineAppShell.Main>
```

## Suggested fix
In `AppShell`: `const { pathname } = useLocation(); useEffect(() => document.getElementById("content")?.focus(), [pathname]);`

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: no-route-focus|src/AutoNate.Spa/src/shell/AppShell.tsx|content -->

---

## archived-16 — Archived rows are distinguished by colour alone (.row-archived)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:spa`

## What
`.row-archived td { color: var(--mantine-color-dimmed) }` (widgets.css:98-100) is the only archived cue in RecordList, EdgeTypeList and RecordTypeList; `grep -i badge` on the archived branch of RecordList.tsx → 0.

## Where
`src/AutoNate.Spa/src/widgets.css:98-100; consumers RecordList.tsx, EdgeTypeList.tsx, RecordTypeList.tsx`

## Why it matters
Screen-reader users get no signal that a record is archived; colour-deficient users see only a lightness shift. WCAG 1.4.1 / 508 §502.

## Evidence
```
98: .row-archived td {
99:     color: var(--mantine-color-dimmed);
100: }
```

## Suggested fix
Add an "Archived" Mantine `<Badge>` in the name/status cell when `isArchived` (also fixes `.notification-unread` which is weight-only).

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: color-only-status|src/AutoNate.Spa/src/widgets.css|row-archived -->

---

## archived-17 — Login: sign-in error Alert has no role="alert" and both inputs autoFocus

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:spa`

## What
The invalid/locked error is a static `<Alert>` (Login.tsx:112) with no `role="alert"` (`grep role="alert" Login.tsx` → 0); both `TextInput` and `PasswordInput` set `autoFocus`.

## Where
`src/AutoNate.Spa/src/pages/login/Login.tsx:112-115, :151-152`

## Why it matters
On a failed login the error is never announced — the user is left on a form that appears to have done nothing. Autofocus skips heading/brand so the user lands mid-form with no context. WCAG 3.3.1 (+4.1.3) / 508 §502.

## Evidence
```
112:          <Alert color="red" variant="filled" radius="md">
113:            {error === "locked"
151:              autoComplete="username"
152:              autoFocus={prefilledUsername.length === 0}
```

## Suggested fix
Add `role="alert"` to the `<Alert>` and drop the `autoFocus` props (or keep only the password one when a username is prefilled).

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: no-live-region|src/AutoNate.Spa/src/pages/login/Login.tsx|error-alert -->

---

## archived-18 — Only the datastores pages set a per-route document title; everything else reads "AutoNate"

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
`useDocumentTitle` exists but is used in 4 files (all admin datastores); `SiteAppearanceProvider.tsx:40` resets `document.title` to the site name globally.

## Where
`src/AutoNate.Spa/src/providers/SiteAppearanceProvider.tsx:40; src/AutoNate.Spa/index.html:7`

## Why it matters
Screen-reader users orient by title on navigation; tab and history lists are indistinguishable. WCAG 2.4.2 / 508 §502.

## Evidence
```
40:    document.title = effectiveAppearance.siteName;
```
`grep -rln useDocumentTitle src --include=*.tsx | wc -l` → 4.

## Suggested fix
Call the existing `useDocumentTitle("<Page> · <Site>")` in each top-level page component, or drive it from the route table.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: no-per-route-title|src/AutoNate.Spa/src/providers/SiteAppearanceProvider.tsx|document.title -->

---

## archived-19 — explain_workflow agent skill returns full BPMN of every workflow model with no WorkflowModel:View check

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:api`

## What
`find_workflow` / `explain_workflow` call `IWorkflowModelStore.ListAsync` / `GetAsync` with no authorizer (`grep -c 'IAuthorizer\|AuthorizeAsync'` → 0) and return `bpmnXml`. The HTTP routes over the same store are gated `RequireKindPermission(WorkflowModel, View)` / `RequirePermission(WorkflowModel, View, "id")` (WorkflowEndpoints.cs:29-44). `IAgentSkill.cs:27-29` states the rule this breaks: skills MUST route reads through stores that already gate by IAuthorizer.

## Where
`src/AutoNate.Web/Services/Agent/Skills/ExplainWorkflowSkill.cs:109-142`

## Why it matters
Any signed-in user with zero workflow grants opens the chat and asks "explain workflow X" → receives the full process definition (service-task endpoints, behaviour keys) for every model, while `GET /api/workflows/{id}` returns 403 for the same user.

## Evidence
```
109:        var store = context.Services.GetRequiredService<IWorkflowModelStore>();
110:        var model = await store.GetAsync(workflowId, ct);
141:                publishedVersionNumber = model.PublishedVersionNumber,
142:                bpmnXml = model.BpmnXml
```
Path: `POST /api/agent/conversations/{id}/messages` (owner check only) → `SkillRegistry` (no per-skill filtering) → `InvokeExplainWorkflowAsync` → unfiltered store.

## Suggested fix
Gate with `IAuthorizer.AuthorizeAsync(ctx.Session.User, Actions.View, new EntityRef(EntityKinds.WorkflowModel, id), ct)` before returning (list: filter by the same check) — the in-repo pattern is `OperateWorkflowExecutionsSkill.CanExecutionAsync` (:168). Regression test: no-grant user asks the skill → error, not BPMN.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: skill-bypasses-endpoint-gate|src/AutoNate.Web/Services/Agent/Skills/ExplainWorkflowSkill.cs|InvokeExplainWorkflowAsync -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Fixed on `master` via archived-167.

**Evidence**
- `AgentSkillAuthorizationTests` 11/11. The denial paths wire stores that **throw if touched**, so the refusal is proven to happen *before* the read rather than as post-filtering. Includes a test that a denial and a genuine miss are worded identically, so `explain_workflow` can't enumerate workflow ids.
- Red check: deleting the guard from `ExplainWorkflowSkill` fails four tests, including the permanent gate — the guard is real, not decorative.
- Full `AutoNate.Web.Tests` **1432 passed / 1 failed** (the failure is archived-163's known intermittent flake, previously reproduced on `master` unchanged), full E2E **141 passed / 0 failed / 2 skipped**.

**The permanent part:** every `IAgentSkill` is now classified as `Authorizer`, `GatedStore`, `ActorScopedStore` or `NoGatedData`. A new skill fails the test until classified; one classified `Authorizer` fails if the call is removed. Recorded in `docs/codebase/Architecture.md`.

Two corrections found while classifying, worth keeping: `LookupRecordsSkill`/`ManageRecordsSkill` mention `IAuthorizer` only in **comments** (a naive grep mis-reads them as guarded) — they are safe because `EfCoreRecordStore` applies `IAuthorizer` internally, which I verified in the store; and the notes skills authorize via `IContentAuthorizer`, not `IAuthorizer`, which is why the gate matches call sites rather than one type name.

</details>

---

## archived-20 — list/get_system_issue agent skills expose administrative issues (with exception text) to non-admins

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:api`

## What
`list_system_issues` / `get_system_issue` call `ISystemIssueStore.ListAsync` / `GetAsync` with no authorizer; the file has zero references to `IAuthorizer`, `Actions` or `EntityKinds`. `SystemIssueEndpoints.cs:64-66` gates the same reads on `RequireKindPermission(SystemIssue, View)`; `CoreEntityTypes.cs:165` documents SystemIssue as "every issue is administrative".

## Where
`src/AutoNate.Web/Services/Agent/Skills/AnalyzeSystemIssueSkill.cs:111-112`

## Why it matters
`UnhandledExceptionRecorder` writes exception messages into `FactsJson`, which the skill returns verbatim — a non-admin reads production stack traces and failing IDs through chat while `GET /api/system-issues` returns 403.

## Evidence
```
111:        var store = context.Services.GetRequiredService<ISystemIssueStore>();
112:        var issue = await store.GetAsync(id, ct);
```

## Suggested fix
Add the kind-level guard `ProjectionsSkill.cs:103-112` uses, with `EntityKinds.SystemIssue` / `Actions.View`, returning `Error("SystemIssue:view permission required.")`. Add a gate test that enumerates every `IAgentSkill` and asserts it either calls `IAuthorizer` or only uses `*ForUserAsync` stores — the permanent version of this finding.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: skill-bypasses-endpoint-gate|src/AutoNate.Web/Services/Agent/Skills/AnalyzeSystemIssueSkill.cs|InvokeGetAsync -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Fixed on `master` via archived-167.

**Evidence**
- `AgentSkillAuthorizationTests` 11/11. The denial paths wire stores that **throw if touched**, so the refusal is proven to happen *before* the read rather than as post-filtering. Includes a test that a denial and a genuine miss are worded identically, so `explain_workflow` can't enumerate workflow ids.
- Red check: deleting the guard from `ExplainWorkflowSkill` fails four tests, including the permanent gate — the guard is real, not decorative.
- Full `AutoNate.Web.Tests` **1432 passed / 1 failed** (the failure is archived-163's known intermittent flake, previously reproduced on `master` unchanged), full E2E **141 passed / 0 failed / 2 skipped**.

**The permanent part:** every `IAgentSkill` is now classified as `Authorizer`, `GatedStore`, `ActorScopedStore` or `NoGatedData`. A new skill fails the test until classified; one classified `Authorizer` fails if the call is removed. Recorded in `docs/codebase/Architecture.md`.

Two corrections found while classifying, worth keeping: `LookupRecordsSkill`/`ManageRecordsSkill` mention `IAuthorizer` only in **comments** (a naive grep mis-reads them as guarded) — they are safe because `EfCoreRecordStore` applies `IAuthorizer` internally, which I verified in the store; and the notes skills authorize via `IContentAuthorizer`, not `IAuthorizer`, which is why the gate matches call sites rather than one type name.

</details>

---

## archived-21 — GET /api/content/locators/{n} enumerates the whole content tree to any signed-in user; OpenToAuthenticated rationale is false

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:api`

## What
The handler performs five unfiltered `db.*.Where(x => x.Locator == locator)` lookups and returns kind + GUID + full ancestor chain with no `IContentAuthorizer` call (0 authorizer references between :150 and :216), then declares `.OpenToAuthenticated("…Locators are sequential, so authenticated mapping doesn't leak meaningful info.")`. The sibling `/tree` endpoint (:137) does filter via `GetAllowedIdsAsync`.

## Where
`src/AutoNate.Web/Endpoints/ContentLocatorEndpoints.cs:216-220`

## Why it matters
Locators are sequential longs: `for i in 1..N: GET /api/content/locators/$i` gives any authenticated user a complete map of every project/cabinet/notebook/page/note in the tenant — including entities the authorizer would 404 — plus the GUIDs to feed other endpoints. The rationale asserts the opposite of what the code does.

## Evidence
```
216:        }).OpenToAuthenticated(
217:            "Locator → (kind, id, ancestor chain) lookup. Returns identifiers " +
218:            "only; the SPA's follow-up fetch for the entity is gated by " +
219:            "IContentAuthorizer. Locators are sequential, so authenticated " +
220:            "mapping doesn't leak meaningful info.");
```

## Suggested fix
After resolving `(kind, id)`, call `authorizer.AuthorizeAsync(http.User, kind, id, Actions.View, ct)` and return NotFound on deny — the pattern at ContentLocatorEndpoints.cs:47-53 — then change the marker to `AuthorizedInHandler`.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: rationale-drift|src/AutoNate.Web/Endpoints/ContentLocatorEndpoints.cs|locator-lookup -->

---

## archived-22 — GET /api/code-transformers/{id} returns transformer source to any authenticated user; AuthorizedInHandler marks a handler with no check

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:api`

## What
The detail route (CodeTransformerEndpoints.cs:35-42) calls `store.GetAsync` and returns the DTO including `Code`, with only the group-level `RequireAuthorization()`; the list endpoint above it is `RequireKindPermission(Transformer, List)`. `Transformer:View` is declared in `AnalyticsEntityTypes.cs:101` but nothing enforces it.

## Where
`src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs:35-42`

## Why it matters
A user without `transformer:list` who obtains any transformer GUID (list endpoint for anyone with the list grant, pipeline definitions, audit events) reads its full Python/JS body — including transformers flagged `IsUnsafe`, which commonly carry credentials or internal API shapes.

## Evidence
```
35:        group.MapGet("/{id:guid}", async (Guid id, ICodeTransformerStore store, CancellationToken ct) =>
36:        {
37:            var row = await store.GetAsync(id, ct);
38:            return row is null ? Results.NotFound() : Results.Ok(MapDto(row));
39:        }).AuthorizedInHandler(
```

## Suggested fix
`.RequireKindPermission(EntityKinds.Transformer, Actions.View)` (already declared, currently inert). Regression: no-grant → 403 in a new `TransformerEnforcementTests.cs`.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: rationale-drift|src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs|detail -->

---

## archived-23 — POST /api/code-transformers gates on (Transformer, Run) regardless of requested kind — a run grant confers authoring rights

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:api`

## What
Create is `RequireKindPermission(EntityKinds.Transformer, Actions.Run)` (:81) even when `request.Kind == analyzer`, while the inline unsafe check five lines up uses `MapKindToEntityKind(request.Kind)` (:54-58) — two different kinds gating one request. `Actions.Create`/`Edit` are not in the Transformer/Analyzer `actions[]` (AnalyticsEntityTypes.cs:101,108), so there is no correct token to use today; `Analyzer:run`/`Analyzer:view` are consequently never enforced.

## Where
`src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs:54-58, :81-82; src/AutoNate.Web/Authorization/EntityTypes/AnalyticsEntityTypes.cs:101, :108`

## Why it matters
`AnalyticsEntityTypes.cs:118-120` says Run is gated separately from Edit "so an operator can hand out execution rights without authoring rights" — but granting `transformer:run` so a user can execute pipeline nodes actually lets them author and store arbitrary sandboxed code that later pipeline runs execute; and an admin granting `analyzer:run` changes nothing.

## Evidence
```
54:            if (request.IsUnsafe)
56:                var decision = await authorizer.AuthorizeAsync(
57:                    http.User, Actions.ExecuteUnsafe,
58:                    new EntityRef(MapKindToEntityKind(request.Kind), string.Empty), ct);
81:        }).RequireKindPermission(EntityKinds.Transformer, Actions.Run)
```

## Suggested fix
Add `Actions.Create`/`Actions.Edit` to both kinds' `actions[]`, compute the kind once via `MapKindToEntityKind(request.Kind)` and gate create on `Create` and update on `Edit` (same shape as RecordEndpoints.cs:237).

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: wrong-action-token|src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs|create -->

---

## archived-24 — Dataset/Pipeline `schedule`, Pipeline `cancel` and the entire PipelineRun kind are grantable but inert

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
`Actions.Schedule` (Dataset, Pipeline), `Actions.Cancel` (Pipeline) and every `PipelineRun` action are declared and offered by `/api/admin/registry`, but `grep -rn 'Actions.Schedule'` outside EntityTypes → 0 and `EntityKinds.PipelineRun` outside EntityTypes/Program.cs → 0. `POST /api/pipelines/{id}/runs/{runId}/cancel` is gated on `Actions.Run` (PipelineEndpoints.cs:167).

## Where
`src/AutoNate.Web/Authorization/EntityTypes/AnalyticsEntityTypes.cs:70, :118-129; src/AutoNate.Web/Endpoints/PipelineEndpoints.cs:167`

## Why it matters
An admin grants `pipeline:cancel` to an on-call operator; the operator still gets 403 on cancel (which needs `run` — the same grant that lets them start runs). `pipeline:schedule` / `dataset:schedule` do nothing: cron edits ride on `Edit`. Grants that do nothing are admin confusion and a false sense of control.

## Evidence
```
122:            Actions.Run, Actions.Schedule, Actions.Cancel
127:        kind: EntityKinds.PipelineRun,
129:        actions: new[] { Actions.View, Actions.List, Actions.Cancel });
```

## Suggested fix
Gate cancel on `Actions.Cancel`, gate the cron fields on `Actions.Schedule`, and either enforce `PipelineRun` on the run-list/run-detail routes or delete the kind. Permanent version: a test that diffs every declared (kind, action) against `RequirePermission`/`RequireKindPermission` call sites and fails on inert pairs.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: grantable-but-inert|src/AutoNate.Web/Authorization/EntityTypes/AnalyticsEntityTypes.cs|PipelineRun -->

---

## archived-25 — `document` and `folder` kinds are enforced on 22 routes but absent from the entity registry, so grants can't be authored from the Grants page

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`CoreEntityTypes.All` (:25-30) lists 17 kinds; `EntityKinds.Document`/`Folder` appear in neither CoreEntityTypes nor AnalyticsEntityTypes (0 hits), yet `RequirePermission(EntityKinds.Document, …)` appears on 22 route registrations and `ContentAuthorizer.cs:215` honours `/document/…` and `/folder/…` selectors.

## Where
`src/AutoNate.Web/Authorization/EntityTypes/CoreEntityTypes.cs:24-30`

## Why it matters
`/api/admin/registry` drives the Grants admin picker; admins get no document/folder kind, action list or tags, so grants the runtime honours cannot be discovered or authored from the standard page — only via `ContentPermissionOverrideEndpoints`, which hardcodes its own `DocumentGrantableActions` (a second source of truth).

## Evidence
```
26:            User!, Group!, Role!, RecordType!, Record!,
27:            WorkflowModel!, WorkflowExecution!, WorkflowTask!, Plugin!,
28:            Form!, ExternalConnection!, SystemIssue!, SiteConfig!,
29:            Project!, Cabinet!, Notebook!, Page!
```

## Suggested fix
Add `Folder` and `Document` `EntityTypeDefinition`s alongside `Page` (actions View/Edit/Delete, empty tags), append to `All`, and have `ContentPermissionOverrideEndpoints` read the vocabulary from `IEntityRegistry`.

_Found by `/n8-audit authorization` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unregistered-enforced-kind|src/AutoNate.Web/Authorization/EntityTypes/CoreEntityTypes.cs|All -->

---

## archived-26 — add-permission-gate skill tells you to use `usePermissionPrefetch`, which does not exist

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:high`, `area:docs`

## What
Step 6 says to add the key to "the page's `usePermissionPrefetch` list". `grep -rn usePermissionPrefetch src .claude` → only that SKILL.md line. The real API is `usePermissionChecks(checks)` + `permissionKey(check)` in `src/AutoNate.Spa/src/hooks/usePermissionChecks.ts:20,38`, used e.g. at `WorkflowExecutions.tsx:91`.

## Where
`.claude/skills/add-permission-gate/SKILL.md:59`

## Why it matters
A dead-end instruction in the one skill that gates security-relevant UI; the follower either invents the hook or skips the step.

## Evidence
```
59: …use `permissions.has(permissionKey({ kind, action, id }))` and add the key to the page's `usePermissionPrefetch` list so it's loaded before render.
```

## Suggested fix
Replace step 6 with: build a `PermissionCheck[]`, call `usePermissionChecks(checks)` from `@/hooks/usePermissionChecks`, read `data[permissionKey(check)]`.

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: skill-drift|.claude/skills/add-permission-gate/SKILL.md|usePermissionPrefetch -->

---

## archived-27 — plugin-creator skill omits four live IPluginContext surfaces (AgentSkills, Connectors, Transformers, Analyzers)

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:medium`, `area:docs`

## What
Line 24 enumerates `Code, SchemaName, Hooks, Data, Menus, Behaviors, Projections, HostServices`; `IPluginContext.cs` also exposes `AgentSkills`, `Connectors`, `Transformers`, `Analyzers` (0 mentions of any in the skill).

## Where
`.claude/skills/plugin-creator/SKILL.md:24; src/AutoNate.Plugin.Abstractions/IPluginContext.cs:51-70`

## Why it matters
Same failure mode as the historical missing-`Cleanup` bug — a plugin author reading the skill never learns four of the host's extension points exist.

## Evidence
`grep -oE 'IPlugin[A-Za-z]+ [A-Z][A-Za-z]+ \{' IPluginContext.cs` → Data Menus Behaviors Projections **AgentSkills Connectors Transformers Analyzers**; `grep -c 'AgentSkills\|Connectors\|Transformers\|Analyzers' SKILL.md` → 0.

## Suggested fix
Add the four members to line 24 and a one-line bullet each in "What a plugin can do".

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: skill-drift|.claude/skills/plugin-creator/SKILL.md|IPluginContext -->

---

## archived-28 — add-record-event-type skill says there are four RecordEventEnvelope emit sites; there are six, and publishing now goes through the outbox

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:medium`, `area:docs`

## What
Line 41: "there are four (Created, Updated, StatusChanged, Deleted/Updated-on-restore)". `grep -c 'PublishAsync(new RecordEventEnvelope' EfCoreRecordStore.cs` → 6 (adds `AssigneesChanged` :666 and `Purged` :732). `RecordEventPublisher.cs:104` now enqueues via `IAuditEventOutbox` rather than posting to Dapr inline.

## Where
`.claude/skills/add-record-event-type/SKILL.md:41`

## Why it matters
A follower searching for "four" call sites stops early and misses the newer event families, and won't know the outbox is in the path.

## Evidence
`RecordEventTypes.` hits in EfCoreRecordStore.cs: 459 Created, 629 Updated, 647 StatusChanged, 666 AssigneesChanged, 732 Purged, 782 Deleted/Restored.

## Suggested fix
Change "four" → "six" with the full list and add a sentence noting `DaprRecordEventPublisher` enqueues via `IAuditEventOutbox`.

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: skill-drift|.claude/skills/add-record-event-type/SKILL.md|emit-sites -->

---

## archived-29 — CodeTransformerEndpoints header comment points at AnalyzerEndpoints.cs, which does not exist

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:low`, `area:api`

## What
Lines 12-16 refer to catalog endpoints "in `TransformerEndpoints.cs` / `AnalyzerEndpoints.cs`"; only `TransformerEndpoints.cs` exists.

## Where
`src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs:14`

## Why it matters
Anyone tracing the analyzer read surface looks for a file that isn't there and can't tell whether it was deleted or never written.

## Evidence
```
14: // in TransformerEndpoints.cs / AnalyzerEndpoints.cs would surface these
```
`ls Endpoints/AnalyzerEndpoints.cs` → No such file.

## Suggested fix
Drop `/ AnalyzerEndpoints.cs` or say "and a future `AnalyzerEndpoints.cs` (not yet written)".

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: stale-comment|src/AutoNate.Web/Endpoints/CodeTransformerEndpoints.cs|header-comment -->

---

## archived-30 — Dead SPA module (trial-delete verified): pages/notes/CabinetMenu.tsx

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
`CabinetMenu` has no importer. Verified by the real protocol: moved the file aside, cleared `tsconfig.*.tsbuildinfo`, `npx tsc -b --force` → no `Cannot find module` for this path (four other candidates from the same sweep DID fail the trial-delete and are not reported).

## Where
`src/AutoNate.Spa/src/pages/notes/CabinetMenu.tsx`

## Why it matters
A hand-rolled dropdown (no `role="menu"`, no arrow-key focus) that reads as live API to anyone browsing `pages/notes`; if it is ever wired it would reintroduce a 508 finding.

## Evidence
Trial-delete 2026-08-30: `mv CabinetMenu.tsx CabinetMenu.tsx.bak && rm -f tsconfig.*.tsbuildinfo && npx tsc -b --force` → zero errors referencing this module. Note: `grep` alone is unreliable here — `WorkflowStudio.tsx` is classified as binary by grep.

## Suggested fix
`git rm src/AutoNate.Spa/src/pages/notes/CabinetMenu.tsx` (re-run the trial delete first if the tree has moved on).

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: dead-module|src/AutoNate.Spa/src/pages/notes/CabinetMenu.tsx|CabinetMenu -->

---

## archived-31 — Dead SPA module (trial-delete verified): pages/dynamic-page/jsxBindings.ts

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
`useJsxBindings` has no importer; survived the same trial-delete + `npx tsc -b --force` run with zero errors.

## Where
`src/AutoNate.Spa/src/pages/dynamic-page/jsxBindings.ts`

## Why it matters
Looks like intentionally-staged feature code; either wire it or demote it so the next build surfaces any consumer.

## Evidence
Trial-delete 2026-08-30 → no `Cannot find module '…/jsxBindings'`.

## Suggested fix
Prefer demoting (`export` → local) if the feature is still planned; otherwise `git rm`.

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: dead-module|src/AutoNate.Spa/src/pages/dynamic-page/jsxBindings.ts|useJsxBindings -->

---

## archived-32 — 22 of 23 eslint-disable comments in the SPA carry no reason

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
Only `hooks/useBusSubscription.ts:34` uses the `-- reason` suffix; the other 22 suppress `react-hooks/exhaustive-deps`, `no-console` or `no-explicit-any` bare (`grep -rIn eslint-disable src | grep -v ' -- ' | wc -l` → 22), eight of them in `AutoConfigForm.tsx` alone.

## Where
`src/AutoNate.Spa/src (22 sites; e.g. widgets/AutoConfigForm.tsx:48,51,67,70,134,165,184,188; pages/records/RecordForm.tsx:106)`

## Why it matters
A bare `exhaustive-deps` disable is indistinguishable from a stale-closure bug someone silenced.

## Evidence
Good example: `useBusSubscription.ts:34 // eslint-disable-next-line react-hooks/exhaustive-deps -- channelsKey is the change signal`.

## Suggested fix
Adopt the `-- reason` suffix repo-wide (start with the seven `exhaustive-deps` ones) and enable `eslint-comments/require-description` to enforce it.

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unexplained-suppression|src/AutoNate.Spa/src/widgets/AutoConfigForm.tsx|eslint-disable -->

---

## archived-33 — Phase-numbered test files (ProjectionFrameworkPhase2/3Tests, Phase7DocumentImportTests) name delivery phases, not behaviour

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:tests`

## What
`ProjectionFrameworkPhase2Tests.cs`, `ProjectionFrameworkPhase3Tests.cs`, `Phase7DocumentImportTests.cs` sit alongside `ProjectionFrameworkTests.cs`; the phase numbers resolve to nothing in the repo now that `docs/plans/` is historical.

## Where
`tests/AutoNate.Web.Tests/`

## Why it matters
A reader can't tell what's covered without opening all three.

## Evidence
`find tests -name '*Phase*Tests.cs'` → 3 files.

## Suggested fix
Rename to behaviour-named files (e.g. `ProjectionRetentionTests.cs`, `DocumentImportTests.cs`) next time they're touched.

_Found by `/n8-audit cleanup` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: stale-phase-marker|tests/AutoNate.Web.Tests/ProjectionFrameworkPhase2Tests.cs|filename -->

---

## archived-34 — flowable-extension: spring-boot 4.0.x has a CRITICAL advisory (fix ≥ 4.0.6) plus a high

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:critical`, `area:flowable`

## What
Dependabot reports one critical and one high advisory against `org.springframework.boot:spring-boot` in `flowable-extension/pom.xml`; first patched version 4.0.6. Related: `spring-boot-autoconfigure` (medium, fix 4.0.7) and `spring-web` (medium, fix 7.0.8).

## Where
`flowable-extension/pom.xml`

## Why it matters
The Flowable extension runs inside the workflow JVM and terminates the workflow-behavior callback channel; a critical Spring Boot advisory is exploitable wherever that JVM is network-reachable.

## Evidence
`gh api repos/nathanpond/AutoNate/dependabot/alerts?state=open` → `critical maven flowable-extension/pom.xml org.springframework.boot:spring-boot 4.0.6`, `high … spring-boot 4.0.6`. Full list: https://github.com/nathanpond/AutoNate/security/dependabot

## Suggested fix
Bump Spring Boot to ≥ 4.0.7 in the pom (autoconfigure/spring-web follow), rebuild the custom Flowable image in infra/, run the workflow E2E specs. `dependabot.yml` now includes the maven ecosystem so future bumps arrive as PRs.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:critical`._

<!-- fingerprint: vulnerable-dependency|flowable-extension/pom.xml|spring-boot -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Resolved by Dependabot archived-96 (merge `223c937d` on `master`): Spring Boot 4.0.2 → 4.1.1, spring-web 7.0.9, jackson-databind 2.20.2 → 2.22.2.

**Verification (2026-08-31):** local JDK 21.0.12 / Maven 3.9.16 `mvn test` → 34/34 before and after; `docker compose build flowable` (extension compiled in `maven:3.9.9-eclipse-temurin-21`) → BUILD SUCCESS 34/34; `infra/ensure-up.sh` rebuilt and recreated the Flowable container (healthy, REST 200); workflow E2E specs `WorkflowStudioTests|WorkflowExecutionTests|WorkflowOverrideTests` → 9/9 against the rebuilt image. Dependabot alerts for `flowable-extension/pom.xml` should clear on its next scan.

</details>

---

## archived-35 — flowable-extension: jackson-databind has high + medium advisories (fix ≥ 2.21.5)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:flowable`

## What
Five Dependabot alerts (2 high, 3 medium) against `com.fasterxml.jackson.core:jackson-databind`; first patched 2.21.4 / 2.21.5.

## Where
`flowable-extension/pom.xml`

## Why it matters
jackson-databind deserialises the callback payloads exchanged with AutoNate.Web; databind advisories are typically DoS or gadget-chain deserialization.

## Evidence
`gh api …/dependabot/alerts` → `high maven flowable-extension/pom.xml com.fasterxml.jackson.core:jackson-databind 2.21.4` ×2, medium ×3.

## Suggested fix
Pin `jackson-databind` ≥ 2.21.5 (or take it transitively from the Spring Boot bump) and rebuild the Flowable image.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: vulnerable-dependency|flowable-extension/pom.xml|jackson-databind -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Resolved by Dependabot archived-96 (merge `223c937d` on `master`): Spring Boot 4.0.2 → 4.1.1, spring-web 7.0.9, jackson-databind 2.20.2 → 2.22.2.

**Verification (2026-08-31):** local JDK 21.0.12 / Maven 3.9.16 `mvn test` → 34/34 before and after; `docker compose build flowable` (extension compiled in `maven:3.9.9-eclipse-temurin-21`) → BUILD SUCCESS 34/34; `infra/ensure-up.sh` rebuilt and recreated the Flowable container (healthy, REST 200); workflow E2E specs `WorkflowStudioTests|WorkflowExecutionTests|WorkflowOverrideTests` → 9/9 against the rebuilt image. Dependabot alerts for `flowable-extension/pom.xml` should clear on its next scan.

</details>

---

## archived-36 — SPA: direct deps axios, react-router-dom and vite carry high advisories

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:spa`

## What
`npm audit` in src/AutoNate.Spa: 21 advisories (12 high, 9 moderate). Direct: `axios` (7 high/medium/low alerts, fix 1.16.0–1.18.0), `react-router-dom`/`react-router` (fix 7.18.2), `vite` (fix 8.0.16). All have `fixAvailable: true`.

## Where
`src/AutoNate.Spa/package.json, package-lock.json`

## Why it matters
axios is the SPA's HTTP client for every API call; vite is the dev server that proxies to the API. Fixes are semver-compatible bumps.

## Evidence
`npm audit --json` → `{"moderate":9,"high":12,"critical":0,"total":21}`; Dependabot: axios 6×high, react-router 3×high, vite 1×high.

## Suggested fix
`npm update axios react-router-dom vite` in src/AutoNate.Spa, then `npm run lint && npm run build` and the E2E suite.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: vulnerable-dependency|src/AutoNate.Spa/package.json|axios -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Resolved on `master` by the Dependabot merges of 2026-08-31 (archived-157 for axios/react-router; vite via the earlier SPA group). Verified against `src/AutoNate.Spa/package-lock.json` at `f28a1c85`:

| package | locked | fixed in (per this issue) |
|---|---|---|
| axios | **1.20.0** | ≥ 1.18.0 |
| react-router-dom / react-router | **7.18.3** | ≥ 7.18.2 |
| vite | **8.2.2** | ≥ 8.0.16 |

`npm audit` now reports **0 direct-dependency advisories** for these (9 vulnerable packages total, all transitive — `nanoid`, `lodash-es` and the mermaid/chevrotain chain under `@excalidraw/mermaid-to-excalidraw`; `@excalidraw/excalidraw` shows up only as their carrier). GitHub's open alerts on the SPA manifest agree: 9, all `nanoid`/`lodash-es`. Those are tracked in archived-37.

</details>

---

## archived-37 — SPA: transitive advisories (brace-expansion, nanoid, postcss, js-yaml, lodash-es, form-data, ws, immutable, dompurify, mermaid)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:spa`

## What
Remaining SPA alerts are transitive: brace-expansion (6 high), nanoid (4 high, 2 medium), postcss (2 high, 1 medium), js-yaml (2 high, 1 medium), lodash-es, form-data, ws, immutable (high), dompurify (10 alerts across 3.4.6–3.4.13, one with no fix), mermaid (5). `nanoid` via `@excalidraw/excalidraw@0.17.6` needs a major bump of excalidraw.

## Where
`src/AutoNate.Spa/package-lock.json`

## Why it matters
Mostly build-time or ReDoS-class; dompurify/mermaid matter because they sanitise/render user-authored markdown in notes and the agent panel.

## Evidence
Dependabot alert list grouped by package (see security tab); `npm audit` reports `fixAvailable: true` for all but the excalidraw-pinned nanoid.

## Suggested fix
`npm audit fix` (non-breaking) for the lockfile-only bumps; evaluate `@excalidraw/excalidraw` upgrade separately; verify dompurify's remaining unfixed advisory isn't in a code path the SPA uses.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: vulnerable-dependency|src/AutoNate.Spa/package-lock.json|transitive -->

---

## archived-38 — services/hocuspocus: ws and form-data have high advisories

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:services`

## What
`npm audit` in services/hocuspocus: 2 high (`ws` fix 8.21.0, `form-data` fix 4.0.6), both transitive with fixes available.

## Where
`services/hocuspocus/package-lock.json`

## Why it matters
`ws` is the WebSocket server every collaborative editing session rides on; the ws advisory class is typically DoS via crafted frames/headers.

## Evidence
`npm audit --json` → `{"high":2,"total":2}`; Dependabot: `high npm services/hocuspocus/package-lock.json ws 8.21.0`, `form-data 4.0.6`.

## Suggested fix
`npm audit fix` in services/hocuspocus and rebuild the sidecar image.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: vulnerable-dependency|services/hocuspocus/package-lock.json|ws -->

---

## archived-39 — services/executor has no package-lock.json — its dependencies cannot be audited or pinned

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:services`

## What
`services/executor/package.json` exists but there is no `package-lock.json` (and no node_modules locally); `npm audit` returns no data and Dependabot cannot scan it.

## Where
`services/executor/`

## Why it matters
The executor runs workflow scripts — the one sidecar where supply-chain pinning matters most — and it is the only manifest in the repo with no lock, so every install resolves fresh and no advisory ever surfaces.

## Evidence
`ls services/executor/package-lock.json` → No such file; `npm audit` → "no audit data"; Dependabot alert list has no executor manifest.

## Suggested fix
`cd services/executor && npm install --package-lock-only`, commit the lockfile, and confirm the `npm` entry for `/services/executor` in `.github/dependabot.yml` starts producing alerts.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: no-lockfile|services/executor/package.json|lockfile -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Resolved by archived-140: `services/executor/package-lock.json` is now committed (isolated-vm 7.0.1, pyodide 0.26.4, nats 2.29.3) and the `npm` Dependabot entry for `/services/executor` will start producing alerts on the next run.

</details>

---

## archived-40 — Make jsx-a11y an error-level lint gate (directory ratchet) and add an axe-core smoke check

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:low`, `area:spa`

## What
`eslint-plugin-jsx-a11y` is wired with 9 rules at `warn` (eslint.config.js:89-97) inside a `--max-warnings=411` total budget, so ~98 jsx-a11y warnings never fail anything and new violations are free until the budget is exhausted. `axe-core` / `@axe-core/react` / `@axe-core/playwright` are not installed.

## Where
`src/AutoNate.Spa/eslint.config.js:89-97; src/AutoNate.Spa/package.json:11`

## Why it matters
This is the permanent version of every 508 finding filed today — without a gate they will regress.

## Evidence
`npx eslint src -f json` → 98 jsx-a11y warnings across click-events-have-key-events (31), no-static-element-interactions (29), no-autofocus (25), …

## Suggested fix
Add a second flat-config block setting the jsx-a11y rules to `error` scoped to already-clean directories (`src/components`, `src/shell`, `src/agent`, `src/pages/records`) and ratchet the directory list as `pages/notes` etc. are fixed; `npm i -D @axe-core/react` mounted under `import.meta.env.DEV`; one Playwright spec with `@axe-core/playwright` over login/home/records/notifications; a unit test asserting `checkContrastWarnings(DEFAULT_SITE_APPEARANCE)` is empty.

_Found by `/n8-audit 508` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: a11y-gate-missing|src/AutoNate.Spa/eslint.config.js|jsx-a11y -->

---

## archived-41 — Re-enable CA2016 (forward CancellationToken) and S108 (empty block) — the codebase is clean on both, so they'd act as regression guards

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`.editorconfig:222` sets `CA2016` to `none` and `:149` sets `S108` to `none`. The stability sweep found zero unlogged broad `catch {}` on non-teardown paths and only one unforwarded-token site (`BusWatcherStreamService.PublishAsync`), i.e. both rules are currently discipline rather than enforcement.

## Where
`.editorconfig:144-149, :218-222`

## Why it matters
These are exactly the two categories the stability audit had to check by hand across 647 catch blocks; an analyzer does it on every build.

## Evidence
`grep -n 'CA2016\|S108' .editorconfig` → 144, 149, 218, 222.

## Suggested fix
Set both to `warning`, fix or annotate the handful of sites that fire (teardown catches get a comment + `#pragma` with rationale), and keep the rationale line per rule as the suppression file convention requires.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: analyzer-suppressed|.editorconfig|CA2016 -->

---

## archived-42 — Seeded Site Configuration → Security menu items render "coming soon" stubs while the real admin pages exist under other template keys

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
`configSecurityUsers/Groups/Roles/Permissions/PermissionChecker` (and `configFormMappings`) are page templates that render `<Stub>` ("This section is a stub. Functionality coming soon.", sections.tsx:22, :92-137), while working `ManageUsers`, `AdminRoles`, `AdminGroups`, `AdminGrants`, `AdminExplain` are registered under `manageUsers`/`adminRoles`/… keys. `DatabaseSchemaInitializer.cs:1038-1044` seeds the Site Configuration menu with the stub keys.

## Where
`src/AutoNate.Spa/src/pages/admin/config/sections.tsx:22, :92-137; src/AutoNate.Spa/src/pageTemplates.tsx:76-80 (configSecurity* → Stub) vs :58-63 (real pages); src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs:1038-1044`

## Why it matters
An admin navigating the seeded Site Configuration → Security menu gets a stub instead of the shipped user/role/permission admin — the feature exists and is unreachable from the place the seed points them.

## Evidence
```
22:            <Text size="sm">This section is a stub. Functionality coming soon.</Text>
92: export function SecurityManageUsers() {
94:     <Stub
```
Seed: `'Manage Users' … '{"templateKey":"configSecurityUsers"}'` (:1038), `'Set Permissions' … configSecurityPermissions` (:1044).

## Suggested fix
Point the `configSecurity*` template keys at `<ManageUsers/>`, `<AdminGroups/>`, `<AdminRoles/>`, `<AdminGrants/>`, `<AdminExplain/>` in the page-template registry (or re-seed the menu with the live keys) and delete the stubs. E2E: seeded menu → each security page renders its real content.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: unwired-handler|src/AutoNate.Spa/src/pages/admin/config/sections.tsx|SecurityManageUsers -->

---

## archived-43 — auth_cache_version is bumped from ~15 mutation sites and never read

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:api`

## What
`AuthCacheBumper.BumpAsync` issues `UPDATE auth_cache_version SET version = version + 1` on every grant/role/group mutation (6 files call `BumpAsync`); `grep -rn auth_cache_version src` → 4 hits: DDL, seed INSERT, the UPDATE, and the class comment. No SELECT anywhere; no version-keyed auth cache exists.

## Where
`src/AutoNate.Web/Authorization/Evaluator/AuthCacheBumper.cs:6-21`

## Why it matters
A per-mutation extra DB round-trip whose stated purpose ("in-memory caches built around the version number become stale automatically") has no consumer — and anyone adding an auth cache later will assume invalidation already works.

## Evidence
`grep -rn 'auth_cache_version' --include=*.cs src | grep -iv 'UPDATE\|CREATE TABLE\|INSERT'` → only the comment at AuthCacheBumper.cs:6.

## Suggested fix
Either have `Authorizer` read and memoise on the version (then the round-trip earns its keep), or delete `AuthCacheBumper`, its 6 call sites and the table.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: write-no-read|src/AutoNate.Web/Authorization/Evaluator/AuthCacheBumper.cs|BumpAsync -->

---

## archived-44 — audit_outbox_dead_letters has a writer (park remediator) and no reader, endpoint or replay

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:api`

## What
`AuditOutboxDeadLetterParkRemediator` INSERTs abandoned outbox rows into `audit_outbox_dead_letters` (:46) and records "Parked … into audit_outbox_dead_letters" (:87). All 9 repo references are DDL/indexes/the INSERT/comments; zero SELECTs, zero routes, zero SPA mentions of dead letters.

## Where
`src/AutoNate.Web/Services/SystemIssues/Remediators/AuditOutboxDeadLetterParkRemediator.cs:15-87`

## Why it matters
Dropped audit events are "preserved" into a table an operator can only reach with psql — the self-healing story ends in a black hole, and the two indexes at DatabaseSchemaInitializer.cs:1810-1812 serve nothing.

## Evidence
`grep -rn audit_outbox_dead_letters --include=*.cs src | grep -iv 'INSERT\|CREATE\|//'` → only the index DDL and the Notes string.

## Suggested fix
Add `GET /api/system-issues/dead-letters` (+ `POST …/{id}/replay`) gated on `SystemIssue:Remediate` and a panel on the System Issues admin page.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: write-no-read|src/AutoNate.Web/Services/SystemIssues/Remediators/AuditOutboxDeadLetterParkRemediator.cs|audit_outbox_dead_letters -->

---

## archived-45 — PUT /api/records/{id}/assignees has no SPA caller, so Record:Assign is grantable but bypassed via the generic Edit path

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:spa`

## What
The dedicated assignee route (RecordEndpoints.cs:321, gated `RequirePermission(Record, Assign)` at :347) is called only from `RecordEndpointsTests.cs`; `grep -rn '/assignees' src/AutoNate.Spa/src` → 0. The SPA changes assignees through the generic `PUT /api/records/{id}`, gated on `Edit`.

## Where
`src/AutoNate.Web/Endpoints/RecordEndpoints.cs:321-347`

## Why it matters
Granting or denying `Record:Assign` has no observable effect for real users — the permission is grantable but the UI never hits the path that enforces it (a UI-side cousin of the grantable-but-inert class).

## Evidence
```
321:        group.MapPut("/{id:guid}/assignees", async (
347:          .RequirePermission(EntityKinds.Record, Actions.Assign);
```

## Suggested fix
Have the record-detail assignee control call `PUT /api/records/{id}/assignees` and strip `assigneeIds` from the generic update DTO (or require `Assign` inside the generic update when assignees change).

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: orphaned-route|src/AutoNate.Web/Endpoints/RecordEndpoints.cs|assignees -->

---

## archived-46 — GET /api/forms/{id}/versions/{versionNumber} has no caller anywhere (SPA, tests, plugins, docs)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
The single-version fetch (FormEndpoints.cs:116 → `store.GetVersionAsync`) is the only fully orphaned route in the 375-route inventory: `api/forms.ts` calls only the list (`/versions`) and `/restore/{n}`; tests cover the list only.

## Where
`src/AutoNate.Web/Endpoints/FormEndpoints.cs:116`

## Why it matters
Either the version-diff UI was dropped or never built; the route and `IFormStore.GetVersionAsync` are untested dead surface.

## Evidence
`grep -rn 'versions/\${' src/AutoNate.Spa/src/api/forms.ts` → 0; `grep -rn 'versions/[0-9]\|versions/{' tests | grep -i form` → 0.

## Suggested fix
Wire a "view this version" action in the form-version history panel, or delete the route and `GetVersionAsync`.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: orphaned-route|src/AutoNate.Web/Endpoints/FormEndpoints.cs|get-version -->

---

## archived-47 — POST /api/admin/projections/feeds/{feed}/reset-watermark is documented as the recovery step but has no SPA button and no test

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:spa`

## What
Registered at AdminProjectionsEndpoints.cs:96; `grep -rn 'reset-watermark\|resetWatermark' src/AutoNate.Spa/src tests` → 0. Siblings `/pause`, `/resume`, `/rebuild` all resolve to `src/AutoNate.Spa/src/api/projections.ts`. `docs/projection-framework/operations.md:81,279` documents it as a curl.

## Where
`src/AutoNate.Web/Endpoints/AdminProjectionsEndpoints.cs:96`

## Why it matters
The projections admin page exposes pause/resume/rebuild but not watermark reset, so the documented recovery step requires curl against an admin endpoint.

## Evidence
```
96:        group.MapPost("/feeds/{feedName}/reset-watermark", async (
```

## Suggested fix
Add `resetFeedWatermark()` to `api/projections.ts` and a confirm-guarded button on the Projections admin page; add an endpoint test.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: orphaned-route|src/AutoNate.Web/Endpoints/AdminProjectionsEndpoints.cs|reset-watermark -->

---

## archived-48 — /admin/config/features is seeded in the menu but SettingGroup.Features has zero definitions

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:low`, `area:api`

## What
`SettingGroup.Features` exists only as the enum member (1 hit in src); the registry defines settings for `General` and `Chatbot` only. `DatabaseSchemaInitializer.cs:1024` seeds a 'Features' menu item with `configFeatures`; `SiteSettingsForm.tsx:130` renders "No settings in this group yet."

## Where
`src/AutoNate.Web/Services/SiteSettings/SiteSettingsRegistry.cs:26, :60-78; src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs:1024`

## Why it matters
A seeded nav item leads to an empty form, and the registry's own "Adding a new feature flag" instructions (:11-15) point at a group with no entries.

## Evidence
`grep -rn 'SettingGroup.Features' src/AutoNate.Web` → 1 (the enum).

## Suggested fix
Move at least one flag into the Features group or drop the `configFeatures` seed row.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unwired-feature-flag|src/AutoNate.Web/Services/SiteSettings/SiteSettingsRegistry.cs|SettingGroup.Features -->

---

## archived-49 — JetStreamCodeNodeRunner comments claim a durable consumer and CodeNode:* config keys that don't exist

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `documentation`, `sev:low`, `area:api`

## What
JetStreamCodeNodeRunner.cs:13 (and NatsStreamProvisioner.cs:97, executor index.ts:7) say the executor "subscribes via a durable consumer named `executor`"; the executor actually uses core-NATS `nc.subscribe(SUBJECT, { queue: "executor" })` (index.ts:22). Line 19 says timeout/memory are "override-able via CodeNode:TimeoutMs / CodeNode:MemoryMb" — those keys appear in no appsettings file (0 hits) and have no `IOptions` binding; 30 s / 128 MB are hard-coded.

## Where
`src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs:13, :19; src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs:97; services/executor/src/index.ts:7`

## Why it matters
Operators tuning the executor will set config keys that do nothing, and the provisioned `pipeline-code-runs` stream buys nothing under a core subscription — the comments describe a design that was not built.

## Evidence
`grep -rn CodeNode src/AutoNate.Web/appsettings*.json` → 0; `index.ts:22 nc.subscribe(SUBJECT, { queue: "executor" })`.

## Suggested fix
Either bind `CodeNode:*` via `IOptions<CodeNodeOptions>` and switch the executor to a JetStream durable pull consumer, or correct the three comments to describe core request/reply and delete the unused stream.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: stale-comment|src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs|header-comment -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-146: all three misleading comments are now accurate — `JetStreamCodeNodeRunner.cs` header (core queue subscriber; timeouts hard-coded, no `CodeNode:*` keys claimed), the `NatsStreamProvisioner` block (stream removed entirely), and `services/executor/src/index.ts` (fixed in archived-145). The unused `pipeline-code-runs` stream is gone rather than documented.

</details>

---

## archived-50 — SitewideAppearance stub export in sections.tsx is unused (configAppearance maps to the real page)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
`export function SitewideAppearance()` renders a `<Stub>`; `grep -rn SitewideAppearance src/AutoNate.Spa/src` → 1 (the definition). `configAppearance` maps to `<SiteAppearancePage />`.

## Where
`src/AutoNate.Spa/src/pages/admin/config/sections.tsx:81-83`

## Why it matters
Leftover that makes the stub set look larger than it is; delete alongside the security stubs.

## Evidence
1 reference repo-wide.

## Suggested fix
Remove `SitewideAppearance` from `sections.tsx`.

_Found by `/n8-audit integration` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: orphaned-export|src/AutoNate.Spa/src/pages/admin/config/sections.tsx|SitewideAppearance -->

---

## archived-51 — HOT POST /api/auth/check: one DbContext + one SQL round-trip per check item (2 per row on list pages)

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:high`, `area:api`

## What
The batched permission endpoint loops `checks` and calls `IAuthorizer.AuthorizeAsync` per item (AuthEndpoints.cs:164-169); each instance-level check opens its own DbContext and runs its own `AnyAsync` (InstanceAuthorizers.cs:27-30).

## Where
`src/AutoNate.Web/Endpoints/AuthEndpoints.cs:164-169; src/AutoNate.Web/Authorization/Evaluator/InstanceAuthorizers.cs:27-30`

## Why it matters
Cost shape: O(N) DbContext creations + O(N) sequential SQL round-trips per request, N = rows × gated actions per row. `WorkflowExecutions.tsx:97-103` emits 2 checks/row → a 25-row page is 50 queries behind one HTTP call, on an endpoint fired by every gated list view.

## Evidence
```
164:            foreach (var c in checks)
166:                var decision = await authorizer.AuthorizeAsync(
```
```
27:        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
29:        var visible = await authorizer.FilterQueryAsync(db, actor, Kind, action, query, cancellationToken);
30:        return await visible.AnyAsync(cancellationToken);
```

## Suggested fix
Group `checks` by (kind, action) and add `IInstanceAuthorizer.FilterExistingAsync(ids)` running `FilterQueryAsync(...).Where(r => ids.Contains(r.Id)).Select(r => r.Id)` once per group — the same push-into-SQL move `BuildRecordSqlFilter`/`FilterQueryAsync` already enable. Collapses N round-trips to ≤ distinct (kind, action) pairs.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: n-plus-one|src/AutoNate.Web/Endpoints/AuthEndpoints.cs|check -->

---

## archived-52 — HOT GET /api/executions[/page]: every execution is fetched from Flowable, authorised in memory, then paged client-side

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:high`, `area:api`

## What
`/page` (ExecutionEndpoints.cs:99-101) and the unpaged `/` (:36-38) both call `flowable.GetWorkflowExecutionsAsync()` (no filter/limit args — IFlowableClient.cs:31), run `FilterVisibleExecutionsAsync` over every row, then apply search/status/sort/`Skip/Take` in LINQ-to-objects (:149-167). `workflow_execution_cache` exists (11 references in DatabaseSchemaInitializer) with `def_status`, `auth_tags` and `start_time_brin` indexes precisely to avoid this.

## Where
`src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs:36-38, :99-101, :149-167`

## Why it matters
Cost shape: O(E) Flowable payload + O(E) selector evaluations per request, E = total executions, regardless of `pageSize`. Page 3 of 25 costs the same as everything. The DataTable auto-mode count probe (`pageSize=0`) pays the full cost a second time per mount.

## Evidence
```
99:            var unfiltered = await flowable.GetWorkflowExecutionsAsync(cancellationToken);
100:            var rawList = await FilterVisibleExecutionsAsync(
149:            var materialized = filtered.ToList();
167:                sliced = ordered.Skip(pageIndex * size).Take(size);
```

## Suggested fix
Serve `/page` from `workflow_execution_cache` via `IAuthorizer.FilterQueryAsync<WorkflowExecutionCache>` + EF `Skip/Take` (uses `WorkflowExecutionCacheSelectorCompiler` and the existing indexes); clamp the unpaged route with `Math.Clamp(take ?? 100, 1, 500)` as in SystemIssueEndpoints.cs:63 or retire it.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: load-all-then-filter|src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs|page -->

---

## archived-53 — HOT GET /api/agent/conversations/{id}: full message + tool-call history re-read after every agent turn

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:high`, `area:api`

## What
`GetForUserAsync` selects all `agent_message` rows for the conversation (:152-156) plus all `agent_tool_call` rows via an IN-list of every message id (:159-163). `AgentSidebar.tsx:339` invalidates this query at the end of every turn.

## Where
`src/AutoNate.Web/Services/Agent/Conversations/EfCoreAgentConversationStore.cs:152-163; src/AutoNate.Spa/src/agent/AgentSidebar.tsx:339`

## Why it matters
Cost shape: O(M+T) rows (with JSONB `content_json` blobs) shipped per turn; over a K-turn conversation the sidebar transfers O(K²) rows. `agent_messages` is on the unbounded-table list.

## Evidence
```
152:        var messages = await dbContext.AgentMessages
154:            .Where(m => m.ConversationId == id)
156:            .ToListAsync(cancellationToken);
159:        var toolCalls = await dbContext.AgentToolCalls
161:            .Where(tc => messageIds.Contains(tc.MessageId))
```
```
339:      await queryClient.invalidateQueries({ queryKey: ["agent", "conversation", id] });
```

## Suggested fix
Add a `?since=<messageId|timestamp>` tail parameter anchored on `ix_agent_message_conversation (conversation_id, created_at_utc)` — the summary-anchored tail read `LoadMessagesWithIdsAsync` (:388-397) already uses — and have the sidebar append the delta instead of invalidating the key.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: unbounded-list|src/AutoNate.Web/Services/Agent/Conversations/EfCoreAgentConversationStore.cs|GetForUserAsync -->

---

## archived-54 — HOT GET /api/users/directory returns the whole local_users table on every editor mount, uncached

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:medium`, `area:api`

## What
`/directory` calls `ILocalUserStore.ListAsync` (UserEndpoints.cs:47) → `LocalUsers.AsNoTracking().OrderBy(...).ToListAsync()` with no `Take` (EfCoreLocalUserStore.cs:19). Its own comment says it is called on every editor mount; `useUsers()` has 16 SPA call sites and no `staleTime` override.

## Where
`src/AutoNate.Web/Endpoints/UserEndpoints.cs:43-48; src/AutoNate.Web/Services/Auth/EfCoreLocalUserStore.cs:16-19`

## Why it matters
Cost shape: O(U) rows per call, U = total users, × (editor mounts + assignee pickers + comment renders), re-fetched every 30 s stale window.

## Evidence
```
43:        group.MapGet("/directory", async (
47:            var users = await store.ListAsync(cancellationToken);
```

## Suggested fix
Back it with a singleton sliding-TTL snapshot invalidated on user create/update/delete, modelled on `PageRegistrySnapshotCache` (the projected row set is actor-invariant), and project only `(id, username, displayName)`.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: missing-cache|src/AutoNate.Web/Endpoints/UserEndpoints.cs|directory -->

---

## archived-55 — DataTable auto-mode count probe doubles server cost of every auto-mode table

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:medium`, `area:spa`

## What
Auto-mode tables issue a `pageSize: 0` probe (DataTable.tsx:236-241) in addition to the real page/all request. 6 tables use `mode="auto"` (ManageUsers, WatchedRecordsPanel on the home page, WorkflowExecutions, Grants, Hierarchy, AllProjects). On endpoints that do their work before slicing — notably `/api/executions/page` — the probe pays the full cost and returns zero rows.

## Where
`src/AutoNate.Spa/src/components/data-table/DataTable.tsx:236-241`

## Why it matters
Cost shape: 2× server work per table mount; on the executions endpoint that is 2× O(E).

## Evidence
```
236:  const probe = useQuery({
238:    queryFn: () => loadPage!({ page: 0, pageSize: 0, search: "", sort: null, filter: null }),
239:    enabled: mode === "auto" && !!loadPage,
```

## Suggested fix
Add a `?countOnly=true` branch to the paged endpoints that short-circuits after `totalCount` (the notification/user paged stores already `CountAsync` on an IQueryable before materialising) and have the probe call it.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: refetch-amplifier|src/AutoNate.Spa/src/components/data-table/DataTable.tsx|count-probe -->

---

## archived-56 — ix_menu_items_template_key has no request-path reader — maintenance cost on every menu_items write

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:low`, `area:api`

## What
A partial expression index on `(config->>'templateKey')` (DatabaseSchemaInitializer.cs:682-684); every `templateKey'` predicate in `src/AutoNate.Web` outside this file is C# on already-materialised rows (`LoadTemplateInfoAsync` reads it from the row then queries `page_templates` by key).

## Where
`src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs:682-684`

## Why it matters
Cost shape: one extra expression-index maintenance per `menu_items` INSERT/UPDATE forever, zero read benefit — same failure mode as the already-dropped `ix_menu_items_page_path` (:764).

## Evidence
```
682:        CREATE INDEX IF NOT EXISTS ix_menu_items_template_key
683:            ON menu_items ((config->>'templateKey'))
684:            WHERE item_type = 'template';
```

## Suggested fix
`DROP INDEX IF EXISTS ix_menu_items_template_key;` in a new schema step, mirroring the `ix_menu_items_page_path` drop.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unused-index|src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs|ix_menu_items_template_key -->

---

## archived-57 — AgentSession persists tool calls one INSERT at a time per turn

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:low`, `area:api`

## What
`foreach (var (toolUseId, info) in toolStarts) await _conversationStore.AppendToolCallAsync(...)` (AgentSession.cs:416-419) — one round-trip per tool call, followed by an audit publish each.

## Where
`src/AutoNate.Web/Services/Agent/Loop/AgentSession.cs:416-419`

## Why it matters
Cost shape: O(C) INSERT round-trips per agent turn, C = tool calls requested that turn (typically 1-5, occasionally 10+), on the per-turn hot path.

## Evidence
```
416:                foreach (var (toolUseId, info) in toolStarts)
418:                    var toolCallId = await _conversationStore.AppendToolCallAsync(
```

## Suggested fix
Add `AppendToolCallsAsync(IReadOnlyList<…>)` doing one `AddRange` + one `SaveChangesAsync`, returning the ids in order.

_Found by `/n8-audit performance` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: n-plus-one|src/AutoNate.Web/Services/Agent/Loop/AgentSession.cs|AppendToolCallAsync -->

---

## archived-58 — Executor Python runner: shared Pyodide interpreter, timeout that cannot fire, memoryMb ignored

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:critical`, `area:services`

## What
One module-level `pyodide` instance serves every request (pythonRunner.ts:13-20). The wall-clock guard is `Promise.race([exec, timer])` (:32-37, :83) — but `runPythonAsync` runs WASM on the same JS thread, so a non-yielding script never lets the `setTimeout` callback run, and nothing could cancel the script anyway. `request.memoryMb` is never read (the JS runner honours it via `new ivm.Isolate({ memoryLimit })`, jsRunner.ts:11). Globals persist across invocations.

## Where
`services/executor/src/pythonRunner.ts:13-20, :28-37, :71-83`

## Why it matters
(1) Any user who can author a Python transformer submits `while True: pass` → the executor event loop is pinned forever, NATS stops being serviced, every code-node pipeline for every tenant fails with the generic 30 s timeout until someone restarts the container — and the executor is not in `infra/docker-compose.yml`, so it has no restart supervisor. (2) `[dict(x=1)] * 10**9` grows the shared WASM heap until the process is OOM-killed. (3) Author A's `def transform` stays defined; author B's script that forgets to define it silently runs A's; A can stash data in a module global for B to read.

## Evidence
```
13: let pyodide: PyodideInterface | null = null;
32:   const timer = new Promise<never>((_, reject) => {
33:     const handle = setTimeout(() => {
34:       reject(new Error(`Python execution timed out after ${timeoutMs}ms.`));
73:       const rawJson = await py.runPythonAsync(wrapper);
83:   return Promise.race([exec, timer]);
```
Path: `POST /api/code-transformers` (language=python) → `POST /api/pipelines/{id}/run` → `JetStreamCodeNodeRunner` → NATS `pipeline-code-run.>` → `index.ts:44 runPython`.

## Suggested fix
Run each Python request in a fresh `loadPyodide` inside a dedicated `worker_threads` Worker with `resourceLimits: { maxOldGenerationSizeMb: request.memoryMb }`; the parent `worker.terminate()`s on timeout (the only way to stop blocking WASM). This matches the per-call `new ivm.Isolate()`/`isolate.dispose()` lifecycle in jsRunner.ts and fixes all three failure modes at once. Add the executor to docker-compose with `restart: unless-stopped`.

_Found by `/n8-audit security+stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:critical`._

<!-- fingerprint: sandbox-shared-interpreter-no-timeout|services/executor/src/pythonRunner.ts|runPython -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan

While reproducing this I found the Python "sandbox" also exposes the Node host to author code (`import js; js.process…`) — filed as archived-161 (`sev:critical`). Same file, same boundary, so both land in one PR. archived-64 (JSON spliced into a triple-quoted literal) is the wrapper in the same function and gets fixed on the way.

**Design** — `services/executor/src/pythonRunner.ts` + new `pythonWorker.ts`:
1. **One Pyodide interpreter per request**, in a dedicated `worker_threads` Worker. A small warm pool (`EXECUTOR_PY_WARM_WORKERS`, default 1) hides the ~0.8 s load; every worker is single-use and terminated after its job, so globals, monkey-patches and leftover state cannot cross authors. Concurrency is capped (`EXECUTOR_PY_MAX_CONCURRENCY`, default 2) with a FIFO wait so a burst cannot fork unbounded interpreters.
2. **Timeout that fires**: the parent owns the deadline. At `timeoutMs` it rejects the request, writes SIGINT into Pyodide's interrupt `SharedArrayBuffer` (raises `KeyboardInterrupt` in the script — measured: stops `while True: pass` at the deadline, even inside `except BaseException: pass`), and `worker.terminate()`s after a short grace for C-level loops the interrupt cannot reach. The NATS loop is never blocked because the WASM runs on the worker thread.
3. **`memoryMb` enforced**: a `WebAssembly.Memory.prototype.grow` hook in the worker caps the interpreter's linear memory at *post-load baseline + memoryMb*; Emscripten turns a refused grow into a plain Python `MemoryError` (measured: interpreter stays usable). `resourceLimits.maxOldGenerationSizeMb` (`EXECUTOR_PY_JS_HEAP_MB`, default 256) guards the worker's JS heap as an operator-level backstop.
4. **Host escape closed (archived-161)**: `loadPyodide({ jsglobals: Object.create(null), env: {…fixed…} })`, `unregisterJsModule("pyodide_js")` + purge `pyodide_js*` from `sys.modules`, and `fetch`/`WebSocket` disabled in the worker after load. Verified by probe: `js.process`, `js.eval`, `pyodide_js`, `run_js`, `open_url`, host FS all fail; virtual MEMFS remains.
5. **archived-64**: inputs/config reach Python via `pyodide.globals.set(...)` as JSON strings — no source splicing.
6. Tests: `node --test` suite for the executor (`npm test`): happy path (transformer + analyzer, columns), timeout on a busy loop with the runner still serving afterwards, `MemoryError` at the cap, no global leakage between requests, every escape vector above rejected, and archived-64's quote/backslash round-trip. Smoke against the compose executor over NATS after rebuild.

Branch `fix/58-python-sandbox`; PR with `Closes archived-58`, `Closes archived-161`, `Closes archived-64`.

</details>

---

## archived-59 — Authorization fails open by default: Enabled=false / Enforcement=off / AssignSuperAdminToAll=true with no startup validation

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:api`

## What
The code defaults are `Enabled=false`, `Enforcement="off"`, `AssignSuperAdminToAllExistingUsers=true`. Unlike `WorkflowBehaviors:CallbackSharedSecret` and `AllowedHosts` (Program.cs:781/795 `.ValidateOnStart()`), there is no `IValidateOptions<AuthorizationOptions>` (`grep -rn 'IValidateOptions<AuthorizationOptions>' src` → 0) forcing an explicit production value. Only `appsettings.Development.json` sets `full`; there is no `appsettings.Production.json`.

## Where
`src/AutoNate.Web/Authorization/AuthorizationOptions.cs:7-11; src/AutoNate.Web/Authorization/Authorizer.cs:107-113, :242, :330, :379`

## Why it matters
A production deploy that omits `Authorization__Enabled` runs with every `RequirePermission` / `RequireKindPermission` / `FilterQueryAsync` returning Allow — any authenticated user reads and mutates every record, grant, plugin and external connection — and every newly created user is silently made SuperAdmin. README lists this as a required override, but the app does not enforce it.

## Evidence
```
 7:     public bool Enabled { get; set; } = false;
 9:     public string Enforcement { get; set; } = AuthorizationEnforcement.Off;
11:     public bool AssignSuperAdminToAllExistingUsers { get; set; } = true;
```
`Authorizer.cs:113` returns `AuthDecision.Allow("write enforcement disabled")` for every instance write when `Enforcement != Full`.

## Suggested fix
Add `IValidateOptions<AuthorizationOptions>` that refuses to start outside Development unless `Enabled && Enforcement == "full"` (and warns loudly when `AssignSuperAdminToAllExistingUsers` is still true), registered with `.ValidateOnStart()` exactly like the `CallbackSharedSecret` validator. Regression test: host fails to start in `Production` with defaults.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: insecure-default-authz-fail-open|src/AutoNate.Web/Authorization/AuthorizationOptions.cs|AuthorizationOptions -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan

Two corrections to the issue's premises, found while reading the code:

- **"every newly created user is silently made SuperAdmin" is not accurate.** `SuperAdminBackfillSql` (`DatabaseSchemaInitializer.cs:537-567`) is gated by *both* `AssignSuperAdminToAllExistingUsers` **and** a one-shot `auth_seed_state` key (`superadmin_backfill_v1`), so it grants the role once, to the users that exist at that moment — users created later get nothing. It is also the **only** startup path that grants SuperAdmin to anyone, so a greenfield install with the flag `false` boots with *no* SuperAdmin at all and, under `Enforcement=full`, is unadministrable. Hard-failing on that flag (as the issue suggests) would therefore break fresh installs — it gets a loud startup **warning**, not a refusal, and the README claim gets corrected.
- **A related fail-open I'll fix here:** `Authorizer.cs:113` compares `Enforcement != AuthorizationEnforcement.Full` with ordinal string equality, so `"Full"` or `"ful"` in config silently means *not full* → every instance write allowed, with no error anywhere. The validator rejects unrecognised values in **every** environment.

**Fix**
1. `AuthorizationOptions.cs` — fail-closed code defaults: `Enabled = true`, `Enforcement = full`. `AssignSuperAdminToAllExistingUsers` stays `true` (greenfield bootstrap; documented). All 3 direct constructions in tests either set the fields explicitly or use `Enabled = false`, which short-circuits before `Enforcement` is read, so the flip is inert for the suite.
2. New `AuthorizationOptionsValidator : IValidateOptions<AuthorizationOptions>` — always rejects an unknown `Enforcement`; outside Development additionally requires `Enabled == true` and `Enforcement == "full"`. Registered with `.ValidateOnStart()` exactly like `WorkflowBehaviors:CallbackSharedSecret` and `Yjs:InternalSharedSecret`.
3. `appsettings.json` — ship an explicit `Authorization` section with the secure values (base config currently has none at all), so the defaults are visible where operators look.
4. Startup warnings outside Development when `AssignSuperAdminToAllExistingUsers=true` or `DryRun=true` (both are fail-open-ish but legitimate rollout/bootstrap tools).
5. README: correct the SuperAdmin claim, document the greenfield trap, note the new refuse-to-start behaviour.
6. Tests: `AuthorizationOptionsValidatorTests` covering every branch (defaults in Production/Staging refused; `Enabled=false` refused; `Enforcement=off`/`read-only` refused; `"Full"` typo refused in *all* environments; valid config accepted; Development permissive), plus an assertion that the validator is actually registered on the host.

Branch `fix/59-authz-fail-closed`; PR with `Closes archived-59`.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Fixed on `master` via archived-164.

**What landed**
- Fail-closed defaults: `Enabled = true`, `Enforcement = "full"`.
- `AuthorizationOptionsValidator` (`IValidateOptions<AuthorizationOptions>`, `ValidateOnStart()` + an eager `.Value` read straight after `builder.Build()` so it fires **before any database or hosted-service work** — with `ValidateOnStart` alone the DB initializer ran first and a bad posture only surfaced after a DB connect).
- Unrecognised `Enforcement` refused in **every** environment — a second fail-open this issue didn't cover: `Authorizer.cs:113` compares ordinally, so `"Full"`/`"FULL"`/a typo read as *not full* and silently allowed every instance write.
- Base `appsettings.json` now ships an explicit, commented `Authorization` section (it had none at all).
- Startup warnings outside Development for `DryRun` and `AssignSuperAdminToAllExistingUsers`.

**Evidence** — real host via `dotnet run`, bogus DB so nothing is touched:

| environment + config | result |
|---|---|
| Production, `Authorization__Enabled=false` (this issue's scenario) | **refused** — "Authorization:Enabled must be true outside Development…" |
| Production, `Enforcement=read-only` | **refused** — "…must be \"full\" outside Development…" |
| Production, **nothing configured** | passes authorization, proceeds — the new defaults are safe |
| Production, `Enabled=true` + `Enforcement=full` | passes authorization, proceeds |
| **Development**, `Enforcement=Full` (mis-cased) | **refused** — "…must be one of \"off\", \"read-only\", \"full\" (lower-case, exactly)…" |

Tests: `AuthorizationOptionsValidatorTests` 16/16; Authorization folder 295/295; full `AutoNate.Web.Tests` 1368/1 and full E2E 141 passed / 0 failed / 2 skipped. The single Web.Tests failure is pre-existing and unrelated — a `master` @ `f28a1c85` baseline in the same checkout fails the same test with the same signature (1352/1), it passes 3/3 in isolation, and it dies on a fixed 5 s WebSocket budget. Filed as archived-163.

**Correction to the issue's premise:** "every newly created user is silently made SuperAdmin" is not accurate. `SuperAdminBackfillSql` is gated by both the flag **and** a one-shot `auth_seed_state` key (`superadmin_backfill_v1`) — it grants the role once, to users existing at that moment; users created later get nothing. It is also the only startup path that grants SuperAdmin, so hard-failing on that flag would leave a greenfield install with no admin at all and, under `Enforcement=full`, unadministerable. It therefore warns rather than refusing, and the README claim is corrected with both real traps documented (pointing a deployment at a database that already holds other users; turning it off on a greenfield install).

</details>

---

## archived-60 — REST data connector fetches admin-supplied URLs with no SSRF guard and echoes the body to the caller

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:high`, `area:api`

## What
`config.Url` comes straight from user-supplied `ConfigJson` and is handed to `HttpClient` (RestDataConnectorHandler.cs:110-112) with no scheme/host allowlist, no private-IP rejection and no DNS pin (`grep -c 'IsBlockedAddress\|IDnsResolver\|Loopback'` → 0). `WebFetchSkill.IsBlockedAddress` (WebFetchSkill.cs:231) already implements the guard for the agent's fetch tool.

## Where
`src/AutoNate.Web/Services/DataConnectors/Builtin/RestDataConnectorHandler.cs:110-113`

## Why it matters
A user with `DataConnector:Create` + `Connect` (not SuperAdmin) creates a connector with `Url = http://169.254.169.254/computeMetadata/v1/instance/service-accounts/default/token` (or Flowable REST :8080, the Dapr sidecar :3500, NATS monitoring :8222) and calls preview — the response body is parsed into rows and returned verbatim. Cloud instance credentials and internal-only APIs are read out through the app.

## Evidence
```
110:        var resolvedUrl = config.Url.Replace("{lastFetchDate}", token, StringComparison.Ordinal);
112:        var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
```
Path: `POST /api/dataconnectors` → `POST /api/dataconnectors/{id}/preview` (`RequirePermission(DataConnector, Connect)`) → `handler.FetchAsync` → `client.SendAsync` → rows in `DataConnectorPreviewResult`.

## Suggested fix
Route `resolvedUrl` through the same scheme + resolved-IP check `WebFetchSkill` uses (`IDnsResolver` + `IsBlockedAddress`) before building the request; require https outside Development. Regression test: preview against `http://127.0.0.1` and a link-local address → 400.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: ssrf-no-allowlist|src/AutoNate.Web/Services/DataConnectors/Builtin/RestDataConnectorHandler.cs|BuildRequest -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan (archived-60 + archived-61 together)

Both are "an outbound URL is caller-controlled and the request carries something valuable", so they land in one PR — but archived-61's surface is **wider than the issue states**.

**archived-61 scope correction.** `IConnectionModelLister` is one of *five* consumers of the same connection-metadata `baseUrl`. The others are worse, because they run on every call rather than on an admin button press:

| site | what it sends |
|---|---|
| `Agent/Providers/AnthropicChatProvider.cs:38,46` | `x-api-key` on **every chat turn** |
| `Agent/Providers/OpenAIChatProvider.cs:38,46` | `Authorization: Bearer` on **every chat turn** |
| `Agent/Search/TavilyWebSearchProvider.cs:37` | Tavily key on every search |
| `ExternalConnections/IConnectionModelLister.cs:57,106` | the key (issue's named site) |
| `Agent/Catalog/IAgentModelCatalogRefresher.cs:89` | via the lister, on a background timer |

Fixing only the lister would leave the key flowing to an attacker-named host on every message, so all five get the guard. (A hostile base URL also feeds attacker-controlled text straight into the agent's context — prompt injection on top of exfiltration.)

**Two guards, because the two problems have different shapes**

1. `IOutboundUrlGuard` (async: scheme + DNS-resolve + `IsBlockedAddress`) for **archived-60**, where arbitrary REST endpoints are the point of the feature so no allowlist is possible. `IsBlockedAddress` moves out of `WebFetchSkill` into the shared guard and `WebFetchSkill` delegates to it, so the two copies cannot drift; its existing behaviour (http allowed) is preserved via a `RequireHttps` policy flag that the REST connector sets outside Development.
2. `IProviderBaseUrlPolicy` (sync: https + per-kind host allowlist) for **archived-61**. An allowlist is strictly stronger than an IP check here because the set of legitimate hosts is known, and it is enforced at the trust boundary — the three places untrusted metadata becomes a `Uri` (`ChatProviderResolver`, `WebSearchProviderResolver`, `ConnectionModelLister`). Defaults `api.anthropic.com` / `api.openai.com` / `api.tavily.com`, extendable per kind through an `ExternalConnections:AllowedProviderHosts` config section for Azure OpenAI, gateways or a local model.

**Behaviour change to call out:** an existing connection pointing at a custom base URL (self-hosted OpenAI-compatible endpoint, Ollama) stops working until its host is added to the allowlist. That is the intended trade — it is exactly the capability being abused — and the failure is a clear error naming the key to set.

**Tests**: guard unit tests (literal IPs, DNS-to-private, mixed public/private answers, scheme rules, https-outside-Development); REST-connector preview/test refused against `127.0.0.1` and `169.254.169.254`; policy tests per kind (default allowed, attacker host refused before the credential is attached, http refused, operator-configured host allowed); plus the existing `WebFetchSkillTests` must stay green through the refactor.

Branch `fix/60-61-outbound-url-guards`; PR with `Closes archived-60`, `Closes archived-61`.

</details>

---

## archived-61 — External-connection BaseUrl is caller-controlled: stored provider API key is sent to any host the admin names

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:api`

## What
`input.BaseUrl` flows unvalidated into `new Uri(...)` and the request carries the decrypted secret as `Authorization: Bearer` (IConnectionModelLister.cs:106-109). No scheme check, host allowlist or private-IP guard (`grep -c 'IsBlockedAddress\|allowlist'` → 0).

## Where
`src/AutoNate.Web/Services/ExternalConnections/IConnectionModelLister.cs:105-111`

## Why it matters
Anyone with `ExternalConnection:Manage` points a connection at `http://attacker.example/` and clicks "list models" — the plaintext Anthropic/OpenAI key is transmitted to the attacker, and the upstream body is echoed (256 chars) in the error path, making it an internal-network probe too. `IConnectionSecretProtector` is defeated by one field.

## Evidence
```
106:        var baseUrl = new Uri(string.IsNullOrWhiteSpace(input.BaseUrl) ? "https://api.openai.com" : input.BaseUrl);
108:        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "/v1/models"));
109:        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.Secret);
```

## Suggested fix
Require `https` and validate the host against a per-kind allowlist (`api.openai.com`, `api.anthropic.com`, plus operator-configured entries in site settings) before attaching the credential; reuse the `IsBlockedAddress` guard for the IP check.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: credential-exfil-via-base-url|src/AutoNate.Web/Services/ExternalConnections/IConnectionModelLister.cs|ListOpenAIAsync -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan (archived-60 + archived-61 together)

Both are "an outbound URL is caller-controlled and the request carries something valuable", so they land in one PR — but archived-61's surface is **wider than the issue states**.

**archived-61 scope correction.** `IConnectionModelLister` is one of *five* consumers of the same connection-metadata `baseUrl`. The others are worse, because they run on every call rather than on an admin button press:

| site | what it sends |
|---|---|
| `Agent/Providers/AnthropicChatProvider.cs:38,46` | `x-api-key` on **every chat turn** |
| `Agent/Providers/OpenAIChatProvider.cs:38,46` | `Authorization: Bearer` on **every chat turn** |
| `Agent/Search/TavilyWebSearchProvider.cs:37` | Tavily key on every search |
| `ExternalConnections/IConnectionModelLister.cs:57,106` | the key (issue's named site) |
| `Agent/Catalog/IAgentModelCatalogRefresher.cs:89` | via the lister, on a background timer |

Fixing only the lister would leave the key flowing to an attacker-named host on every message, so all five get the guard. (A hostile base URL also feeds attacker-controlled text straight into the agent's context — prompt injection on top of exfiltration.)

**Two guards, because the two problems have different shapes**

1. `IOutboundUrlGuard` (async: scheme + DNS-resolve + `IsBlockedAddress`) for **archived-60**, where arbitrary REST endpoints are the point of the feature so no allowlist is possible. `IsBlockedAddress` moves out of `WebFetchSkill` into the shared guard and `WebFetchSkill` delegates to it, so the two copies cannot drift; its existing behaviour (http allowed) is preserved via a `RequireHttps` policy flag that the REST connector sets outside Development.
2. `IProviderBaseUrlPolicy` (sync: https + per-kind host allowlist) for **archived-61**. An allowlist is strictly stronger than an IP check here because the set of legitimate hosts is known, and it is enforced at the trust boundary — the three places untrusted metadata becomes a `Uri` (`ChatProviderResolver`, `WebSearchProviderResolver`, `ConnectionModelLister`). Defaults `api.anthropic.com` / `api.openai.com` / `api.tavily.com`, extendable per kind through an `ExternalConnections:AllowedProviderHosts` config section for Azure OpenAI, gateways or a local model.

**Behaviour change to call out:** an existing connection pointing at a custom base URL (self-hosted OpenAI-compatible endpoint, Ollama) stops working until its host is added to the allowlist. That is the intended trade — it is exactly the capability being abused — and the failure is a clear error naming the key to set.

**Tests**: guard unit tests (literal IPs, DNS-to-private, mixed public/private answers, scheme rules, https-outside-Development); REST-connector preview/test refused against `127.0.0.1` and `169.254.169.254`; policy tests per kind (default allowed, attacker host refused before the credential is attached, http refused, operator-configured host allowed); plus the existing `WebFetchSkillTests` must stay green through the refactor.

Branch `fix/60-61-outbound-url-guards`; PR with `Closes archived-60`, `Closes archived-61`.

</details>

---

## archived-62 — Every plugin DB role inherits SELECT on the entire public schema via plg_readers

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:plugins`

## What
Each per-plugin LOGIN role is granted `plg_readers` (PluginSchemaProvisioner.cs:102), and `DatabaseSchemaInitializer.cs:1557-1558` grants that group `USAGE ON SCHEMA public` + `SELECT ON ALL TABLES IN SCHEMA public` (plus default privileges for future tables).

## Where
`src/AutoNate.Web/Plugins/PluginSchemaProvisioner.cs:99-106; src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs:1552-1565`

## Why it matters
The documented posture is "per-plugin role restricted to plg_<code>". In reality any uploaded plugin, using the connection the host builds from its own role password, can `SELECT * FROM local_users` (password hashes, lockout state), `external_connections` (protected provider secrets), `plugins.role_password_encrypted` (every other plugin's role password) and `saved_query_share_tokens`. The intended reads (menus, pages) don't need blanket access.

## Evidence
```
1557:        GRANT USAGE ON SCHEMA public TO plg_readers;
1558:        GRANT SELECT ON ALL TABLES IN SCHEMA public TO plg_readers;
```
Path: `POST /api/admin/plugins` → `PluginManagementService.UploadAsync` → `ProvisionAsync` → role joins `plg_readers`; plugin code reads via `IPluginContext.Data`.

## Suggested fix
Replace the blanket grant with an explicit table allowlist (menus, menu_items, page_templates, pages) and drop the default-privileges clause; add a schema test asserting `plg_readers` cannot select from `local_users`.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: overbroad-db-grant|src/AutoNate.Web/Plugins/PluginSchemaProvisioner.cs|plg_readers -->

---

## archived-63 — Plugin zip size cap trusts the attacker-supplied header, then ExtractToDirectory writes the real bytes uncapped

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:medium`, `area:plugins`

## What
The uncompressed-size gate sums `entry.Length` (PluginUploadValidator.cs:36) — the central-directory size field, which the uploader controls independently of the deflate stream. `ZipFile.ExtractToDirectory` (PluginManagementService.cs:86 and :212) then writes the actual decompressed bytes with no cap.

## Where
`src/AutoNate.Web/Plugins/PluginUploadValidator.cs:26-39; src/AutoNate.Web/Plugins/PluginManagementService.cs:86, :212`

## Why it matters
A crafted 1 MB zip declaring `Length = 100` per entry passes validation and expands to tens of GB into `/data/plugins/<guid>/`, filling the runtime data volume that also holds uploads, `/files` and per-plugin state — taking the host down. `Plugins:MaxUploadBytes` (50 MB) only bounds the compressed upload. Requires `Plugin:Manage`, hence medium.

## Evidence
```
36:                  total += entry.Length;
37:                  if (total > maxUncompressedBytes)
```
```
86:                ZipFile.ExtractToDirectory(tempZip, folder, overwriteFiles: true);
```

## Suggested fix
Extract entry-by-entry through a byte-counting stream that aborts past the cap and rejects entries whose declared `Length` disagrees with bytes read. Regression test: zip with forged central-directory sizes → rejected before any file is written.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: zip-bomb-declared-size|src/AutoNate.Web/Plugins/PluginUploadValidator.cs|entry.Length -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Correcting this issue's exploit claim before closing it.** The described attack — declare `Length = 100` per entry, pass validation, then have `ExtractToDirectory` write tens of GB — **does not work on .NET**. `ZipArchive` truncates each entry stream at the declared uncompressed size, so understating that field yields *fewer* bytes, not more.

Measured on this runtime, forging every central-directory record of a real 8 MiB entry down to a declared 10 bytes:

```
zip size on disk = 8269
before forge: big.bin declared Length=8388608 Compressed=8157
patched central headers = 1
after forge:  big.bin declared Length=10      Compressed=8157
actual bytes read from entry stream = 10
```

`ZipFile.ExtractToDirectory` goes through the same `ZipArchive`, so it writes at most the declared size. The declared-size gate is therefore adequate against *this* trick: an honest oversize archive is rejected by the sum, and a forged-small one truncates itself.

**What I changed anyway, and why it is still worth having.** `PluginZipExtractor` replaces both `ExtractToDirectory` calls and:

1. applies the cap to **bytes actually written**, so the guarantee no longer rests on a runtime implementation detail — if a future .NET stops truncating, the bound still holds;
2. re-checks entry paths in the code that creates files, instead of trusting the earlier validation pass (defence in depth against zip-slip);
3. aborts at most one 80 KB buffer past the cap, leaving the caller's existing rollback to delete the folder.

A test pins the truncation behaviour explicitly, so if it ever changes the suite says so rather than silently losing the property this issue was worried about.

Severity in hindsight: the original `sev:medium` was based on an unbounded-write premise that doesn't hold, so the real pre-existing exposure was lower than filed. The hardening is cheap and correct, so it lands regardless.

</details>

---

## archived-64 — Executor Python wrapper splices JSON into a non-raw triple-quoted literal — any double quote in the data breaks json.loads

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:services`

## What
`q()` wraps `JSON.stringify(...)` output in `"""…"""` (pythonRunner.ts:88-92). Python unescapes the literal, so JSON's `\"` becomes a bare `"` before `json.loads` sees it. `JSON.stringify` never emits a bare `"""`, so the `.replace(/"""/g, …)` guard is dead code and the real hazard is unhandled.

## Where
`services/executor/src/pythonRunner.ts:86-92`

## Why it matters
Every Python transformer whose input rows contain a double quote — i.e. most real CSV/JSON data — fails with `json.JSONDecodeError` reported as an opaque failure, while the JS path handles the same data. A trailing backslash in a value also mangles the literal.

## Evidence
```
88: function q(text: string): string {
89:   // Escape any triple-quote markers; JSON strings can contain them.
90:   const escaped = text.replace(/"""/g, '\\"\\"\\"');
91:   return `"""${escaped}"""`;
92: }
```

## Suggested fix
Pass the inputs via `pyodide.globals.set("__inputs", …)` instead of string-splicing into source (or at minimum emit a raw literal `r"""…"""`). Regression test: a row containing `"` and `\` round-trips.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: unsafe-source-interpolation|services/executor/src/pythonRunner.ts|q -->

---

## archived-65 — Datastore file download echoes the uploader's Content-Type without the sanitiser used for page attachments

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:low`, `area:api`

## What
`metadata.ContentType` is whatever the uploader put in the multipart part header and is replayed on download (DataStoreEndpoints.cs:304-307) with no allowlist, magic-byte sniff or dangerous-type downgrade — all of which `PageAttachmentEndpoints` performs (`SanitizeResponseContentType`, :230).

## Where
`src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs:304-307`

## Why it matters
`fileDownloadName` makes ASP.NET emit `Content-Disposition: attachment`, which stops inline rendering today — so defence-in-depth, not live XSS. But same-origin `text/html` / `image/svg+xml` bytes are one refactor (an inline preview, a dropped `fileDownloadName`) away from stored XSS against the session cookie.

## Evidence
```
304:                return Results.File(
305:                    content,
306:                    contentType: metadata.ContentType ?? "application/octet-stream",
307:                    fileDownloadName: metadata.Filename);
```

## Suggested fix
Reuse `PageAttachmentEndpoints.SanitizeResponseContentType` on download and its sniff check on upload (extract both into a shared helper).

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unsanitized-response-content-type|src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs|files-download -->

---

## archived-66 — Local NATS/JetStream published on all interfaces with no authentication

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:low`, `area:infra`

## What
The NATS container runs `--jetstream` with no `--auth`/`--user`/credentials and publishes `4222` and `8222` host-wide (docker-compose.yml:115-126); the Dapr endpoints in the same file are pinned to `127.0.0.1`.

## Where
`infra/docker-compose.yml:115-126`

## Why it matters
Anyone reaching the Docker host on 4222 can publish to `pipeline-code-run.>` (queue arbitrary work at the executor), subscribe to the audit-event and workflow-signal streams, and read JetStream state on 8222. The HTTP callbacks have a shared-secret boundary; NATS has none. Local-dev compose, hence low — but this file is the template people copy for staging.

## Evidence
```
118:    command:
119:      - --jetstream
124:    ports:
125:      - "4222:4222"
126:      - "8222:8222"
```

## Suggested fix
Bind `127.0.0.1:4222:4222` / `127.0.0.1:8222:8222` and add a NATS user/token that the app and executor present via `Nats:Url` credentials, injected like the Postgres/Flowable credentials.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unauthenticated-message-bus|infra/docker-compose.yml|nats -->

---

## archived-67 — 1 GiB global request-body limit + fully-materialised XLSX parsing is an OOM lever for any pipeline author

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:low`, `area:api`

## What
`Program.cs:926-928` sets `MultipartBodyLengthLimit` and `Kestrel.Limits.MaxRequestBodySize` to 1 GiB globally rather than per route; `XlsxToCsvTransformer.cs:33` base64-decodes the whole workbook into a `MemoryStream` and `XLWorkbook` materialises it fully.

## Where
`src/AutoNate.Web/Program.cs:926-928; src/AutoNate.Web/Services/Pipelines/Transformers/XlsxToCsvTransformer.cs:33`

## Why it matters
A large or deliberately bloated XLSX submitted through a pipeline expands to several times its size in managed memory on the request/worker thread; the plugin upload route only needs 50 MB and JSON routes need kilobytes, so the global limit removes the cheapest defence.

## Evidence
`Program.cs:928 Kestrel.Limits.MaxRequestBodySize = 1 GiB`; XLSX decode → `new MemoryStream(bytes)` → `new XLWorkbook(stream)`.

## Suggested fix
Set the 1 GiB limit only on the datastore upload route via `[RequestSizeLimit]`/`IHttpMaxRequestBodySizeFeature`, and give the XLSX path a row/size cap that fails cleanly.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unbounded-parser-memory|src/AutoNate.Web/Services/Pipelines/Transformers/XlsxToCsvTransformer.cs|XLWorkbook -->

---

## archived-68 — Data-connector and datastore endpoints fold raw exception text into 200/400 responses

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:low`, `area:api`

## What
`DataConnectorEndpoints.cs:150` returns `ErrorMessage: ex.Message` in a 200 body for any handler exception; `DataStoreEndpoints.cs:764` does the same with Postgres `ex.MessageText`.

## Where
`src/AutoNate.Web/Endpoints/DataConnectorEndpoints.cs:150; src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs:764`

## Why it matters
Leaks internal hostnames, connection-string fragments and Postgres error detail to any caller with `DataConnector:Connect` / datastore access — useful reconnaissance paired with the SSRF finding.

## Evidence
`return Results.Ok(new DataConnectorPreviewResult(… ErrorMessage: ex.Message …))`

## Suggested fix
Log the exception with a correlation id and return a generic message plus the id; keep detail behind `SiteConfig:Edit` or Development only.

_Found by `/n8-audit security` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: error-detail-leak|src/AutoNate.Web/Endpoints/DataConnectorEndpoints.cs|ErrorMessage -->

---

## archived-69 — Executor: NATS reconnect exhaustion silently ends the subscription loop; no unhandledRejection handler, no supervisor

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:services`

## What
`connect({ servers })` uses nats.js defaults (`maxReconnectAttempts: 10`). After 10 failed reconnects the connection closes, the `for await (const message of subscription)` iterator completes normally, `main()` resolves, and nothing notices (index.ts:19-25). `grep -c 'process.on\|maxReconnectAttempts\|closed()'` → 0; hocuspocus has `process.on` handlers, the executor does not. The executor is also absent from `infra/docker-compose.yml`, so it has no `restart:` policy.

## Where
`services/executor/src/index.ts:18-26, :66-69`

## Why it matters
NATS restarts and takes longer than ~10×2 s to come back → the process either exits 0 or idles with an empty loop; every code-node pipeline then fails with the generic 30 s timeout and an operator has to notice manually.

## Evidence
```
19:  const nc: NatsConnection = await connect({ servers: NATS_URL });
22:  const subscription = nc.subscribe(SUBJECT, { queue: "executor" });
23:  for await (const message of subscription) {
24:    void handleMessage(message);
25:  }
```

## Suggested fix
`connect({ servers, maxReconnectAttempts: -1, reconnectTimeWait: 2000 })`, `nc.closed().then(err => { log; process.exit(1) })`, and the two `process.on("unhandledRejection"/"uncaughtException")` handlers from `services/hocuspocus/src/index.ts`; add the executor to docker-compose with `restart: unless-stopped`.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: unbounded-retry-exhaustion|services/executor/src/index.ts|main -->

---

## archived-70 — PluginRuntime leaks a collectible AssemblyLoadContext on every failed plugin enable

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:plugins`

## What
`PluginAssemblyLoadContext` (`isCollectible: true`, PluginAssemblyLoadContext.cs:32) is constructed and the entry assembly loaded before validation (PluginRuntime.cs:129). The only `alc.Unload()` in the file is in `CleanupAsync` (:415); the catch at :257-261 and the other early returns never unload.

## Where
`src/AutoNate.Web/Plugins/PluginRuntime.cs:129-263`

## Why it matters
A plugin whose `Configure()` throws or whose migration fails → admin clicks Enable again (the reaction `PluginEnableFailureDetector` prompts). Each attempt pins another ALC + assembly + resolver for the process lifetime and keeps the .dll memory-mapped, so re-uploading a fixed build over the same folder fails. Silent — nothing logs the leak.

## Evidence
```
129:            PluginAssemblyLoadContext alc = new(entryFull);
257:            catch (Exception ex)
259:                scoped?.RemoveAllForPlugin();
261:                return new(false, ex.Message);
```

## Suggested fix
Track success and `finally { if (!_loaded.ContainsKey(row.Id)) alc.Unload(); }` — mirroring `CleanupAsync`. Regression test: enable a plugin whose Configure throws twice; assert `AssemblyLoadContext.All` count is unchanged.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: alc-not-unloaded|src/AutoNate.Web/Plugins/PluginRuntime.cs|EnableAsync -->

---

## archived-71 — AuditOutboxDispatcher makes untimed HTTP publishes (100 s default) inside an open Postgres transaction holding FOR UPDATE locks

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:api`

## What
`httpClientFactory.CreateClient()` (AuditOutboxDispatcher.cs:170) resolves the unnamed client registered at Program.cs:485/:922 with no `ConfigureHttpClient` → default 100 s timeout. Up to `BatchSize` (100) posts run serially between `BeginTransactionAsync` (:100) and commit.

## Where
`src/AutoNate.Web/Services/Events/AuditOutboxDispatcher.cs:99-101, :166-172; src/AutoNate.Web/Program.cs:485, :922`

## Why it matters
When the Dapr sidecar accepts TCP but stalls — the exact state `DaprStreamingSubscriber.cs:44-50` documents — the transaction can stay open ~2.8 h with 100 `audit_outbox` rows locked: `idle in transaction`, autovacuum xmin horizon pinned across the whole database, table bloat. One LogError per row, no signal that a transaction is wedged.

## Evidence
```
 99:        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
100:        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
170:            var httpClient = httpClientFactory.CreateClient();
171:            using var response = await httpClient.PostAsync(publishUri, content, cancellationToken);
```

## Suggested fix
Register a named `dapr-publish` client with `c.Timeout = TimeSpan.FromSeconds(5)` (mirroring the `"data-connector"` 30 s client at Program.cs:490 and the 3 s health-probe overrides), and consider publishing outside the transaction (claim rows → commit → publish → mark).

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: missing-timeout|src/AutoNate.Web/Services/Events/AuditOutboxDispatcher.cs|TryPublishAsync -->

---

## archived-72 — RepeatedAuthFailureDetector: unbounded singleton dictionary keyed by attacker-supplied username

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:api`

## What
`_windows` (RepeatedAuthFailureDetector.cs:37) gains one entry per distinct username seen in an `auth.login.failed` event and is never evicted — `GetOrAdd` at :131 is the only touch point.

## Where
`src/AutoNate.Web/Services/SystemIssues/Detectors/RepeatedAuthFailureDetector.cs:37, :129-141`

## Why it matters
A credential-stuffing run against the unauthenticated `POST /api/auth/login` with rotating usernames adds a `string` + `FailureWindow` + `Queue<DateTimeOffset>` per attempt to a singleton hosted service, surviving long past the 5-minute window. Millions of attempts over a weekend → steady heap growth, Gen2 pressure, eventual OOM. Silent.

## Evidence
```
37:    private readonly ConcurrentDictionary<string, FailureWindow> _windows =
131:        var window = _windows.GetOrAdd(username, _ => new FailureWindow());
```
Path: `POST /api/auth/login` → `AuthEventTopic` → Dapr → `BusWatcherStreamService` → `RepeatedAuthFailureDetector.HandleAsync` → `RecordFailure`.

## Suggested fix
Sweep `_windows` on each call (drop entries whose queue is empty after eviction) and cap the map size; regression test: 100k distinct usernames → dictionary size bounded.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: unbounded-cache-growth|src/AutoNate.Web/Services/SystemIssues/Detectors/RepeatedAuthFailureDetector.cs|_windows -->

---

## archived-73 — Dapr sidecar watchdog orphans a hung restart script and respawns it every 120 s

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
`WaitForExitAsync(timeoutCts.Token)` (DaprStreamingSubscriber.cs:253) throws on the 45 s timeout; the child is never killed (`grep -c 'Kill('` → 0) — `using var process` disposes only the wrapper. stdout/stderr are read only after exit (:254-255).

## Where
`src/AutoNate.Web/Services/Signals/DaprStreamingSubscriber.cs:250-256, :285-289`

## Why it matters
If `start-autonate-web-sidecar.sh` blocks (daprd holding a port, docker daemon hung), the watchdog logs "Sidecar restart threw", returns false, and `RestartCooldown` re-fires every 2 minutes — accumulating orphaned bash/daprd children indefinitely while pub/sub stays broken. A child producing >64 KB of output would also deadlock on the full pipe.

## Evidence
```
251:            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
253:            await process.WaitForExitAsync(timeoutCts.Token);
254:            var stdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
```

## Suggested fix
`catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }` before the general catch, and start both `ReadToEndAsync` calls before `WaitForExitAsync`.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: orphaned-process|src/AutoNate.Web/Services/Signals/DaprStreamingSubscriber.cs|TryRestartSidecarAsync -->

---

## archived-74 — Hocuspocus pg.Pool has no 'error' listener — an idle-client failure becomes an uncaughtException

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:services`

## What
`new pg.Pool(config)` (persistence.ts:17) with no `pool.on("error", …)` (0 hits). node-postgres emits `'error'` on the Pool when a backend error or network partition kills an idle client; an EventEmitter `'error'` with no listener is rethrown as an uncaught exception.

## Where
`services/hocuspocus/src/persistence.ts:14-24`

## Why it matters
Postgres restarts or a NAT drops an idle connection → uncaughtException; `index.ts:86` catches and logs so the process survives, but whatever hook was mid-flight is abandoned, and it repeats on every idle-client death.

## Evidence
```
17:   const pool = new pg.Pool(config);
```

## Suggested fix
`pool.on("error", err => console.error("[persistence] idle client error:", err));` immediately after construction, plus `connectionTimeoutMillis` in `pgConfig` (index.ts:19-25).

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: missing-error-handler|services/hocuspocus/src/persistence.ts|pool -->

---

## archived-75 — Hocuspocus auth and webhook fetches to the .NET host have no timeout (undici default 300 s)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:services`

## What
Both cross-service `fetch` calls — `auth.ts:25` (`/internal/yjs-auth`) and `webhook.ts:34` — omit `AbortSignal.timeout` (`grep -c AbortSignal` → 0 in both files).

## Where
`services/hocuspocus/src/auth.ts:25-36; services/hocuspocus/src/webhook.ts:34`

## Why it matters
If the .NET host is up but wedged (thread-pool starvation, DB pool exhaustion), every new document connection sits in `onAuthenticate` for five minutes holding an open WebSocket and a pending fetch; under a refresh storm this accumulates hundreds of pending sockets, and the failure never surfaces as the clean "could not connect" `auth.ts:38-47` intends.

## Evidence
```
25:     const response = await fetch(`${config.autonateBaseUrl}/internal/yjs-auth`, {
26:       method: "POST",
```

## Suggested fix
Add `signal: AbortSignal.timeout(5000)` to both fetches — mirrors the explicit per-dependency budgets on the .NET side (`Program.cs:647` webfetch 10 s, `:662` websearch 15 s).

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: missing-timeout|services/hocuspocus/src/auth.ts|onAuthenticate -->

---

## archived-76 — BusWatcherStreamService fans out sequentially, never forwards its CancellationToken, and logs Information per message

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`PublishAsync(message, cancellationToken)` (BusWatcherStreamService.cs:25) awaits each in-process subscriber serially (:32-34) and the delegate signature has no token parameter, so it can never be forwarded. `.editorconfig:218-222` sets `CA2016` (forward the token) to `none`, which is the rule that would have flagged it.

## Where
`src/AutoNate.Web/Services/BusWatcher/BusWatcherStreamService.cs:25-43`

## Why it matters
One slow subscriber (`WorkflowTaskNotificationListener` on a loaded DB pool) delays every other subscriber for the same message; `DaprSubscriptionOptions` uses a 30 s `MessageHandlingPolicy(Retry)`, so a pile-up causes redelivery and duplicate processing. The per-message `LogInformation` is hot-path noise.

## Evidence
```
25:    public async Task PublishAsync(BusWatcherMessage message, CancellationToken cancellationToken)
32:        foreach (var subscriber in _messageSubscribers.Values)
```

## Suggested fix
Change the subscriber signature to `Func<BusWatcherMessage, CancellationToken, Task>` and forward the token; drop the per-message log to Debug; re-enable `CA2016` at `warning` now that the codebase is otherwise clean on it.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: cancellation-not-propagated|src/AutoNate.Web/Services/BusWatcher/BusWatcherStreamService.cs|PublishAsync -->

---

## archived-77 — FlowableClient.DeleteAllWorkflowExecutionsAsync loops forever if any listed instance can't be deleted

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`while (true)` (FlowableClient.cs:755) always requests page 0 (`size=200`, no `start`) and exits only on an empty page; blank-Id rows are `continue`d and 404s on history delete count as success.

## Where
`src/AutoNate.Web/Services/Flowable/FlowableClient.cs:755-780`

## Why it matters
If any historic instance cannot actually be removed, the same page is refetched forever — an unbounded HTTP hammer on Flowable from inside a live admin request (`ExecutionEndpoints.cs:705`) with no overall time budget.

## Evidence
```
755:        while (true)
759:            using var pageResponse = await _httpClient.GetAsync(
760:                $"service/history/historic-process-instances?size={pageSize}",
```

## Suggested fix
Break when a pass deletes zero rows, and give the whole operation a time budget (or move it to a background job with progress).

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: unbounded-loop|src/AutoNate.Web/Services/Flowable/FlowableClient.cs|DeleteAllWorkflowExecutionsAsync -->

---

## archived-78 — NatsConnectionProvider caches the connection forever behind a non-volatile double-checked read and ignores the cancellation token

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`_connection` is read outside the lock without `Volatile.Read` (INatsConnectionProvider.cs:26), never re-validated, and `conn.ConnectAsync()` (:33) is called with no token although one was threaded in from `JetStreamCodeNodeRunner.cs:57`.

## Where
`src/AutoNate.Web/Services/Nats/INatsConnectionProvider.cs:24-37`

## Why it matters
If NATS.Net surfaces a terminally-closed connection, every subsequent code-node run reuses the dead handle until process restart. Contrast the correct in-repo shape: `AgentModelCatalog.GetOrLoad` uses `Volatile.Read/Write` around its double-check.

## Evidence
```
26:        if (_connection is not null) return _connection;
33:                await conn.ConnectAsync();
```

## Suggested fix
`Volatile.Read/Write` on `_connection`, pass the token to `ConnectAsync`, and drop+recreate when `ConnectionState != Open`.

_Found by `/n8-audit stability` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: singleton-race|src/AutoNate.Web/Services/Nats/INatsConnectionProvider.cs|GetAsync -->

---

## archived-79 — No CI: ~1,400 backend tests and 28 E2E specs only ever run on one developer's machine

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:ci`

## What
`.github/` contains `dependabot.yml` and `ISSUE_TEMPLATE/` but no `workflows/`; no other pipeline definition exists in the repo.

## Where
`.github/ (no workflows/)`

## Why it matters
Every other test finding is unenforced — nothing prevents a PR that deletes assertions, skips tests or breaks the auth enforcement suite. This is the M0/CI milestone's job; filing so the gap is tracked.

## Evidence
`ls .github/workflows` → No such file or directory. `find . -maxdepth 2 -iname '*ci*.yml'` → empty.

## Suggested fix
Add `.github/workflows/ci.yml` (workflow_call) running `dotnet build -warnaserror`, `dotnet test tests/AutoNate.Web.Tests` with Postgres/NATS/Redis service containers (fixtures already honour `AUTONATE_POSTGRES_PORT`/`AUTONATE_POSTGRES_PASSWORD`), `npm run lint && npm run build` in the SPA, and the Playwright suite.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: no-ci|.github/workflows|missing -->

---

## archived-80 — Yjs collaboration endpoints and both shared-secret filters have zero tests

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:tests`

## What
912 lines mapping `/api/yjs/ticket`, `/api/yjs/internal/yjs-auth` (the authority for who may edit a collaborative document) and `/yjs-webhook`; neither the endpoints nor `YjsInternalSecretEndpointFilter` / `SharedSecretEndpointFilter` / `YjsManagedContentGuard` are referenced by any test.

## Where
`src/AutoNate.Web/Endpoints/YjsEndpoints.cs (:217 `.AllowAnonymous()`, :342 `.AddEndpointFilter<YjsInternalSecretEndpointFilter>()`)`

## Why it matters
`yjs-auth` is the sole authorization decision on the realtime editing path; a regression silently opens every document to every authenticated user.

## Evidence
`grep -rl --include=*.cs -F '/api/yjs' tests` → 0; `YjsInternalSecretEndpointFilter` → 0; `SharedSecretEndpointFilter` → 0; `YjsManagedContentGuard` → 0.

## Suggested fix
Add `tests/AutoNate.Web.Tests/Authorization/YjsAuthEnforcementTests.cs`: `/api/yjs/internal/yjs-auth` denies a no-grant user, and returns 401 when the shared-secret header is absent or wrong.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: untested-endpoint|src/AutoNate.Web/Endpoints/YjsEndpoints.cs|yjs-auth -->

---

## archived-81 — DataStore uploads/copy/table-preview are untested and EntityKinds.DataStore has no enforcement test

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:tests`

## What
`POST /{id}/files`, `/files/{fileId}/copy`, `/folders/copy`, `/tables/preview` (991-line endpoint file) have no endpoint test; the only two test files touching `/api/datastores` cover list filtering and event payloads.

## Where
`src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs:219`

## Why it matters
File upload plus a no-grant→403 gap on a storage kind is the classic exfiltration surface; nothing proves the gate denies.

## Evidence
`grep -rl -F '/api/datastores' tests` → DataStoreListFilteringTests.cs, DataStoreEventPublishingTests.cs only. `EntityKinds.DataStore` appears in 0 `*EnforcementTests.cs`.

## Suggested fix
Add `tests/AutoNate.Web.Tests/Authorization/DataStoreEnforcementTests.cs` (upload/copy/download with no grant → 403) and a happy-path multipart upload test.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: enforcement-gap|src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs|files-upload -->

---

## archived-82 — POST /api/datasets/preview-file-source (untrusted file parser dispatch) has zero tests

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:tests`

## What
The endpoint resolves a parser from `DatasetFileParserRegistry` and streams an uploaded file through it; no test touches the endpoint or the registry (`CsvFileParserTests` covers the parser class in isolation only).

## Where
`src/AutoNate.Web/Endpoints/DatasetEndpoints.cs:146-205`

## Why it matters
An untrusted-file parser reachable over HTTP is a parsing/DoS surface; registry dispatch, unknown-kind handling and the auth gate are all unverified.

## Evidence
`grep -rl -F '/api/datasets' tests` → 0; `grep -rl DatasetFileParserRegistry tests` → 0.

## Suggested fix
Add `tests/AutoNate.Web.Tests/Datasets/DatasetEndpointsTests.cs`: unknown `ParserKind` → 400, malformed/oversized CSV → 400 not 500, no-grant → 403.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: untested-endpoint|src/AutoNate.Web/Endpoints/DatasetEndpoints.cs|preview-file-source -->

---

## archived-83 — Notes/pages write API and version-restore endpoints have no endpoint tests

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:tests`

## What
`/api/content/notes` (443 lines), the create/copy/tree paths of `/api/content/pages` (847 lines), `NoteVersionEndpoints.cs` and `DocumentVersionEndpoints.cs` (restore-a-revision) have no direct endpoint tests.

## Where
`src/AutoNate.Web/Endpoints/NoteEndpoints.cs, ContentPageEndpoints.cs, NoteVersionEndpoints.cs, DocumentVersionEndpoints.cs`

## Why it matters
Version restore mutates user content irreversibly and is completely unguarded by tests.

## Evidence
`grep -rl -F '/api/content/notes' tests` → 0; `grep -rlE 'notes/.*/versions' tests` → 0; `grep -rlE 'content/documents/.*/versions' tests` → 0. `/api/content/pages` matches only NotificationsTests, PageAttachmentEndpointsTests, PageEnforcementTests.

## Suggested fix
Add `tests/AutoNate.Web.Tests/NoteVersionEndpointsTests.cs` covering `POST …/notes/{id}/versions/{n}/restore` round-trip and no-grant → 403; same for documents.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: untested-endpoint|src/AutoNate.Web/Endpoints/NoteVersionEndpoints.cs|restore -->

---

## archived-84 — AgentConversationTests: UI delete assertion is neutralised by an API delete before the check

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:tests`

## What
The test clicks the UI delete affordance (line 49), then unconditionally deletes the same conversation over the API (line 52) before asserting it is gone (line 56).

## Where
`tests/AutoNate.E2E.Tests/AgentConversationTests.cs:49-56`

## Why it matters
The assertion passes even if the UI delete button is completely broken — the only test of that affordance cannot fail.

## Evidence
```
49:        await page.GetByLabel($"Delete {title}").ClickAsync();
52:        var cleanup = await page.APIRequest.DeleteAsync($"/api/agent/conversations/{conversationId}");
53:        Assert.True(cleanup.Ok || cleanup.Status == 404, await cleanup.TextAsync());
56:        await Assertions.Expect(page.GetByText(title, new() { Exact = true })).Not.ToBeVisibleAsync(
```

## Suggested fix
Assert `cleanup.Status == 404` (proving the UI already deleted it), or move the API cleanup into a `finally`/`DisposeAsync` after the visibility assertion.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: cannot-fail-assertion|tests/AutoNate.E2E.Tests/AgentConversationTests.cs|delete-conversation -->

---

## archived-85 — E2E-044/045 permission-gating journeys blocked on real SPA gaps: no client-side admin guard, unconditional delete button

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:spa`

## What
Two High-priority journeys are BLOCKED because admin pages sit under the authenticated `AppShell` with no client-side permission guard (limited users can deep-link into admin shells; APIs reject the calls) and `RecordDetail.tsx` renders its delete action unconditionally.

## Where
`docs/playwright-test-backlog.md:91-92; src/AutoNate.Spa/src/pages/records/RecordDetail.tsx; src/AutoNate.Spa/src/shell/ProtectedRoute.tsx`

## Why it matters
A documented authorization-affordance defect with no test means it will not be caught when someone assumes it was fixed. Backend gates hold, so this is UX/affordance, not exposure.

## Evidence
```
91: | E2E-044 | Limited user admin-route gating | … | High | PermissionGatingTests.cs | BLOCKED | … limited users can deep-link into admin shells.
92: | E2E-045 | Limited user record-action gating | … | High | PermissionGatingTests.cs | BLOCKED | … RecordDetail.tsx renders its delete action unconditionally.
```

## Suggested fix
Add a permission-aware route guard for `/admin/*` and gate the delete button on `useCan(record, 'delete')`; then implement both journeys in `PermissionGatingTests.cs`.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: untested-journey|docs/playwright-test-backlog.md|E2E-044 -->

---

## archived-86 — Playwright backlog: 19 of 68 journeys BLOCKED and 3 named spec files never created

`OPEN` · nathanpond · opened 2026-08-31

Labels: `bug`, `sev:medium`, `area:tests`

## What
19 rows are `BLOCKED` (49 `DONE`); `NotesAdvancedTests.cs`, `RecordsAdvancedTests.cs` and `WorkflowExecutionAdminTests.cs` are named as targets but do not exist on disk.

## Where
`docs/playwright-test-backlog.md; tests/AutoNate.E2E.Tests/`

## Why it matters
Whole journey families — notes move/copy/history-restore (E2E-050..053), typed record fields & filters (E2E-061), record relationships & revisions (E2E-062), workflow execution admin controls (E2E-060) — have no file, so nobody notices they are missing.

## Evidence
`grep -c '| BLOCKED |' docs/playwright-test-backlog.md` → 19; the three spec files → No such file.

## Suggested fix
Create `RecordsAdvancedTests.cs` with the E2E-061 typed-field filter matrix (needs the typed-schema seeder the row calls out), then the other two.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: untested-journey|docs/playwright-test-backlog.md|blocked-rows -->

---

## archived-87 — 15 EntityKinds have no no-grant→403 enforcement test (gate-presence test only proves a gate exists)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:tests`

## What
`EntityKinds.cs` declares 27 kinds. No `*EnforcementTests.cs` references SiteConfig, Plugin, SystemIssue, Cabinet, Notebook, Folder, Document, DataStore, DataConnector, Dataset, Transformer, Analyzer, Pipeline, PipelineRun, Query (nor Project / WorkflowTask beyond E2E).

## Where
`tests/AutoNate.Web.Tests/Authorization/*EnforcementTests.cs (22 files); src/AutoNate.Web/Authorization/EntityKinds.cs`

## Why it matters
`AuthorizationGatePresenceTests.EveryMappedEndpoint_HasExplicitAuthDecision` inspects route metadata only — it never proves the gate denies. A wrong `(EntityKind, Action)` pair passes presence and leaks.

## Evidence
For each kind: `grep -rl 'EntityKinds.<Kind>' tests/AutoNate.Web.Tests/Authorization/*EnforcementTests.cs` → 0.

## Suggested fix
Add enforcement tests per kind following `RecordEdgeEnforcementTests.cs` (no-grant actor → 403 on each verb, positive control with a wildcard grant). Start with Pipeline, Document, DataStore, Query.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: enforcement-gap|tests/AutoNate.Web.Tests/Authorization|missing-kinds -->

---

## archived-88 — Two [Fact(Skip)] E2E tests describe product defects with no linked issue

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:tests`

## What
`DocumentEditorTests.cs:39` (DOCX import never finalises parsed content) and `AdminOperationsTests.cs:111` (appearance Save accepts edits but reload restores the default Site name) are skipped with prose reasons and no issue number.

## Where
`tests/AutoNate.E2E.Tests/DocumentEditorTests.cs:39; tests/AutoNate.E2E.Tests/AdminOperationsTests.cs:111`

## Why it matters
A skipped test with no tracked issue is a permanently silent bug; the appearance one is data-loss-shaped (user saves, value reverts).

## Evidence
```
[Fact(Skip = "Blocked: appearance Save changes accepts edits, but reloading restores the default Site name instead of the saved value.")]
[Fact(Skip = "Blocked: DOCX import upload navigates to ?import=1, but the editor wrapper never finalizes parsed content; …")]
```

## Suggested fix
Reproduce both, file them as bugs, reference the issue number in `Skip = "#NNN — …"`, and un-skip as failing regression tests once fixed.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: skipped-no-issue|tests/AutoNate.E2E.Tests/AdminOperationsTests.cs|appearance-save -->

---

## archived-89 — DocumentEditorTests uses a 3-second sleep to wait for Yjs persistence

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:tests`

## What
`await page.WaitForTimeoutAsync(3_000);` (line 32) stands in for "the Yjs update has been persisted server-side" before reloading.

## Where
`tests/AutoNate.E2E.Tests/DocumentEditorTests.cs:30-33`

## Why it matters
Flaky by design — passes on a fast laptop, fails on a loaded CI runner, and the failure looks like a product bug.

## Evidence
```
31:        await Assertions.Expect(editor).ToContainTextAsync(bodyText);
32:        await page.WaitForTimeoutAsync(3_000);
33:        await page.ReloadAsync();
```

## Suggested fix
Poll `GET /api/content/documents/{id}/versions` (or the document's `updatedAt`) until it reflects the edit, then reload.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: sleep-sync|tests/AutoNate.E2E.Tests/DocumentEditorTests.cs|WaitForTimeoutAsync -->

---

## archived-90 — Document bindings and comments API (837 lines) has no server-side endpoint tests

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:tests`

## What
Bindings (record-field and AQL-table data injected into documents, incl. `refresh-all`) and document comments have only authorizer unit tests (`DocumentBindingAuthorizerTests`, `DocumentCommentAuthorizerTests`), no route tests.

## Where
`src/AutoNate.Web/Endpoints/ContentDocumentBindingEndpoints.cs; ContentDocumentCommentEndpoints.cs`

## Why it matters
A binding that resolves record data under the document owner's grants instead of the caller's leaks record fields into a document the caller can read.

## Evidence
`grep -rl -F 'documents/{documentId' tests` → 0; `grep -rl -F '/refresh-all' tests` → 0.

## Suggested fix
Add `ContentDocumentBindingEndpointsTests.cs` asserting `POST …/bindings/refresh-all` resolves record-field values under the caller's grants.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: untested-endpoint|src/AutoNate.Web/Endpoints/ContentDocumentBindingEndpoints.cs|refresh-all -->

---

## archived-91 — Privilege-mutation endpoints /api/admin/role-assignments and content permission-override have no tests

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:tests`

## What
`RoleAssignmentEndpoints.cs` (grants a role to a principal) and `ContentPermissionOverrideEndpoints.cs` have zero test references; `EfCoreRoleAssignmentStoreTests` covers the store, not the route or its gate.

## Where
`src/AutoNate.Web/Endpoints/RoleAssignmentEndpoints.cs; ContentPermissionOverrideEndpoints.cs`

## Why it matters
These are privilege-escalation endpoints — an authz regression here hands out roles.

## Evidence
`grep -rl -F '/api/admin/role-assignments' tests` → 0; `grep -rl -F 'permission-override' tests` → 0.

## Suggested fix
Add `RoleAssignmentEnforcementTests.cs`: non-admin `POST /api/admin/role-assignments` → 403; same shape for permission overrides.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: enforcement-gap|src/AutoNate.Web/Endpoints/RoleAssignmentEndpoints.cs|role-assignments -->

---

## archived-92 — E2E: 32 raw CSS/attribute locators in 5 spec files (tbody tr, [contenteditable])

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:low`, `area:tests`

## What
32 `Locator("…")` calls using `[contenteditable='true']`, `table`, `tbody tr` versus 813 role/label/text/placeholder queries; `PipelinesAdminTests.cs:495-497` documents picking "the first row in the body" because the semantic option was judged fragile.

## Where
`tests/AutoNate.E2E.Tests/PipelinesAdminTests.cs:495-497; DocumentEditorTests.cs:26 (+3 files)`

## Why it matters
Couples tests to DOM structure and row ordering.

## Evidence
```
DocumentEditorTests.cs:26 var editor = page.Locator("[contenteditable='true']").First;
PipelinesAdminTests.cs:495 var runsTable = main.Locator("table").First; …Locator("tbody tr").First.ClickAsync();
```

## Suggested fix
Add `data-testid` to the runs-table rows and the editor surface; swap the five files to `GetByTestId`.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: brittle-selector|tests/AutoNate.E2E.Tests/PipelinesAdminTests.cs|tbody-tr -->

---

## archived-93 — PipelinesAdminTests bypasses E2ETestBase.NewSignedInAsAdminAsync and its ConsoleErrorGuard

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:tests`

## What
`ConsoleErrorGuard` is installed automatically by `E2ETestBase.NewSignedInAsAdminAsync` (E2ETestBase.cs:38); `PipelinesAdminTests.cs` is the one spec that constructs its own page/guard and runs a 1-second `Task.Delay` poll loop outside the guard's window.

## Where
`tests/AutoNate.E2E.Tests/PipelinesAdminTests.cs:480`

## Why it matters
Minor asymmetry in an otherwise strong guard; a spec that opts out silently can hide console errors.

## Evidence
`grep -rc ConsoleErrorGuard tests/AutoNate.E2E.Tests` → E2ETestBase.cs, ConsoleErrorGuard.cs, PipelinesAdminTests.cs only.

## Suggested fix
Route `PipelinesAdminTests` through `NewSignedInAsAdminAsync()` like the other 27 specs.

_Found by `/n8-audit tests` on 2026-08-30. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: guard-bypass|tests/AutoNate.E2E.Tests/PipelinesAdminTests.cs|ConsoleErrorGuard -->

---

## #112 — POST /api/admin/projections/{name}/rebuild returns 400 for every projection — no IProjectionBackfillSource<> is implemented

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:high`, `area:api`

## What
`BackfillRunner.RunGenericAsync` resolves `IProjectionBackfillSource<TSource>` from DI and throws when none is registered (BackfillRunner.cs:58-63); `AdminProjectionsEndpoints.cs:72-93` maps that to 400. No class implements the interface (`grep -rn ': IProjectionBackfillSource<'` → 0; `FlowableExecutionBackfillSource` named in `FlowableExecutionPollingFeed.cs:14` does not exist). The SPA shows a Rebuild button per projection (`pages/admin/Projections.tsx:176` → `api/projections.ts:47`).

## Where
`src/AutoNate.Web/Services/Projections/BackfillRunner.cs:58-63; src/AutoNate.Web/Endpoints/AdminProjectionsEndpoints.cs:72-93; src/AutoNate.Spa/src/pages/admin/Projections.tsx:176`

## Why it matters
The documented recovery path (`docs/projection-framework/operations.md`) for a corrupted or retention-truncated cache — `workflow_execution_cache`, `workflow_task_cache`, `workflow_variable_cache`, `workflow_event_log_cache`, `record_activity_rollup_cache` — does not work; admins get a red "No IProjectionBackfillSource<…> registered" toast. Adjacent to archived-47 (reset-watermark) but distinct.

## Evidence
Booted `AutoNateWebApplicationFactory` in a throw-away xunit probe (deleted afterwards), listed `GET /api/admin/projections` (5 projections, all `"feeds":[]`) and POSTed `/rebuild` for each:
```
REBUILD flowable.workflow_execution_cache -> 400 {"ok":false,"message":"No IProjectionBackfillSource<WorkflowExecutionSummary> registered for projection 'flowable.workflow_execution_cache'."}
REBUILD flowable.workflow_task_cache -> 400 …<FlowableTaskSummary>…
REBUILD flowable.workflow_variable_cache -> 400 …
REBUILD flowable.workflow_event_log_cache -> 400 …
REBUILD records.record_activity_rollup_cache -> 400 …
```

## Suggested fix
Implement `IProjectionBackfillSource<WorkflowExecutionSummary>` over `IFlowableClient.GetWorkflowExecutionsAsync` (the polling feed already enumerates), register next to `AddProjection` at `Program.cs:953-966`, repeat for task/variable/event-log/rollup sources; add a test asserting 200 for each name in `GET /api/admin/projections`. Until then hide the button when `feeds` is empty or return 501 with a clear message.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: rebuild-no-backfill-source|src/AutoNate.Web/Services/Projections/BackfillRunner.cs|RunGenericAsync -->

---

## archived-113 — infra/ensure-nats-stream.sh narrows the workflow-execution stream back to workflow.execution.> on every make infra-ensure

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:infra`

## What
`ensure-nats-stream.sh:14` sets `SUBJECT="workflow.execution.>"` and `:31` runs `nats stream edit workflow-execution --subjects '${SUBJECT}' --force`. `infra/scripts/bootstrap-jetstream.sh:9-12` documents that narrowing the filter to exactly this value makes publishes to `record.*`/`application.*` fail, and sets `SUBJECTS="workflow.execution.> record.> application.> content.>"`. The narrow script is what `Makefile:28` and `infra/ensure-up.sh:391` call.

## Where
`infra/ensure-nats-stream.sh:13-33; infra/scripts/bootstrap-jetstream.sh:9-12; Makefile:28; infra/ensure-up.sh:391`

## Why it matters
Every `make infra-ensure` / `make app` re-introduces the "no response from stream" regression the bootstrap script exists to prevent; `NatsStreamProvisioner` only re-widens the subjects on the next app boot, so record/application/content events are dropped in the window between.

## Evidence
```
14: SUBJECT="workflow.execution.>"
31:         nats … stream edit ${STREAM_NAME} --subjects '${SUBJECT}' --force
```
vs bootstrap-jetstream.sh:12 `SUBJECTS="workflow.execution.> record.> application.> content.>"`.

## Suggested fix
Make `ensure-nats-stream.sh` source the subject list from `bootstrap-jetstream.sh` (or delete it and call bootstrap), and make `NatsStreamProvisioner` the single owner of the subject set (the script only creates the stream if absent).

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: stream-subjects-regression|infra/ensure-nats-stream.sh|SUBJECT -->

---

## archived-114 — services/executor is not part of the local stack (no compose service, Makefile target, or ensure-up entry)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:infra`

## What
`grep -c executor infra/docker-compose.yml Makefile infra/ensure-up.sh` → 0 / 0 / 0. The executor has a `Dockerfile` but no compose service, so nothing consumes `pipeline-code-run.>` in the documented dev stack (`services/hocuspocus` is at docker-compose.yml:162 for contrast).

## Where
`infra/docker-compose.yml; Makefile; infra/ensure-up.sh:30-41 (REQUIRED_SERVICES); services/executor/Dockerfile`

## Why it matters
A pipeline code node in dev waits for a reply that never comes and fails with the generic 30 s timeout; there is also no `restart:` policy for the one component with no supervisor (archived-69 and archived-58 assume one exists).

## Evidence
`grep -n executor infra/docker-compose.yml` → none; `grep -n hocuspocus infra/docker-compose.yml` → service present.

## Suggested fix
Add an `executor` service to `infra/docker-compose.yml` (build `../services/executor`, `NATS_URL=nats://nats:4222`, `restart: unless-stopped`), add it to `REQUIRED_SERVICES` in ensure-up.sh, and give it a health check (a NATS request to a `pipeline-code-run.ping` subject).

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: sidecar-not-in-stack|infra/docker-compose.yml|executor -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Plan:
- `infra/docker-compose.yml`: add an `executor` service modelled on `hocuspocus` (build `../services/executor`, `NATS_URL=nats://nats:4222`, `depends_on: nats` healthy, `restart: unless-stopped`).
- Health: the sidecar has no HTTP port, so add a tiny NATS health responder in `services/executor/src/index.ts` (subject `executor.health`, outside the `pipeline-code-run.>` stream capture — see archived-141) and a compose `healthcheck` that requests it.
- `infra/ensure-up.sh`: add `executor` to `REQUIRED_SERVICES` with the same build-stamp handling hocuspocus has; `Makefile`/README/docs mention.
- Verify: `make infra-ensure` brings it up healthy; a JS transformer request through NATS is answered by the compose-managed executor (the smoke from archived-139).
- Acceptance: executor container is part of `infra-ensure`, reports healthy, restarts on failure, and code nodes have a consumer in the documented dev stack.
Files: infra/docker-compose.yml, infra/ensure-up.sh, services/executor/src/index.ts, docs/codebase/Integrations.md, README.md.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-145 (merge `ed13cce1` on `master`).

- [x] `executor` service in `infra/docker-compose.yml` (build from `services/executor`, `NATS_URL=nats://nats:4222`, `restart: unless-stopped`, no ports, depends on nats healthy).
- [x] In `REQUIRED_SERVICES` of `infra/ensure-up.sh`, with the same build-input stamp scheme as hocuspocus.
- [x] Health: NATS probe `executor.health` (`healthcheck.ts`) → compose healthcheck → ensure-up readiness.

**Evidence:** `./infra/ensure-up.sh` → `autonate-executor … (healthy)`; `docker exec autonate-executor node dist/healthcheck.js` → 0; JS (isolated-vm 7) and Python (Pyodide) transformers sent over NATS answered by the compose container with the expected rows; second ensure-up run = 1 s no-op. CI: none yet (archived-79). Next thing code nodes hit is archived-141.

</details>

---

## archived-115 — System health page has no Hocuspocus probe — a collab-sidecar outage leaves /api/health/system green

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
`grep -ci 'hocuspocus\|yjs' SystemHealthService.cs` → 0, although `infra/ensure-up.sh` lists hocuspocus as a required service and every notes/pages/documents/diagram load depends on it.

## Where
`src/AutoNate.Web/Services/SystemHealth/SystemHealthService.cs:77-141`

## Why it matters
A Hocuspocus outage shows fully green health while every Y.Doc load fails in the SPA; the 5 s health poll (`useSystemHealth.ts`) exists precisely to surface this class of failure.

## Evidence
`SystemHealthService.cs` probes Postgres, Flowable, Dapr control plane, NATS; no Yjs/Hocuspocus check.

## Suggested fix
Add `CheckHocuspocusAsync` mirroring `CheckDaprControlPlaneAsync` (:125-141) — a TCP/HTTP probe on `YjsServer:HocuspocusWsUrl` with the 3 s per-instance timeout the other probes use — and add the executor liveness check from the compose issue while there.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: missing-health-probe|src/AutoNate.Web/Services/SystemHealth/SystemHealthService.cs|hocuspocus -->

---

## archived-116 — WorkflowStudio.tsx contains two literal NUL bytes, so grep classifies the 3,916-line file as binary

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:spa`

## What
Lines 2190 and 2198 build a `Map` key as `` `${topic}<0x00>${eventType}` `` with a raw NUL in the source. `perl -ne 'print "$.:".($_=~tr/\0//)."\n" if /\0/'` → `2190:1`, `2198:1`; it is the only tracked text file in the repo containing a NUL. `grep -l`/`grep -rIl` therefore skip the largest SPA file.

## Where
`src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx:2186-2200`

## Why it matters
Every grep-based audit, dead-code claim and refactor search silently misses this file — the /n8-audit run on 2026-08-30 had to reject four false "zero importers" findings caused by exactly this; the next agent will hit it again.

## Evidence
`grep -Il useWorkflowStudioPageContext WorkflowStudio.tsx` prints nothing (binary); `sed -n 74p` shows the import.

## Suggested fix
Replace both bytes with a visible separator (`"\^@"` escape or `"::"` — topic/eventType are identifiers) in one commit, and add a guard (unit test or pre-commit) that fails on `\x00` in any tracked `.ts/.tsx/.cs/.md` file.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: nul-bytes-in-source|src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx|knownEvents -->

---

## archived-117 — SPA production bundle: 3.9 MB entry chunk with no manualChunks and only 8 lazy() boundaries

`OPEN` · nathanpond · opened 2026-08-31

Labels: `performance`, `sev:medium`, `area:spa`

## What
`vite.config.ts` has no `build.rollupOptions.manualChunks` (0 hits); 8 `lazy(` sites across the SPA. The current built entry chunk `wwwroot/assets/index-*.js` is 3.94 MB; a fresh `vite build` reports ~4.46 MB / 1.29 MB gzip for the entry plus five more chunks > 500 kB (BlockNote, docx-editor ×4, Excalidraw, `@xyflow/react`, recharts, CodeMirror ×5, sucrase all in the graph).

## Where
`src/AutoNate.Spa/vite.config.ts; src/AutoNate.Spa/package.json; route table in src/AutoNate.Spa/src/routes/appRoutes.tsx`

## Why it matters
Cost shape: ~1.3 MB gzip of JS on every first load before `/api/auth/me`, and every deploy invalidates the whole bundle.

## Evidence
`ls -la src/AutoNate.Web/wwwroot/assets/index-D5dhTFRi.js` → 3.94 MB; Vite prints `(!) Some chunks are larger than 500 kB` on build.

## Suggested fix
Lazy-import the editor stacks at the route boundary (`DocumentEditorPage.tsx` already does this for docx-editor — extend to Notes/BlockNote, Excalidraw, WorkflowStudio, dashboards) and add `manualChunks` for mantine / blocknote / docx-editor / excalidraw / codemirror; set `build.chunkSizeWarningLimit` only after.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: no-code-splitting|src/AutoNate.Spa/vite.config.ts|build -->

---

## archived-118 — SPA lint cap is a ceiling, not a ratchet: 411/411 saturated, 40 exhaustive-deps warnings, 12 unused eslint-disable directives

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`

## What
`"lint": "eslint src --max-warnings=411"` equals the current count exactly, so the next warning fails lint while nothing requires the number to fall. Breakdown: `react/no-unescaped-entities` 234 (130 in `pages/admin/config/PluginDocumentation.tsx`), `react-hooks/exhaustive-deps` 40 (2 in the shared `DataTable.tsx`), jsx-a11y 97 (archived-40), `no-unused-vars` 24. `npx eslint src --report-unused-disable-directives` finds 12 unused directives (8 in `widgets/AutoConfigForm.tsx`).

## Where
`src/AutoNate.Spa/package.json:11; src/AutoNate.Spa/eslint.config.js`

## Why it matters
40 stale-closure warnings are hook-correctness bugs waiting to happen; the unused directives are free to delete and are what archived-32 (missing reasons) trips over.

## Evidence
`npx eslint src -f json` → 0 errors / 411 warnings; `--report-unused-disable-directives` → 12.

## Suggested fix
Fix `no-unescaped-entities` mechanically (drops the cap to ~177), lower `--max-warnings` in the same PR every time, add `--report-unused-disable-directives` to the lint script, and promote `react-hooks/exhaustive-deps` to error once the 40 are triaged.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: lint-ceiling|src/AutoNate.Spa/package.json|lint -->

---

## archived-119 — IFlowableReadThrough is registered in DI but injected nowhere — the executions UI never reads workflow_execution_cache

`OPEN` · nathanpond · opened 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
`grep -rln IFlowableReadThrough src/AutoNate.Web` → Program.cs (registration), the interface, and `FlowableReadThrough.cs`; zero endpoints or services inject it. The executions endpoints call `IFlowableClient` directly (archived-52) while AQL, dashboards and the authorization selector compiler read only the 60 s-poll cache.

## Where
`src/AutoNate.Web/Services/Flowable/Cache/IFlowableReadThrough.cs; src/AutoNate.Web/Services/Flowable/Cache/FlowableReadThrough.cs; src/AutoNate.Web/Program.cs`

## Why it matters
The abstraction built to reconcile live-vs-cache reads is dead code, and its absence is why the executions list has two sources of truth.

## Evidence
`grep -rn IFlowableReadThrough src/AutoNate.Web/Endpoints/*.cs` → 0.

## Suggested fix
Route `ExecutionEndpoints` through `IFlowableReadThrough` as part of archived-52, or delete the interface and implementation.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: registered-unused-service|src/AutoNate.Web/Services/Flowable/Cache/IFlowableReadThrough.cs|IFlowableReadThrough -->

---

## archived-120 — README documents AUTONATE_DATA_ROOT and /data/; the real key is Data:Root (default data/) — and the Rider run config drifted from README

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:low`, `area:docs`

## What
README.md:156 says the runtime data root is `/data/`, configurable via `AUTONATE_DATA_ROOT`; the only occurrence of that name in the repo is that line. `Storage/DataOptions.cs:5,10` binds section `Data` with `Root = "data"` resolved against the content root (`Data__Root` in containers). README:56-57 also says the `AutoNate.Web: Rider` config runs `dapr: AutoNate.Web Sidecar Status` before launch; `.run/AutoNate.Web_ Rider.run.xml:15-16` runs only `infra: Ensure Up` and has the sidecar task `enabled="false"`.

## Where
`README.md:56-57, :156; src/AutoNate.Web/Storage/DataOptions.cs:5-10; .run/AutoNate.Web_ Rider.run.xml:15-16`

## Why it matters
An operator following README mounts the wrong path and sets an env var that does nothing; a developer following the Rider flow expects a fail-fast sidecar check that never runs.

## Evidence
`grep -rn AUTONATE_DATA_ROOT README.md src` → README only; `DataOptions.SectionName = "Data"`.

## Suggested fix
Rewrite the README paragraph around `Data:Root` / `Data__Root` and the default `src/AutoNate.Web/data/`; either re-enable the Sidecar Status before-launch task in the Rider config or fix README to match.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: readme-drift|README.md|AUTONATE_DATA_ROOT -->

---

## archived-121 — Test factory sets Flowable:BaseAddress but the option is Flowable:BaseUrl — binds to nothing

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:tests`

## What
`AutoNateWebApplicationFactory.cs:61` sets `["Flowable:BaseAddress"] = "http://localhost/flowable"`; `Configuration/InfrastructureOptions.cs:7` declares only `BaseUrl`. Tests run with `BaseUrl = ""`, masked because `IFlowableClient` is replaced by `StubFlowableClient`.

## Where
`tests/AutoNate.Web.Tests/AutoNateWebApplicationFactory.cs:61; src/AutoNate.Web/Configuration/InfrastructureOptions.cs:7`

## Why it matters
Any test that un-stubs Flowable (or any options validation added for `BaseUrl`) fails with an empty base URL for a reason nobody will look for in the factory.

## Evidence
`grep -n 'Flowable:Base' AutoNateWebApplicationFactory.cs` → `BaseAddress`; `grep -n BaseUrl InfrastructureOptions.cs` → the only property.

## Suggested fix
Rename the key to `Flowable:BaseUrl`; consider a `ValidateOnStart` on `FlowableOptions.BaseUrl` outside Development.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: config-key-drift|tests/AutoNate.Web.Tests/AutoNateWebApplicationFactory.cs|Flowable:BaseAddress -->

---

## archived-122 — ScopedSubscriptionsOptions (Features:ScopedSubscriptions:Enabled) is declared and never read

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:low`, `area:api`

## What
The class declares `SectionName = "Features:ScopedSubscriptions"` and `Enabled = false`; `grep -rn ScopedSubscriptionsOptions src` → only the class itself. No appsettings file mentions the section.

## Where
`src/AutoNate.Web/Services/BusWatcher/Subscriptions/ScopedSubscriptionsOptions.cs`

## Why it matters
An operator toggling the flag changes nothing; the scoped-subscription feature is unconditionally on via `AddScopedSubscriptions()` (Program.cs:236).

## Evidence
1 reference repo-wide.

## Suggested fix
Delete the class, or bind it and honour `Enabled` in `AddScopedSubscriptions`.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: dead-options-class|src/AutoNate.Web/Services/BusWatcher/Subscriptions/ScopedSubscriptionsOptions.cs|ScopedSubscriptionsOptions -->

---

## archived-123 — Agents.codex.md prescribes `npx playwright test` against :5173; the E2E suite is Playwright .NET on a random port

`OPEN` · nathanpond · opened 2026-08-31

Labels: `documentation`, `sev:low`, `area:docs`

## What
`Agents.codex.md` (loaded by `.codex/config.toml`) lists `npm ci`, `npx playwright install`, `npx playwright test` as required checks and `localhost:5173` for exploration; the actual suite is `tests/AutoNate.E2E.Tests` (xUnit + Microsoft.Playwright) launched via `dotnet test` with `AutoNateE2EFixture` on a random port.

## Where
`Agents.codex.md; .codex/config.toml; tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs`

## Why it matters
A Codex session following it runs checks that don't exist and never runs the real suite.

## Evidence
`ls tests/AutoNate.E2E.Tests/*.csproj`; no `playwright.config.*` anywhere in the repo.

## Suggested fix
Rewrite the required checks to `dotnet test tests/AutoNate.E2E.Tests` (+ infra prerequisites from docs/codebase/Testing.md), or delete the file if Codex is no longer used.

_Found by `/n8-map` on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: stale-agent-instructions|Agents.codex.md|required-checks -->

---

## archived-124 — Spike: schema initialisation has no advisory lock and no version ledger

`OPEN` · nathanpond · opened 2026-08-31

Labels: `needs-triage`, `spike`, `area:api`

## What
`DatabaseSchemaInitializer.EnsureAsync` runs 3,827 lines of idempotent SQL on every boot with no `pg_advisory_lock` and no schema-version table (`grep -n 'pg_advisory\|schema_version'` → 0). `dotnet-ef` is pinned in `dotnet-tools.json` but there are zero EF migrations. `infra/postgres/init/02-create-autonate-app-schema.sql` is a hand-synced second copy replayed by both test fixtures; the only model-vs-schema test checks four table names.

## Where
`src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs; infra/postgres/init/02-create-autonate-app-schema.sql; dotnet-tools.json`

## Why it matters
Two hosts booting concurrently can race DDL; nobody can answer "which schema version is this database at"; the init SQL copy drifts silently. This shapes any multi-instance or zero-downtime deployment story.

## Evidence
See docs/codebase/Concerns.md §2 and Architecture.md (schema ownership).

## Suggested fix
Decision to make in `/n8-roadmap`: (a) wrap `EnsureAsync` in `pg_advisory_xact_lock` + a `schema_versions` ledger and generate the init SQL from it, or (b) move to EF migrations. Time-box the spike; close with a decision comment and follow-up stories.

_Found by `/n8-map` on 2026-08-31. Filed as a design spike for `/n8-roadmap`._

<!-- fingerprint: design-schema-versioning|src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs|EnsureAsync -->

---

## archived-125 — Spike: executions have two sources of truth (Flowable live reads vs workflow_execution_cache)

`OPEN` · nathanpond · opened 2026-08-31

Labels: `needs-triage`, `spike`, `area:api`

## What
The executions UI reads Flowable live through `IFlowableClient` (archived-52), while AQL, dashboards and the `WorkflowExecutionCacheSelectorCompiler` read `workflow_execution_cache` populated by a 60 s poll; `IFlowableReadThrough` — built to reconcile the two — is injected nowhere.

## Where
`src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs; src/AutoNate.Web/Services/Flowable/Cache/*; src/AutoNate.Web/Authorization/… WorkflowExecutionCacheSelectorCompiler`

## Why it matters
Authorization decisions and list views can disagree for up to 60 s; every performance fix on the list (archived-52) implicitly picks a side. This is a design decision, not a bug fix.

## Evidence
See docs/codebase/Concerns.md §2 and Architecture.md (workflows section).

## Suggested fix
Decide whether the cache is the read model (then the UI reads it and the poll interval/CDC becomes the SLA) or Flowable is (then the cache is only an index for AQL/authorization and archived-52 needs a Flowable-side query). Close with a decision comment; feed archived-52 and the IFlowableReadThrough issue.

_Found by `/n8-map` on 2026-08-31. Filed as a design spike for `/n8-roadmap`._

<!-- fingerprint: design-execution-source-of-truth|src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs|source-of-truth -->

---

## archived-126 — Spike: runtime dependency surface — 9 containers for a single-host app; Redis exists only for Dapr actor state

`OPEN` · nathanpond · opened 2026-08-31

Labels: `needs-triage`, `spike`, `area:infra`

## What
Local/prod topology: Postgres, Flowable, Redis, NATS, Dapr placement, Dapr scheduler, Dapr sidecar, hocuspocus, executor. Redis is touched only by the Dapr state-store health probe — no feature uses it. The audit-outbox dispatcher POSTs raw HTTP to Dapr `/v1.0/publish` while the executor and stream provisioner already talk to NATS natively via NATS.Net, so the Dapr pub/sub hop carries a message path that has an in-process client.

## Where
`infra/docker-compose.yml; src/AutoNate.Web/Services/Events/AuditOutboxDispatcher.cs; src/AutoNate.Web/Services/Signals/DaprStreamingSubscriber.cs; src/AutoNate.Web/Services/Nats/*`

## Why it matters
Operational surface, restart-supervision gaps (archived-69), the self-restarting-sidecar path in `DaprStreamingSubscriber`, and a 100 s outbox timeout (archived-71) all trace back to the Dapr hop. Collapsing it is a roadmap-level decision with deployment consequences.

## Evidence
See docs/codebase/Integrations.md and Concerns.md §2.

## Suggested fix
Time-boxed spike: prototype the outbox dispatcher publishing to NATS JetStream directly and `BusWatcher` consuming from it; enumerate what still needs Dapr (workflow signals from the Flowable extension?) and Redis. Close with a decision comment and follow-up stories.

_Found by `/n8-map` on 2026-08-31. Filed as a design spike for `/n8-roadmap`._

<!-- fingerprint: design-runtime-surface|infra/docker-compose.yml|services -->

---

## archived-132 — Playwright E2E suite fails 100% on master: fixture-launched app never renders the login form (all tests time out in SignInAsync)

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:tests`

## What
Running `dotnet test tests/AutoNate.E2E.Tests --filter RecordsCrudTests|ManageUsersTests|NotificationsTests` on `master` (f68dc371 and again after archived-111) fails every test after exactly 30 s with `TimeoutException … waiting for GetByRole(Textbox, Name="Username")` at `AutoNateE2EFixture.SignInAsync` (:100). The fixture does start the app (`Now listening` is detected, wwwroot is rebuilt — index.html mtime matches the run), and infra postgres/nats/redis/hocuspocus were up, but **Flowable was not running** (app log: `Connection refused (localhost:8080)`). The same app launched with the launch profile (SpaProxy → Vite on :5173) renders the login form immediately.

## Where
`tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs:86-104, :134-162; src/AutoNate.Web/AutoNate.Web.csproj:12-24 (SpaProxy), infra/ensure-up.sh (Flowable in REQUIRED_SERVICES)`

## Why it matters
The E2E suite is the only automated coverage for 49 journeys (docs/playwright-test-backlog.md) and was the intended gate for the mantine-datatable 9 bump (archived-111) — it could not be used. Either the fixture has an undocumented dependency (Flowable up? a running Vite dev server?) or the wwwroot-served SPA fails to boot in the fixture path; the tests give no signal which, because the guard reports only the element timeout. Also: the .NET Playwright package (1.50.0) needs browser build `chromium_headless_shell-1155`, which was absent from the cache — `pwsh bin/…/playwright.ps1 install` (or `npx playwright@1.50.0 install chromium-headless-shell`) is an undocumented prerequisite.

## Evidence
Runs on 2026-08-31: master+archived-111 → `Failed: 14, Passed: 0` (7 m 1 s); master control → `Failed: 13, Passed: 0` (6 m 31 s); every error identical. `docker ps` → autonate-postgres/nats/redis/hocuspocus only. Smoke: `dotnet run --project src/AutoNate.Web -p:BuildSpa=true` (launch profile) → SpaProxy redirect to :5173 → login form renders (Playwright snapshot shows textbox "Username"). First attempt with the stale browser cache → `Executable doesn't exist at …/chromium_headless_shell-1155/…`.

## Suggested fix
1) Make the fixture fail fast with a diagnostic when `/` doesn't serve the SPA: after `Now listening`, GET `/` and assert 200 + `<div id="root">`, and dump the app's stderr/stdout tail + the page's console errors on the first timeout. 2) Verify whether the login page needs Flowable (or anything else in `infra/ensure-up.sh`'s REQUIRED_SERVICES) and either start it in the fixture or document it in docs/codebase/Testing.md. 3) Add the Playwright browser-install step to Testing.md and to the future CI workflow (archived-79).

_Found by `Dependabot PR review` on 2026-08-31. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: e2e-harness-broken|tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs|SignInAsync -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Plan:
- Reproduce the fixture path exactly (`dotnet run --no-launch-profile -p:BuildSpa=true`, `ASPNETCORE_URLS=http://127.0.0.1:<port>`, auto-login off, Dapr probe skipped, test DB) and inspect `/` with curl + a real browser to find why the login form never renders.
- Fix the root cause in the fixture and/or the host's static-file pipeline.
- Make the fixture fail fast with a useful diagnostic (assert `/` serves the SPA shell after `Now listening`; dump app stdout/stderr tail + page console on the first sign-in timeout) so this class of failure is never a bare 30 s element timeout again.
- Document the prerequisites (Playwright browser install for Microsoft.Playwright 1.50.0, required infra services) in `docs/codebase/Testing.md`.
- Acceptance: `dotnet test tests/AutoNate.E2E.Tests --filter RecordsCrudTests|ManageUsersTests` passes on this machine; a deliberately broken SPA shell produces a clear fixture error instead of 13 identical timeouts.
Files expected: `tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs`, possibly `src/AutoNate.Web/Program.cs`, `docs/codebase/Testing.md`.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-136 (merge `08e56246` on `master`).

**Root cause:** `bdc72176` changed the SPA fallback to `{*path:nonfile:regex(^(?!api(/|$)))}`; `RegexRouteConstraint` returns false for a missing catch-all value, so `GET /` stopped matching and was a bare 404 while deep links still served the shell. Every spec starts at `/` (`SignInAsync`), hence 27/27 identical sign-in timeouts. Flowable being down was a red herring; the Playwright browser-build mismatch was a separate, now-documented prerequisite.

**Acceptance criteria**
- [x] Fixture path reproduced exactly (`--no-launch-profile -p:BuildSpa=true`): `GET /` → 404 / 0 B, `/home` → 200 shell, `/api/auth/me` → 200 — confirmed the fallback route as the cause.
- [x] Fix: explicit `MapFallbackToFile("/", "index.html")` beside the constrained catch-all (`Program.cs`).
- [x] Fixture fails fast: `AutoNateE2EFixture.AssertSpaShellServedAsync` probes `/` after `Now listening` and throws with the app's stdout/stderr tail.
- [x] Prerequisites documented in `docs/codebase/Testing.md` (browser install for bare `dotnet test`, the two guards).

**Tests**
- `SpaRootFallbackTests` (new): 6/6 pass; with the fix reverted → 1 failure, exactly `SpaRoutes_ServeTheShell(path: "/")`.
- E2E slice `RecordsCrudTests|ManageUsersTests|NotificationsTests`: **Passed 14 / Failed 0 (12 s)** — was 14/14 timeouts on `master` before the fix.
- CI: none exists yet (archived-79).

</details>

---

## archived-139 — Standardise the Node.js runtime on 24 (Active LTS) across sidecars, dev and build

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `feature`, `area:services`

## What
Standardise the Node.js runtime on the current Active LTS (24) everywhere: the two sidecar images (`services/hocuspocus`, `services/executor`) are on `node:22-alpine`, local dev and the .NET-driven SPA build run whatever is on `PATH` (24.x today), and nothing pins a version.

## Acceptance criteria
- [ ] `.nvmrc` and `engines.node` (root, SPA, both sidecars) declare Node 24.
- [ ] Both sidecar Dockerfiles build from `node:24-alpine`; `docker build` succeeds for each.
- [ ] Executor sandbox executes a JS transformer end-to-end through NATS on the rebuilt image.
- [ ] `dependabot.yml` tracks the Docker base images so 24 → 26 arrives as a PR once 26 is LTS.
- [ ] `docs/codebase/Stack.md` reflects the pinned version.

## Notes
Unblocks archived-102 (isolated-vm 7 requires Node ≥ 24). archived-105 / archived-101 (`@types/node` 26) are closed rather than merged — types track the runtime major; they return as a 24 → 26 bump later. Node 26 is Current until it enters LTS in October 2026; revisit then.

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-140 (merge `7df45faa` on `master`).

**Acceptance criteria**
- [x] `.nvmrc` and `engines.node` (`>=24 <25`) in root, SPA, hocuspocus, executor.
- [x] Both sidecar Dockerfiles on `node:24-alpine`; `docker build` succeeds for each.
- [x] Executor sandbox executes a JS transformer (isolated-vm 7.0.1) **and** a Python transformer (Pyodide) end-to-end via NATS on the rebuilt image — both return the expected rows.
- [x] `dependabot.yml` tracks the two Docker base images.
- [x] `docs/codebase/Stack.md` reflects the pin; `.n8/decisions.md` records the policy (revisit Node 26 when it enters LTS, Oct 2026).

**Found on the way**
- isolated-vm 5 cannot compile against Node 24 (`#error "C++20 or later required."`) → moved to 7.0.1, superseding archived-102.
- The executor image never worked, even on `node:22` — `--ignore-scripts` skipped the native build (control build reproduced `Cannot find module './out/isolated_vm'` on master). Fixed in the Dockerfile; npm ≥ 11.19's install-script approval added.
- archived-141 filed: the .NET runner's plain NATS request receives JetStream's publish ack before the executor's reply.

**Tests:** image builds ×2, runtime boot ×2, sandbox smoke 2/2; CI: none yet (archived-79).

</details>

---

## archived-141 — JetStreamCodeNodeRunner uses a plain NATS request on a stream-captured subject — the first reply is JetStream's publish ack, not the executor's CodeNodeReply

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:high`, `area:api`

## What
`JetStreamCodeNodeRunner.RunCodeAsync` publishes the `CodeNodeRequest` with `nats.RequestAsync<byte[],byte[]>` (JetStreamCodeNodeRunner.cs:58) on `pipeline-code-run.<runId>.<nodeId>`. `NatsStreamProvisioner` provisions the `pipeline-code-runs` stream over `pipeline-code-run.>`, and JetStream sends a **PubAck** (`{"stream":"pipeline-code-runs","seq":N}`) to the reply subject of every captured message. A core request takes the first reply, so the runner deserialises the ack as a `CodeNodeReply` (`Success=false`, `ErrorMessage=null`) and reports a failure — the executor's real reply arrives on the same inbox a few ms later and is discarded.

## Where
`src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs:49-69; src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs:97-101; services/executor/src/index.ts:22`

## Why it matters
Every code-transformer / analyzer node fails as soon as a working executor is attached (until now the executor image never booted — archived-114/archived-139 — so the failure mode was a 30 s timeout instead). With the fixed image the node fails instantly with an empty error.

## Evidence
Smoke test 2026-08-31 against the Node 24 executor image: `nc.request("pipeline-code-run.smoke.node1", …)` → `{"stream":"pipeline-code-runs","seq":1}` (JS) / `seq:2` (Python) in ~5 ms. Same payloads published to an explicit inbox with the ack skipped → `{"success":true,"output":{rows:[{x:1,doubled:2},…]}}` for both languages. `NatsStreamProvisioner` log on app boot: `JetStream stream 'pipeline-code-runs' is ready (subjects: pipeline-code-run.>)`.

## Suggested fix
Either (a) stop capturing the request subject in a stream — request/reply is core NATS; delete the `pipeline-code-runs` stream from `DesiredStreams` (the executor subscribes with core `nc.subscribe`, see archived-49) — or (b) keep the stream and make the runner subscribe to an explicit inbox, skipping any reply without a `success` field (the smoke script's shape). (a) is simpler and matches what the sidecar actually does. Regression test: a fake executor answering on the reply subject after a JetStream ack; the runner must return the executor's reply.

_Found by `Node 24 executor smoke test (archived-139)` on 2026-08-31. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: jetstream-ack-shadows-reply|src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs|RequestAsync -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Plan:
- `NatsStreamProvisioner`: stop provisioning the `pipeline-code-runs` stream over `pipeline-code-run.>` — the executor is a core-NATS queue subscriber (archived-49), so the stream only exists to intercept request/reply with a PubAck.
- `JetStreamCodeNodeRunner`: read replies from an explicit inbox and ignore anything that isn't a `CodeNodeReply` (defence in depth if a stream ever captures the subject again); keep the existing timeout/cancellation semantics.
- Regression test against the real NATS in the test infra: provision a throw-away stream that captures a test subject, run a fake executor that answers after JetStream's ack, and assert the runner returns the executor's reply — fails on today's code.
- Acceptance: test passes; `NatsStreamProvisioner` no longer declares the stream; docs/codebase/Integrations.md updated.
Files: src/AutoNate.Web/Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs, src/AutoNate.Web/Services/Nats/NatsStreamProvisioner.cs, tests/AutoNate.Web.Tests/…

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-146 (merge `9c7b1ca1` on `master`).

- [x] `pipeline-code-runs` stream no longer provisioned; listed in `LegacyStreamsToRemove` — live boot logged `Removed legacy JetStream stream 'pipeline-code-runs'`, `nats stream ls` afterwards shows only `workflow-execution`.
- [x] `JetStreamCodeNodeRunner` reads an explicit inbox and ignores anything not shaped like a `CodeNodeReply`.
- [x] Regression test `JetStreamCodeNodeRunnerTests` (real NATS, throw-away capturing stream, fake executor answering after the ack): 6/6 pass; the integration case fails on the old runner with `Executor sidecar reported an unknown failure`.
CI: none yet (archived-79).

</details>

---

## archived-147 — Four DataStoresAdminTests E2E specs are stale: file-store detail moved to the SVAR file manager and file-backed datasets now require picking a file

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:tests`

## What
Full suite on `master` (9c7b1ca1): **136 passed, 4 failed, 2 skipped**. All four failures are in `DataStoresAdminTests` and reproduce by hand with no console or network errors:
- `DataStores_FileStoreDetail_UploadAppearsInList` / `_NewFolderAppearsInList` wait 30 s for buttons named `Upload file` / `New folder`. The detail page has been `DataStoreFileManager.tsx` (SVAR file manager: `Upload to current folder`, `Add New`, folder tree, context-menu `Upload file`) since `706814ee` (2026-06-06); the spec was last updated 2026-06-03.
- `Datasets_CreateOverFileStore_PicksFromDropdownAndPersists` / `Datasets_EditExisting_PersistsRenamedRow` expect the modal to close on Create/Save after picking a FileType store. Since the Files-backed datasets work (`98d99c82`) the modal renders Scope / Browse folder / File / Parser controls and blocks with the alert **"Pick a file before saving."** — the spec never uploads a file.

## Where
`tests/AutoNate.E2E.Tests/DataStoresAdminTests.cs:94-180, :456-590; src/AutoNate.Spa/src/pages/admin/datastores/DataStoreFileManager.tsx; the datasets create/edit modal`

## Why it matters
Four permanently red specs hide real regressions in the same file; the two file-store specs are the only coverage of folder/upload flows and currently exercise nothing.

## Evidence
`dotnet test tests/AutoNate.E2E.Tests` 2026-08-31: `Failed: 4, Passed: 136, Skipped: 2, Duration: 4 m 2 s`. Browser repro on a fixture-style host: detail page snapshot shows `button "Upload to current folder"`, `button "Add New"`, no `Upload file`/`New folder` buttons, 0 console errors; dataset modal after selecting `e2e-repro-ds (FileType)` shows `alert: Pick a file before saving.` and no POST is issued. `git log` dates above.

## Suggested fix
Rewrite the two detail specs against the file manager (`Upload to current folder` → dropzone `SetInputFiles`; `Add New` → folder) and make the two dataset specs upload a small CSV to the store first, pick it under *File*, then Create/Save (or switch them to a SqlType store where the fixture allows). Consider a `data-testid` on the file-manager toolbar buttons — their accessible names come from the third-party widget.

_Found by `full E2E run` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: stale-e2e-spec|tests/AutoNate.E2E.Tests/DataStoresAdminTests.cs|file-store-and-dataset-specs -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Plan:
- Rewrite `DataStores_FileStoreDetail_UploadAppearsInList` / `_NewFolderAppearsInList` against `DataStoreFileManager.tsx` (SVAR file manager): drive the upload through the dropzone's file input / `Upload to current folder`, create a folder via `Add New`, assert the entries appear in the manager's list.
- Rewrite `Datasets_CreateOverFileStore_PicksFromDropdownAndPersists` / `Datasets_EditExisting_PersistsRenamedRow`: seed a small CSV into the file store via `POST /api/datastores/{id}/files` (`page.APIRequest`, same pattern as `AgentConversationTests`), then pick it under *File* in the modal before Create/Save.
- Add `data-testid`s only where the third-party widget gives no stable accessible name.
- Acceptance: `dotnet test --filter DataStoresAdminTests` fully green; full suite back to 0 failures apart from the 2 known skips.
Files: tests/AutoNate.E2E.Tests/DataStoresAdminTests.cs (+ possibly DataStoreFileManager.tsx test ids).

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-148 (merge `9f115c99` on `master`).

- [x] Two file-store detail specs rewritten against `DataStoreFileManager` (SVAR): toolbar upload modal + `Add New → Add new folder → .wx-modal` prompt.
- [x] Two dataset specs seed a CSV over `POST /api/datastores/{id}/files` and pick it under *File* before Create/Save.
- [x] No `data-testid` needed yet — exact-text targeting works; noted as the fallback if the third-party widget proves brittle.

**Evidence:** `dotnet test tests/AutoNate.E2E.Tests --filter DataStoresAdminTests` → **Passed 16 / Failed 0** (20 s); was 12/16 on `master`. Every flow was reproduced by hand in a browser before being encoded. With this, the full suite should be 140 passed / 0 failed / 2 skipped. CI: none yet (archived-79).

</details>

---

## archived-150 — Migrate BlockNote 0.51 → 0.54 (comments API) across SPA and hocuspocus

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `feature`, `area:spa`

## What
Migrate the collaborative editors from BlockNote 0.51 to 0.54 (SPA `@blocknote/{core,react,mantine}` and hocuspocus `@blocknote/{core,server-util}` in lock-step). 0.54 removed `YjsThreadStore` and `User` from `@blocknote/core/comments`, which `src/lib/yjs/commentAudit.ts`, `useBlockNoteWithYjs.ts` and `useResolveUsers.ts` depend on — the reason Dependabot's SPA group (archived-107/archived-131/archived-138) and the hocuspocus group (archived-104) could not merge.

## Acceptance criteria
- [ ] SPA and hocuspocus on the same BlockNote version (0.54.x); `tsc`/build clean in both.
- [ ] Comment threads on notes/pages still create, resolve and audit (the `commentAudit` hooks) — verified by the notes/documents E2E specs and a manual thread round-trip.
- [ ] Yjs document format unchanged: an existing note opens and edits after the bump (no re-migration of stored Y.Docs).
- [ ] archived-104 (hocuspocus group) can then land; a follow-up pairs `@hocuspocus/provider` 4.6 with `@hocuspocus/server` 4.6.

## Notes
Dependency-driven migration (step 3 of the Dependabot plan, 2026-08-31). The docx-editor deprecation is a separate roadmap question and stays excluded.

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-152 (merge `00f1eb38` on `master`).

- [x] SPA and hocuspocus on BlockNote **0.54.0**; `tsc`/lint/Vite build and hocuspocus `tsc` clean. `@tiptap/core` override removed (single 3.30.5 resolves naturally); `y-prosemirror` now a direct SPA dependency.
- [x] Comment threads through the migrated store: `createThread` → 1 thread, `resolveThread` → `resolved: true`, `deleteThread` → gone from store and raw Y.Map. The audit POST fails with 400 for a **pre-existing** contract mismatch (archived-151) — not introduced here.
- [x] Existing dev page (`Hawaii: The Aloha State`: headings, tables, lists, marks) opens with zero console errors; edit mode + formatting toolbar with *Add comment* works for the editor role. Y.Doc format untouched.
- [x] Full Playwright suite against a rebuilt hocuspocus (server-util 0.54.0): **140 passed / 0 failed / 2 skipped**.
- [ ] `@hocuspocus/provider` 4.6 + `@hocuspocus/server` 4.6 (Dependabot archived-104) — now unblocked; next step.

CI: none yet (archived-79).

</details>

---

## archived-151 — Comment audit events never reach the server: SPA posts `documentName`, POST /api/yjs/comment-event requires `pageId` → always 400

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
`src/AutoNate.Spa/src/lib/yjs/commentAudit.ts` (the `wrapThreadStoreWithAuditing` proxy around the BlockNote thread store) POSTs `{ documentName: "page:<guid>", threadId, commentId?, eventType }` — its own comment says ".NET resolves it back to the parent pageId". `YjsEndpoints.cs:156-171` binds `CommentEventRequest(PageId, ThreadId, CommentId, EventType)` and returns 400 `"pageId, threadId, eventType required."` when `PageId` isn't a GUID. Both sides were introduced in the same commit (`1b0cd589`, 2026-05-16), so the round-trip has never worked.

## Where
`src/AutoNate.Spa/src/lib/yjs/commentAudit.ts:6-12, :36-60; src/AutoNate.Web/Endpoints/YjsEndpoints.cs:156-171, :786-791`

## Why it matters
Every comment create/reply/resolve/reopen/delete on notes and pages is silently dropped from the audit bus (the client only `console.warn`s), so the `content.events` catalog entries for comments are dead and nothing downstream (notifications, bus watcher, plugins) ever sees comment activity.

## Evidence
Manual run 2026-08-31 on a dev page with the migrated (0.54) store: `createThread` → `POST /api/yjs/comment-event` → **400**; console: `[yjs] comment-event created for page:426344ed-…/3333aa0c-… failed: AxiosError: Request failed with status code 400`. Server rule: `if (!Guid.TryParse(request.PageId, …)) return BadRequest`.

## Suggested fix
Pick one contract and add a test. Preferred: keep the client's `documentName` (it carries the `page:`/`note:` kind the server needs to resolve notes to their parent page) and make the endpoint accept `documentName`, resolving `note:<guid>` → parent page for the `Page.View` authorization and the `pageId` in the event payload; keep accepting `pageId` for compatibility. Regression test: an endpoint test posting the exact client body shape → 202/204 and a recorded audit event; plus a Playwright spec that creates a comment thread and asserts the POST succeeds (the UI composer needs a real mouse flow — see the archived-150 notes).

_Found by `BlockNote 0.54 migration smoke test (archived-150)` on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: contract-mismatch|src/AutoNate.Web/Endpoints/YjsEndpoints.cs|comment-event -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan

Taking the issue's preferred contract: the client keeps sending `documentName` (it carries the `page:`/`note:` kind), the endpoint learns to resolve it.

1. `src/AutoNate.Web/Endpoints/YjsEndpoints.cs` — `CommentEventRequest` gains `DocumentName` (nullable) alongside the existing `PageId` (kept for compatibility). Handler resolves the target with the same `TryParseDocumentName` + note→parent-page lookup the `/ticket` endpoint already uses (`page:` → itself; `note:`/`napkin:`/`diagram:` → `Notes.PageId`, 404 if missing); `pagemeta:`/`document:` are rejected (no BlockNote comment threads there). Authorization stays `Page.View` on the resolved page. Event resource payload gains `documentName` and `noteId` so consumers can tell a note thread from a page thread.
2. `tests/AutoNate.Web.Tests/YjsCommentEventEndpointTests.cs` — posts the exact client body shape: page doc → 204 + recorded `content.comment.created` with the pageId; note doc → 204 with the *parent* pageId + noteId; unknown/missing docname → 400; legacy `pageId` body → still 204.
3. Playwright spec: create a comment thread through the BlockNote UI and assert the `comment-event` POST returns 204 — attempted after 1–2; if the composer's mouse flow proves too flaky for the suite it gets noted here rather than landing a flaky spec.
4. Client `commentAudit.ts` needs no change beyond its comment; it will be verified end-to-end in step 3.

Branch `fix/151-comment-event-contract`; PR with `Closes archived-151`.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Fixed on `master` via the PR above.

**Evidence**
- `tests/AutoNate.Web.Tests/YjsCommentEventEndpointTests.cs`: 9/9 green with the fix; `PageDocumentName_PublishesCommentCreatedForThatPage` is red against the previous handler (`Failed: 1`), so the guard is real.
- `NotesTests.NotesPage_AddCommentOnRichTextNote_PostsCommentEventForNoteDocument`: real BlockNote *Add comment* flow on a richtext note → `POST /api/yjs/comment-event` **204** with `documentName: note:<guid>`, `eventType: created`.
- Full E2E on the branch: 141 passed / 0 failed / 2 skipped (both pre-existing skips). Web.Tests neighbourhood (Yjs, ContentAuthorizer, ContentTree, PageAttachment, NotesQuery): 40/40.

**Contract now**: `documentName` (`page:` or `note:`/`napkin:`/`diagram:`, resolved to the parent page for `Page.View`) or legacy `pageId`; event payload gains `noteId` + `documentName`.

</details>

---

## archived-154 — Hocuspocus 4.0 → 4.6: bump server and provider together

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `feature`, `area:services`

## What
Move the Yjs transport to Hocuspocus 4.6 on both ends together: `@hocuspocus/server` 4.0 → 4.6 in `services/hocuspocus` (with the rest of Dependabot archived-104: pg 8.23, @types/pg, yjs 13.6.32, react 19.2.8 patches) and `@hocuspocus/provider` 4.0 → 4.6 in the SPA (excluded from the SPA group so it can only move with the server).

## Acceptance criteria
- [ ] hocuspocus builds and boots on 4.6; SPA `tsc`/build clean with provider 4.6; `yjs` on the same version in both.
- [ ] A collaborative page/note syncs end-to-end against the rebuilt sidecar (E2E notes/documents specs + an existing dev page opens with no console errors).
- [ ] Ticket auth (`/internal/yjs-auth`) and the `/internal/yjs-webhook` materializer still work (hocuspocus log + page snapshot persisted).
- [ ] Dependabot archived-104 closed as superseded.

## Notes
Follows the BlockNote 0.54 migration (archived-150). Hocuspocus 4.x minor releases: check the changelog for `onAuthenticate` / extension API changes before bumping.

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closed by PR archived-156 (merge `30d44a99` on `master`).

- [x] hocuspocus builds/boots on `@hocuspocus/server` 4.6.0; SPA on `@hocuspocus/provider` 4.6.0; `yjs` 13.6.32 on both.
- [x] Full Playwright suite against the rebuilt sidecar: 140 passed / 0 failed / 2 skipped.
- [x] Manual round-trip on a throwaway dev page: typed text → hocuspocus → `onStoreDocument` → materializer → `/internal/yjs-webhook` → persisted `bodyJsonb` (version 3); two Yjs tickets, zero console errors; page deleted afterwards.
- [x] Dependabot archived-104 closed as superseded.

Gap noted: the E2E notes/documents specs don't exercise sync (they pass even when the sidecar can't reach the app) — a follow-up spec asserting the webhook-persisted body would make this guard permanent. CI: none yet (archived-79).

</details>

---

## archived-158 — Mantine >=9.4 Textarea autosize triggers Chromium ResizeObserver-loop error on width change (E2E allowlisted)

`OPEN` · nathanpond · opened 2026-08-31

Labels: `bug`, `sev:low`, `area:spa`, `dependencies`

## What

Since Mantine 9.4.0, `Textarea autosize` uses Mantine's own `TextareaAutosize` (`@mantine/core/esm/components/Textarea/Autosize.mjs`) instead of `react-textarea-autosize`. Its `ResizeObserver` observes the textarea and, when the textarea's **width** changes, calls `resizeTextarea()` synchronously inside the observer callback, which writes `height` on the very element being observed. Chromium reports that as

```
ResizeObserver loop completed with undelivered notifications
```

dispatched as a window `error` event (Playwright: `pageerror`). Layout settles on the next frame, so there is no visible defect — but any E2E test that changes an autosize textarea's width (chatbot sidebar resize, viewport resize, panel collapse) would trip `ConsoleErrorGuard`.

## Where

- Trigger in this app: `src/AutoNate.Spa/src/agent/AgentSidebar.tsx` — the `Resize chatbot` handle (`:373`) changes the width of the composer `<Textarea autosize>` (`:494`). Other autosize textareas (`DocumentChatPanel`, `CommentsPanel`, `QueryPage`, admin forms) are exposed the same way on viewport resize.
- Upstream: `packages/@mantine/core/src/components/Textarea/Autosize.tsx` (added in 9.4.0; unchanged in 9.5.2 apart from `rows: minRows`).

## Evidence

Bisect on `Assistant_CrossPageSearchResizePersistenceAndDelete` with only `@mantine/*` varied (all other archived-157 updates applied): 9.1.1 pass, 9.2.0 pass, 9.3.0 pass, **9.4.0 fail**, 9.5.2 fail. `npm view @mantine/core@9.3.2 dependencies.react-textarea-autosize` is set; `@9.4.0` is not.

## Mitigation (landed with archived-157)

`tests/AutoNate.E2E.Tests/Support/ConsoleErrorGuard.cs` — `DefaultAllowed` gains the substring `ResizeObserver loop completed with undelivered notifications` with a rationale comment. This is browser-level notice text, not a JS exception, and matches the class the default allowlist exists for.

## Fix approach

Upstream: defer the height write in the observer callback (`requestAnimationFrame`, as Mantine's own `ScrollArea/use-resize-observer` already does) or observe a wrapper instead of the textarea. Once a Mantine release includes that, remove the allowlist entry and re-run `Assistant_CrossPageSearchResizePersistenceAndDelete` to confirm.

<!-- fingerprint: upstream-bug|@mantine/core/components/Textarea/Autosize|TextareaAutosize -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

## Plan

The defect is in Mantine, not in AutoNate, so the fix has to land upstream; the allowlist entry in `ConsoleErrorGuard` stays until a Mantine release carries it.

1. Confirm upstream state: `mantinedev/mantine` `master` still writes `height` synchronously inside the `ResizeObserver` callback in `packages/@mantine/core/src/components/Textarea/Autosize.tsx`; no open issue/PR covers Textarea (the closest is Popover #9121/#9126, fixed 2026-08-16).
2. Prove the fix locally: patch the installed `Autosize.mjs` to defer `resizeTextarea()` to `requestAnimationFrame` (cancelled on cleanup), disable the allowlist entry, and re-run `Assistant_CrossPageSearchResizePersistenceAndDelete` — must pass where it deterministically failed before.
3. File the upstream issue with the minimal repro + bisect (9.3.2 pass / 9.4.0 fail) and open the upstream PR with the one-hook change; link both here.
4. When a Mantine release includes the fix: bump `@mantine/*`, remove the allowlist entry, re-run the spec, close this issue.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Upstream filed:
- Issue: https://github.com/mantinedev/mantine/issues/9161
- PR: https://github.com/mantinedev/mantine/pull/9162 (one-hook change in `Autosize.tsx`: defer the height write to `requestAnimationFrame`; verified locally — with the patched build and the allowlist entry disabled, `Assistant_CrossPageSearchResizePersistenceAndDelete` passes where stock 9.4.0–9.5.2 fails deterministically)

Remaining on our side: when a Mantine release includes the fix, bump `@mantine/*`, delete the `ResizeObserver loop completed with undelivered notifications` entry from `ConsoleErrorGuard.DefaultAllowed`, re-run that spec, close this issue.

</details>

---

## archived-161 — Executor Python sandbox: Pyodide's `js` module exposes the Node host (process, eval, fetch, NODEFS) to author code

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `security`, `sev:critical`, `area:services`

## What
The executor's Python sandbox is not a sandbox on Node. Pyodide registers a `js` module bound to the host's `globalThis` (`loadPyodide` default `jsglobals`), so any author who can save a Python transformer/analyzer gets the sidecar's Node process: `import js; js.process.env`, `js.process.cwd()`, `js.eval(...)`, `js.fetch(...)`, `js.process.binding('fs')`, and `pyodide_js.FS.mount(NODEFS, {root: '/'}, ...)` for the container filesystem. `pythonRunner.ts:4-5` describes the runtime as "browser-grade — no `os`, no `subprocess`, no host fs", which is only true in a browser.

## Where
`services/executor/src/pythonRunner.ts:16-19` (`loadPyodide` without `jsglobals`), `:71-73` (author source executed in that interpreter)

## Why it matters
Path: `POST /api/code-transformers` (language=python) → pipeline run → NATS `pipeline-code-run.>` → executor. Whatever secrets and network reach the executor container has (NATS URL and credentials once archived-66 lands, any mounted config, the compose network) are readable and usable by pipeline authors; `js.fetch` turns the sidecar into an SSRF pivot inside the compose network. The JS path (isolated-vm) does not have this problem.

## Evidence
Probe on the installed `pyodide@0.26.4` (2026-08-31):
```
escape default: import js; str(js.process.platform) + ' cwd=' + str(js.process.cwd())
  -> darwin cwd=/Users/npond/RiderProjects/AutoNate/services/executor
restricted (jsglobals: Object.create(null)): js.process / js.globalThis / js.eval -> AttributeError
```

## Suggested fix
Load Pyodide with `jsglobals: Object.create(null)`, unregister the `pyodide_js` module (and drop it from `sys.modules`) after load so `pyodide_js.FS` / `loadPackage` are unreachable, and run each request in its own `worker_threads` Worker (the archived-58 design) so nothing leaks between authors. Regression tests: `import js; js.process`, `import pyodide_js`, `open('/etc/passwd')`, and `pyodide.code.run_js` must all fail from author code.

_Found while working archived-58 on 2026-08-31. Severity per the n8SDLC rubric: `sev:critical`._

<!-- fingerprint: sandbox-host-escape|services/executor/src/pythonRunner.ts|loadPyodide-jsglobals -->

---

## archived-163 — SubscriptionManagerTests.Disconnect_ClearsRegistryIndices fails on every full-suite run (5 s WebSocket budget), passes in isolation

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:tests`

## What
`SubscriptionManagerTests.Disconnect_ClearsRegistryIndices` fails every time the **full** `AutoNate.Web.Tests` suite runs, and passes every time it runs in isolation. `TestTimeout()` (`SubscriptionManagerTests.cs:296-297`) is a fixed 5-second `CancellationTokenSource` used for every WebSocket receive in the class; under full-suite parallel load the subscribe ack does not arrive inside that budget and `TestWebSocket.ReceiveAsync` throws `TaskCanceledException`.

## Where
`tests/AutoNate.Web.Tests/SubscriptionManagerTests.cs:296-297` (the shared 5 s budget), consumed at `:231`, `:251`, `:266` and the other `TestTimeout()` call sites.

## Why it matters
The suite cannot be used as a merge gate while one test fails on every full run: a real regression appearing in that file would be indistinguishable from the standing failure, and "1 failed" trains everyone to ignore the result. It also blocks archived-79 (no CI) — wiring this suite into CI as-is would produce a permanently red pipeline.

## Evidence
Four full-suite runs on this machine, same checkout:

| ref | result |
|---|---|
| `master` @ `f28a1c85` | 1352 passed / **1 failed** — `Disconnect_ClearsRegistryIndices` (16 s) |
| `fix/59-authz-fail-closed` (run 1) | 1368 passed / **1 failed** — same test (15 s) |
| `fix/59-authz-fail-closed` (run 2) | 1368 passed / **1 failed** — same test (15 s) |
| same test, `--filter FullyQualifiedName~SubscriptionManagerTests`, ×3 | 10 passed / 0 failed each time (~29 s per run) |

```
System.Threading.Tasks.TaskCanceledException : A task was canceled.
   at Microsoft.AspNetCore.TestHost.TestWebSocket.ReceiverSenderBuffer.ReceiveAsync(CancellationToken)
   at AutoNate.Web.Tests.SubscriptionManagerTests.ReceiveJsonAsync(WebSocket, CancellationToken)
   at AutoNate.Web.Tests.SubscriptionManagerTests.SubscribeAsync(...)   :251
   at AutoNate.Web.Tests.SubscriptionManagerTests.Disconnect_ClearsRegistryIndices()  :266
```
The recorded duration (15–16 s) is three times the 5 s budget, i.e. the test spends its whole budget waiting rather than failing an assertion. Confirmed pre-existing: it reproduces on `master` with no branch changes applied.

## Suggested fix
Scale the receive budget for full-suite conditions rather than raising it blindly — e.g. drive `TestTimeout()` from an environment-aware value (a few seconds locally, 30 s+ under CI/parallel load), or make the subscribe handshake awaited deterministically instead of on a wall-clock race. Whatever the shape, the check is the same: the full suite must pass twice in a row with no `--filter`.

Worth a sweep of the other wall-clock budgets in the suite at the same time — `DataStoreListFilteringTests` and `ApiNotFoundGuardTests` failed in a fresh-worktree run for a different (missing `wwwroot`) reason, so a fresh checkout needs `BuildSpa` before the suite is meaningful; that is worth documenting in `docs/codebase/Testing.md` alongside this fix.

_Found while verifying archived-59 on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: flaky-test-wall-clock-budget|tests/AutoNate.Web.Tests/SubscriptionManagerTests.cs|TestTimeout -->

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Correcting this issue's own evidence.** A fourth full-suite run came back **1422 passed / 0 failed**, with `Disconnect_ClearsRegistryIndices` among the passes. So "fails every time the full suite runs" is wrong — it is **intermittent under load**, not deterministic.

Updated tally, all on the same machine and checkout:

| run | result |
|---|---|
| `master` @ `f28a1c85` | 1 failed — `Disconnect_ClearsRegistryIndices` (16 s) |
| `fix/59-authz-fail-closed` run 1 | 1 failed — same test (15 s) |
| `fix/59-authz-fail-closed` run 2 | 1 failed — same test (15 s) |
| `fix/60-61-outbound-url-guards` run 1 | 2 failed — same test (15 s) **+ `Query.NotesQueryEndpointTests.FromNotes_GroupByType_Counts_PerKind`** (12 s, `Expected: OK, Actual: BadRequest`) |
| `fix/60-61-outbound-url-guards` run 2 | **0 failed** |
| each failing test, run with `--filter` in isolation | passes every time (2–3 attempts each) |

Two consequences for the fix:

1. **It is a second test, not just one.** `NotesQueryEndpointTests.FromNotes_GroupByType_Counts_PerKind` fails the same way — only under full-suite load, passing 2/2 in isolation — but its symptom is a `400` from the query endpoint rather than a timeout, so the shared cause may be contention in the fixture/DB layer rather than the 5 s WebSocket budget alone. Whoever takes this should treat "wall-clock budgets in `SubscriptionManagerTests`" as the confirmed instance and go looking for the general one.
2. **The acceptance bar stated below still holds and is now the important part**: the full suite must pass twice in a row with no `--filter`. A single green run does not clear this — run 2 above was green while runs 1–4 were not.

This matters for archived-79 (CI): at roughly a 60% clean rate per full run, a pipeline wired to this suite as-is would go red on most commits for reasons unrelated to the change under test.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Related environmental trap, found while clearing the security backlog** — worth folding into this issue because it produces the same symptom (a full-suite run that looks like a regression and isn't).

`AutoNate.E2E.Tests`' fixture empties `src/AutoNate.Web/wwwroot`. Two `AutoNate.Web.Tests` need the built SPA to be there:

- `ApiNotFoundGuardTests.Unknown_api_path_returns_404_not_spa_index` — expects `Cache-Control: no-store`, gets `null`
- `DataStoreListFilteringTests.GetDataStores_filters_by_per_store_view_grants` — gets `404`

So **running E2E and then Web.Tests fails those two**, and unlike archived-163's flake they also fail in isolation, which makes them look like real breakage. `dotnet build src/AutoNate.Web -p:BuildSpa=true` restores them; verified both directions in this session.

This also explains a false lead from earlier: a `master` baseline run in a fresh `git worktree` failed exactly these two, because a new worktree has no SPA build either — I initially read that as "master is broken".

Two practical consequences:

1. **Order matters locally**: run `AutoNate.Web.Tests` before `AutoNate.E2E.Tests`, or rebuild the SPA in between.
2. **For archived-79 (CI)**: the pipeline must either build the SPA before the backend suite, run the two suites in separate jobs/checkouts, or rebuild between them — otherwise CI reproduces this as a spurious two-test failure on every run that does E2E first.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Another way this suite misleads, found while clearing the bug backlog.** Related to the `wwwroot` note above, but with a nastier failure signature.

Running `dotnet test --filter …` to "check in isolation" **immediately after the E2E suite** reuses binaries whose static-web-asset manifests E2E just deleted. The test host then fails to stand up cleanly and unrelated tests fail in ways that look like a real regression — I saw `NotesQueryEndpointTests` report `Expected: OK / Actual: BadRequest` and reproduce four times running, which is exactly the profile of a genuine break rather than a flake.

It was not. After a plain rebuild the same tests pass 8/8 on the same branch, `master` passed throughout, and the full suite came back 1461/0.

So the isolation check — the thing I lean on to tell a real failure from a load flake — is itself unreliable in that window. **Rebuild first**: `dotnet build src/AutoNate.Web -p:BuildSpa=true` plus the test project, *then* re-run the filter.

For archived-79 (CI): this is another reason the two suites want separate jobs or checkouts rather than running back-to-back in one workspace.

</details>

---

## archived-165 — REST data connector config never binds: handler deserializes case-sensitively, SPA writes camelCase

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-08-31

Labels: `bug`, `sev:medium`, `area:api`

## What
`RestDataConnectorHandler.ParseConfig` calls `JsonSerializer.Deserialize<RestConnectorConfig>(connector.ConfigJson)` with no options, so binding is **case-sensitive**. The SPA writes and documents the config in **camelCase**, so `Url` never binds and every REST connector authored through the UI fails with `REST connector config is missing Url.`

## Where
`src/AutoNate.Web/Services/DataConnectors/Builtin/RestDataConnectorHandler.cs:94` (`ParseConfig`), against the shape produced at `src/AutoNate.Spa/src/pages/admin/dataconnectors/DataConnectorsPage.tsx:54,75,85` (`'{"url": "", "authMode": "none"}'`) and described to the user at `:360` (`REST: { url, authMode, token?, username?, password?, apiKeyHeader?, apiKey?, rowsPath? }`).

## Why it matters
The built-in REST connector — the only non-plugin connector kind — cannot be configured through its own admin UI. "Test" and "preview" both fail with a message that points at the operator's config rather than at the binding mismatch, so it reads as user error. It works only if the operator ignores the placeholder and hand-types PascalCase into the free-form JSON textarea.

## Evidence
`System.Text.Json` with default options (`PropertyNameCaseInsensitive = false`), same shape as the handler:
```
camelCase  -> Url=''
PascalCase -> Url='https://api.example.com/rows'
```
Reproduced directly against `RestDataConnectorHandler` in a test: a connector whose `ConfigJson` is `{"url":"http://…","authMode":"none"}` throws `REST connector config is missing Url.` before any request is attempted.

## Suggested fix
Deserialize with `PropertyNameCaseInsensitive = true` (or a shared camelCase `JsonSerializerOptions`) in `ParseConfig`, matching what the SPA writes. Regression test: a camelCase config round-trips to a populated `RestConnectorConfig`, and a preview against a stub endpoint returns rows.

**Note for whoever takes this:** the mis-binding currently *masks* the SSRF fixed in archived-60 for UI-authored connectors — they never reach the fetch. archived-60's guard is in place first precisely so that fixing this binding does not turn a dead code path into a live SSRF; keep them in that order.

_Found while working archived-60 on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: json-binding-case-mismatch|src/AutoNate.Web/Services/DataConnectors/Builtin/RestDataConnectorHandler.cs|ParseConfig -->

---

## archived-172 — Site Configuration → Appearance: saving Site name silently reverts on reload

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:api`

## What
Saving a new **Site name** in Site Configuration → Appearance appears to succeed, but reloading the page restores the previous/default value. The edit is lost.

## Where
Reproduced by `AdminOperationsTests.Appearance_SiteNamePersistsAcrossReload`, currently `[Fact(Skip)]` in `tests/AutoNate.E2E.Tests/AdminOperationsTests.cs`.

## Why it matters
Data-loss-shaped from the operator's point of view: the UI reports a successful save and the value silently reverts, so the only way to notice is to reload and re-read. The site name is also the fallback document title (archived-18), so it is visible everywhere.

## Evidence
The E2E spec was written against the expected behaviour and skipped rather than deleted, with the prose reason: *"appearance Save changes accepts edits, but reloading restores the default Site name instead of the saved value."* Un-skip it to reproduce.

## Suggested fix
Trace the Appearance save path end to end — whether the PUT persists, whether the read-back projects the saved row, and whether `SiteAppearanceProvider`'s cached query is being served stale after the mutation. The skipped spec is the acceptance test; it should go green rather than be replaced.

_Filed while working archived-88 (skipped tests with no linked issue) on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: silent-save-revert|src/AutoNate.Web/Endpoints/SiteSettingsEndpoints.cs|appearance-site-name -->

---

## archived-173 — DOCX import navigates to ?import=1 but never finalises parsed content — document reloads empty

`CLOSED` · nathanpond · opened 2026-08-31 · closed 2026-09-01

Labels: `bug`, `sev:medium`, `area:spa`

## What
DOCX import into a project document navigates to `?import=1`, but the editor wrapper never finalises the parsed content — the document reloads with an empty body.

## Where
Reproduced by `DocumentEditorTests.ProjectDocuments_ImportDocx_CommitsImportedContent`, currently `[Fact(Skip)]` in `tests/AutoNate.E2E.Tests/DocumentEditorTests.cs`.

## Why it matters
Import is the migration path into Documents; silently producing an empty document loses the user's file with no error surfaced.

## Evidence
The spec was written against the expected behaviour and skipped with the prose reason: *"DOCX import upload navigates to ?import=1, but the editor wrapper never finalizes parsed content; editor-core-generated DOCX fixtures reload with an empty body."* Un-skip it to reproduce.

## Suggested fix
Follow the `?import=1` handoff: whether the parsed content reaches the Y.Doc, and whether it is committed before the editor mounts/persists. The skipped spec is the acceptance test.

_Filed while working archived-88 (skipped tests with no linked issue) on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: import-never-finalises|src/AutoNate.Spa/src/components/documents/DocxDocumentEditor.tsx|import-handoff -->

---

## archived-182 — Role-assignment revoke is gated kind-level: a one-role assign grant can strip any role from anyone

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-01

Labels: `bug`, `security`, `sev:high`, `area:api`

## What
`DELETE /api/admin/role-assignments/{id}` is gated with `RequireKindPermission(Role, Assign)`. `AuthorizeKindLevelAsync` only asks "does *any* allow grant for role+assign exist?" — it never resolves the assignment's `RoleId`, so it cannot compare it against the grant's selector.

A caller whose assign grant names a single throwaway role can therefore revoke **anybody's** membership of **any** role, including SuperAdmin.

## Where
`src/AutoNate.Web/Endpoints/RoleAssignmentEndpoints.cs:29`

## Why it matters
Assign is gated instance-level on the POST side (`/api/admin/roles/{id}/assignments`), so the two halves of the same privilege disagree: a grant narrow enough to hand out one role is wide enough to strip every role in the system. Locking every administrator out of their own instance needs one narrow grant.

## Evidence
`PrivilegeMutationEndpointTests.RevokeAssignment_WithGrantScopedToADifferentRole_StillRevokesAnotherRolesAssignment` asserts the current behaviour and passes: the actor holds `assign` scoped to role A only, and successfully deletes an assignment of role B.

## Suggested fix
Resolve the assignment first and gate instance-level on its `RoleId`, mirroring the POST. The test above then inverts to assert 403 and the assignment surviving.

_Found while writing the coverage for archived-91 on 2026-08-31. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: kind-level-gate-on-instance-op|src/AutoNate.Web/Endpoints/RoleAssignmentEndpoints.cs|revoke-assignment -->

---

## archived-183 — preview-file-source reads any datastore file with no DataStore authorization

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-01

Labels: `bug`, `security`, `sev:high`, `area:api`

## What
`POST /api/datasets/preview-file-source` is gated only on `RequireKindPermission(Dataset, Create)`. It then reads an arbitrary file out of an arbitrary Files datastore — named by `dataStoreId` + `scopePath` in the request body — with **no DataStore authorization of any kind**.

## Where
`src/AutoNate.Web/Endpoints/DatasetEndpoints.cs` (the `preview-file-source` handler)

## Why it matters
A caller holding `dataset:create` and **zero** datastore grants — one who gets an empty list from `GET /api/datastores` and 403 from every `/api/datastores/{id}/…` route — can still name any store's id and path and read back that file's column names, and through type inference the shape of its values. The datastore permission model is bypassed by a route that belongs to a different feature.

## Evidence
`DatasetPreviewFileSourceTests.PreviewFileSource_WithDatasetCreateGrantOnly_ReturnsColumnsWithoutDataStoreGrant` asserts the current behaviour and passes: the actor holds only `dataset:create`, holds no datastore grant at all, and receives the file's inferred schema.

## Suggested fix
Authorize `(DataStore, View)` against the referenced `dataStoreId` inside the handler before touching the file — the same check `GET /api/datastores/{id}/files` makes. Now that a `DataStoreInstanceAuthorizer` exists (see the instance-authorizer fix), `IAuthorizer.AuthorizeAsync` answers this directly.

_Found while writing the coverage for archived-82 on 2026-08-31. Severity per the n8SDLC rubric: `sev:high`._

<!-- fingerprint: missing-cross-feature-authz|src/AutoNate.Web/Endpoints/DatasetEndpoints.cs|preview-file-source -->

---

## archived-184 — preview-file-source 500s and leaks a stack trace on a folder .keep placeholder

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-01

Labels: `bug`, `security`, `sev:medium`, `area:api`

## What
`POST /api/datasets/preview-file-source` with `scopeKind: "file"` and a `scopePath` pointing at a folder's `.keep` placeholder throws, and the response is a 500 carrying the exception text and stack trace.

`CreateFolderAsync` inserts a `.keep` row with `StorageKey = ""`. The handler calls `fileService.DownloadAsync` **before** dispatching to a parser, and `FileDataStoreService.ResolveAbsolutePath("")` resolves to the datastores root *directory*, so `File.OpenRead` throws. The folder branch filters `.keep` out; the file branch does not.

## Where
`src/AutoNate.Web/Endpoints/DatasetEndpoints.cs` (preview-file-source, file scope) · `FileDataStoreService.ResolveAbsolutePath`

## Why it matters
Reachable by anyone holding `dataset:create`. There is no exception-handling middleware in `Program.cs`, so in Development the DeveloperExceptionPage returns the exception and stack to the caller — internal paths and type names included. A clean 400/404 is owed here; the placeholder is an implementation detail of folder creation, not a file a caller can preview.

## Evidence
`DatasetPreviewFileSourceTests.PreviewFileSource_FolderPlaceholderKeepFile_Returns500` asserts the current 500 with a FINDING comment, and passes.

## Suggested fix
Filter `.keep` in the file branch the way the folder branch already does, and return 404 for a path with no readable content. Separately worth deciding whether the app should install exception-handling middleware so an unhandled throw anywhere cannot return internals.

_Found while writing the coverage for archived-82 on 2026-08-31. Severity per the n8SDLC rubric: `sev:medium`._

<!-- fingerprint: unhandled-exception-leaks-stack|src/AutoNate.Web/Endpoints/DatasetEndpoints.cs|preview-keep-placeholder -->

---

## archived-185 — Yjs ticket endpoint is an existence oracle for notes and documents (403 vs 404)

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-01

Labels: `bug`, `security`, `sev:low`, `area:api`

## What
`POST /api/yjs/ticket` answers 404 for a note or document that does not exist and 403 for one that exists but the caller cannot see. The note branch and the `documents:` branch resolve the row **before** calling `IContentAuthorizer`.

## Where
`src/AutoNate.Web/Endpoints/YjsEndpoints.cs:77-99`

## Why it matters
Any authenticated user with zero grants can distinguish "this note/document id exists" from "it does not", one GUID at a time. Not a disclosure of content, but it confirms identifiers harvested elsewhere and maps what a tenant holds.

The `page:` / `pagemeta:` branch in the same handler is already correct — it authorizes first and answers 403 either way — so the fix is to make the other branches match their sibling.

## Evidence
`YjsEndpointTests.Ticket_WithoutAnyGrant_StillDistinguishesExistingNotesFromMissingOnes` asserts the current behaviour (403 for a real note, 404 for a random GUID) and contrasts it with the correct page branch in the same test. It passes today.

## Suggested fix
Move the existence lookups behind the authorize call, or collapse both outcomes onto 403. Then invert the test.

_Found while writing the coverage for archived-80 on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: existence-oracle|src/AutoNate.Web/Endpoints/YjsEndpoints.cs|ticket-note-document-branch -->

---

## archived-186 — Four small correctness/hardening findings from the endpoint-coverage pass

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-01

Labels: `bug`, `sev:low`, `area:api`

Four smaller findings from the same coverage pass. Grouped because each is a few lines and none is independently worth a milestone slot.

## 1. Content permission overrides can target a **role**
`ContentPermissionOverrideEndpoints` passes `request.PrincipalKind` straight through, and `EfCorePermissionGrantStore.AllowedPrincipalKinds` includes `Role` alongside `User`/`Group`. A self-service folder or document editor can therefore attach a resource grant to a role — including SuperAdmin — even though the endpoint's own header comment describes user/group sharing. The store's rejection message reads `"principalKind must be 'user' or 'group'."` while the set it enforces also contains `role`, so the error text is stale.
**Fix:** restrict the override endpoints to user/group, and correct the message.

## 2. Neither privilege store validates that the principal exists
`EfCoreRoleAssignmentStore.AssignAsync` and `EfCorePermissionGrantStore.CreateAsync` check the principal *kind* and stop. Privilege can be pre-seeded against a user id that does not exist yet and activates the moment that id is created. Pinned by `AssignRole_ToAPrincipalThatDoesNotExist_StillWritesTheAssignment` and `CreateOverride_ForAPrincipalThatDoesNotExist_StillWritesTheGrant`.

## 3. Comment creation has a TOCTOU that surfaces as a 500
`ContentDocumentCommentEndpoints` create and reply do a `SELECT … AnyAsync` collision check on `(document_id, number)` then insert, with no `catch (DbUpdateException)` around `SaveChangesAsync`. The unique index is real, so the concurrent case the code's own comment calls out ("a real-world race we accept") returns an unhandled 500 rather than the 409 the handler promises.

## 4. `refresh-all` returns two different response shapes
The zero-binding branch returns `DocumentBindingListResponse` (`{items}`); every other path returns `RefreshAllResponse` (`{items, failures}`). SPA code reading `res.failures.length` gets `undefined` for a document with no bindings.

Also noted, not filed: the shared-secret filters compare lengths before `FixedTimeEquals`, so secret *length* remains a branch oracle — inherent to that construction, fixable only by hashing both sides to a fixed width first.

_Found while writing the coverage for archived-90 and archived-91 on 2026-08-31. Severity per the n8SDLC rubric: `sev:low`._

<!-- fingerprint: coverage-pass-minor-findings|src/AutoNate.Web/Endpoints|role-principal-override-toctou-shape -->

---

## archived-193 — Seeded admin account ships its password hash and salt in the repo, and is auto-granted SuperAdmin

`CLOSED` · nathanpond · opened 2026-09-01 · closed 2026-09-02

Labels: `security`, `sev:critical`, `area:api`

## What

`infra/postgres/init/02-create-autonate-app-schema.sql` seeds a `local_users` row for `admin` with its `password_hash` **and** `password_salt` written into the file. The plaintext is `admin`.

```sql
INSERT INTO local_users (username, password_hash, password_salt, ...)
VALUES ('admin', 'ItdHztyrstpGA82U3e+0MtFcTVZq5N1jW5YvNtRvMTw=',
        '041Gg5Nyee8Xo8ge595Jyw==', ...)
```

Both halves of a PBKDF2 verification are present, so this is not a hash an attacker has to crack — `PasswordHasher.VerifyPassword("admin", hash, salt)` returns true, and the credential is simply readable in the repository.

## Where

- `infra/postgres/init/02-create-autonate-app-schema.sql` (the INSERT)
- `src/AutoNate.Web/appsettings.json` — `Authorization:AssignSuperAdminToAllExistingUsers: true`

## Why it matters

The INSERT is ungated by environment: it runs in the compose bootstrap, and `AutoNateE2EFixture` replays the same script. Nothing scopes it to development.

`AssignSuperAdminToAllExistingUsers` then defaults **true**, and its one-shot backfill grants the SuperAdmin role to every row in `local_users` on first boot. So any deployment that ran the init script came up with a **super-admin account whose password is public**, reachable from the login form by anyone who can see the page.

Making this repository public would publish the credential to everyone, but the finding does not depend on that: the account exists with the same password on every install that took the default.

Its twin is that removing the seed alone makes the product unusable — a clean production database has **no way to sign in at all**. There is no registration page, no setup wizard, no `create-admin` command, and `POST /api/users` requires an authenticated caller. The seed was load-bearing, which is presumably why it shipped ungated.

## Suggested fix

Delete the INSERT and create the first administrator at startup instead: when `local_users` is empty and both a username and a password are supplied via configuration, create that one account and grant it SuperAdmin directly. When they are not supplied, create nothing and log an actionable message. No default password ships, and because the bootstrap account grants itself SuperAdmin, `AssignSuperAdminToAllExistingUsers` stops being load-bearing and can default false — it promotes the *entire* existing user table when enabled, which is a migration aid, not first-run setup.

Test credentials move into test code: the many suites that talk to the database with no host get the row from `PostgresTestDatabase`, hashed at runtime rather than stored.

Operators upgrading from an affected version must change the `admin` password — the bootstrap deliberately does not touch a non-empty `local_users`, so it cannot fix them.

_Found while preparing the 0.1 public release on 2026-09-01. Severity per the n8SDLC rubric: `sev:critical` — a known-password super-admin on every default install._

<!-- fingerprint: committed-credential|infra/postgres/init/02-create-autonate-app-schema.sql|local_users-admin-seed -->

---

