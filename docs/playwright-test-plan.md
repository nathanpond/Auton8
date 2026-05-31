# Playwright E2E Test Plan

## Current Coverage

AutoNate's browser suite lives in `tests/AutoNate.E2E.Tests/` and uses
Microsoft.Playwright from xUnit. It already has strong foundation coverage:

- Authentication, logout, protected-route redirects, shell navigation, and reload persistence.
- Records CRUD, record types, relationship types, comments, filters, watching, and history.
- Workflow studio smoke coverage plus execution start, detail, cancellation, and deletion.
- Forms creation, publishing, and authenticated runtime rendering.
- Documents project-root CRUD, editor mount, and template gallery smoke coverage.
- Admin configuration routing, users, roles, groups, permissions, agent sidebar affordances,
  notifications, profile, diagnostics, and selected permission gates.

## Exploration Notes

The local infrastructure stack was already healthy and a Vite server was listening on
`http://localhost:5173`. Repository routes and UI source were inspected alongside the
existing E2E suite.

`playwright-cli` launch was attempted first as requested. The CLI daemon initially could
not create `~/Library/Caches/ms-playwright/daemon` in the workspace sandbox, then launched
with elevated sandbox permission. The standalone Vite shell rendered the login form, while
request inspection showed `502` responses for `/api/auth/me` and `/api/appearance` because
the dev backend was not listening on port `5108`. Authenticated exploration and verification
therefore continued through the fixture-hosted .NET Playwright suite.

## High-Value Gaps

### Priority 1: Notes hierarchy behavior

Before this batch, the notes suite verified that `/notes` and `/projects` mount, but it did
not exercise the primary content-creation journey:

1. Create a project from `/projects`.
2. Land in the new project's `/notes/{locator}` workspace.
3. Create a cabinet.
4. Create a notebook.
5. Create a page and verify that it becomes active.

This is the first implementation batch because it crosses several real API mutations and
state transitions while avoiding brittle automation of the Yjs-backed rich-text editor.
The test is now implemented in `NotesTests.cs`.

### Priority 2: Notes workspace interactions

- Collapse and restore the notes sidebar, including persistence across reload.
- Search within a cabinet after creating multiple notebooks/pages.
- Create a note tab and verify the requested editor kind mounts.
- Rename, archive, and delete hierarchy items through their menus.
- Deep-link to a created page and verify ancestor expansion after reload.

### Priority 3: Documents behavior beyond mount checks

- Navigate into a created folder and verify deep-link restoration.
- Create a template and verify it appears only in the template gallery.
- Exercise folder rename and deletion confirmation.

### Priority 4: Admin persistence and permission gates

- Change a general site setting, save, reload, and verify persistence.
- Prove limited users cannot open admin configuration routes.
- Prove record-level delete affordances are hidden without a grant and appear after one.

### Priority 5: Workflow task completion

- Seed a user-task workflow, open its task form, and complete the task.
- Verify the execution transitions from running to completed.

### Priority 6: Query and dashboard stateful flows

- Execute and save an AQL query, reload `/query`, and select the persisted query.
- Mount the dashboard template for the fixture, create a dashboard, rename it, and delete it.
- Keep query validation errors and dashboard widget configuration as low-priority follow-ons.

## Locator Strategy

Prefer semantic locators already exposed by the app:

- `GetByRole` for buttons, links, headings, tabs, and dialogs.
- `GetByPlaceholder` for the custom notes modals, whose visible labels are not associated
  with their inputs.
- `GetByText` for newly-created hierarchy names.

No `data-testid` additions are required for the first batch.

## First Batch

Add a notes hierarchy test that creates a project, cabinet, notebook, and page entirely
through the UI and verifies each state transition. Keep the existing smoke tests as fast
mount checks.

## Verification

Run:

```bash
npm ci
npx playwright install --with-deps
npm run lint --if-present
npm test --if-present
npx playwright test
dotnet test tests/AutoNate.E2E.Tests
```

The final `dotnet test` command is the repository's actual Playwright E2E runner. The
requested `npx playwright test` command is still run and reported even though this repo has
no Node Playwright config.

## Verification Outcome

- `npm ci`: passed.
- `npx playwright install --with-deps`: passed with elevated sandbox permission.
- `make e2e-install`: passed; installed the Chromium revision required by .NET Playwright.
- `npm run lint --if-present`: passed (no root lint script).
- `npm test --if-present`: passed (no root test script).
- `npx playwright test`: ran and reported `Error: No tests found`, because the executable
  suite is the .NET project.
- `dotnet build tests/AutoNate.E2E.Tests --no-restore`: passed with a sandbox-related
  NuGet vulnerability-cache warning.
- `npm run type-check` in `src/AutoNate.Spa`: passed.
- Targeted .NET Notes + Documents run: passed, 15 tests with 0 failures.
- Targeted .NET Notes, AdminConfig, Query, and Dashboard specs: passed.
- Targeted .NET WorkflowExecution, Documents, Query, Dashboard, and ManageUsers low-priority specs: passed.
- Expanded targeted .NET batch: passed, 30 tests with 0 failures and 2 skipped blocked
  reproducers.
- Full .NET Playwright suite: passed, 108 tests with 0 failures and 2 skipped blocked
  reproducers.

## Expanded Coverage Pass

The completed initial backlog established broad smoke and core-CRUD coverage. A second
static audit of routes and feature modules identified additional browser workflows where
the suite still stops at mount checks or API-seeded shortcuts. The expanded backlog in
`docs/playwright-test-backlog.md` records every audited gap as `E2E-028` through `E2E-068`.

Implementation order:

1. Document authoring, import/export, preview, bindings, permissions, and review.
2. Workflow Studio browser-authored lifecycle and specialized BPMN editors.
3. Pages / Menus dynamic routes plus UI-driven IAM and permission gates.
4. Notes advanced organization, sharing, history, and editor variants.
5. Assistant, forms runtime/editor, workflow execution administration, and records.
6. Operational admin pages, notifications, and the remaining manage-users lifecycle.

## Expanded Pass Outcome

Every audited high- and medium-priority item in `docs/playwright-test-backlog.md` is now
either `DONE` or `BLOCKED`. Blocked entries name the missing deterministic fixture hook,
selector, or product behavior needed before their browser flows can be covered reliably.
