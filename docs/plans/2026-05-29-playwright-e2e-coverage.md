# Plan: Comprehensive Playwright E2E Suite for AutoNate

> On approval, move this file to `./docs/plans/2026-05-29-playwright-e2e-coverage.md`
> (project convention — plans live in-repo, not `~/.claude/plans/`).

## Context

The app has a large, growing user surface (records, record types, workflows/executions,
documents, notes, forms, admin config, agent sidebar, notifications, permissions) but the
E2E suite is only **3 files / ~6 tests**: `LoginTests`, `AgentSidebarTests`,
`WorkflowOverrideTests` — all render/smoke checks. There is no behavioral coverage of the
core CRUD journeys a user actually performs.

The goal is a **comprehensive, repeatable Playwright suite** (`tests/AutoNate.E2E.Tests/`,
.NET Microsoft.Playwright) the user can run on demand to confirm the app works end-to-end.

**Decisions locked in (from clarifying questions):**
1. **Dedicated ephemeral test DB** — fixture creates/seeds a separate `AutoNate_E2E`
   database each run. Enables destructive flows + determinism.
2. **Smoke + API-backed** for the heavy editors (BPMN Workflow Studio, BlockNote notes,
   docx editor) — drive real behavior through the API, keep UI checks to render/mount.
3. **Include focused permission-gating UI tests** — create a limited user at runtime, sign
   in as them, assert gated affordances differ from admin.
4. **Local-runnable + a `make e2e` target** (browser install + infra check + `dotnet test`).
   No CI pipeline integration yet.

### Key facts established during research
- Existing fixture (`AutoNateE2EFixture.cs`) boots `AutoNate.Web` via `dotnet run
  -p:BuildSpa=true` on a random port, parses the "Now listening on" line, owns one Chromium
  browser, and is wired as a **collection** fixture (one app boot shared by all classes —
  intentional, to avoid racing SPA builds). Sign-in helpers: `SignInAsAdminAsync`,
  `SignInAsync(page, user, pass)`.
- Auth in the booted app is **fully enforced** (`appsettings.Development.json`:
  `Authorization.Enabled=true`, `Enforcement=full`, `AssignSuperAdminToAllExistingUsers=true`).
- **Only `admin`/`admin` is seeded.** `user1` does NOT exist anywhere (the auto-memory note
  is stale — correct it during implementation). A limited user must be created post-boot via
  `POST /api/users/`; because the `superadmin_backfill_v1` runs once at startup, a user
  created *after* boot does NOT get SuperAdmin → genuinely limited principal.
- `local_users` table + `admin` seed live **only** in `infra/postgres/init/02-create-autonate-app-schema.sql`
  (Docker runs it once on first volume init). The app's `DatabaseSchemaInitializer.EnsureAsync`
  (`Program.cs:831`) idempotently builds the rest (roles, menus, sample project, SuperAdmin
  backfill) but does NOT create `local_users`/`admin`, and nothing issues `CREATE DATABASE`.
  → The fixture must `CREATE DATABASE AutoNate_E2E` and replay `02-...sql` against it; the
  app initializer finishes the job on boot. Both init scripts are portable plain SQL (no
  `\connect`, no `CREATE EXTENSION`/`ROLE`/`OWNER`).
- The SPA has **no `data-testid`**. Locator strategy is role + accessible name + heading
  text + `aria-label` (Mantine renders these consistently). Concrete names captured below.

## Goals / Non-goals

**Goals:** broad coverage of primary user journeys with reliable selectors; a clean fixture
that gives each run an isolated, fully-seeded DB; reusable sign-in / API-seeding / navigation
helpers; one command to run everything.

**Non-goals:** pixel/visual regression; load testing; click-by-click automation of bpmn-js,
BlockNote, or the docx ProseMirror editor; CI pipeline wiring (deferred).

---

## Phase 0 — Foundation (the "playwright-foundation" work)

Everything else depends on this. Land it first as its own PR.

### 0.1 Dedicated ephemeral test DB (`AutoNateE2EFixture.cs`)
- Add `Npgsql` package ref to `tests/AutoNate.E2E.Tests/AutoNate.E2E.Tests.csproj`.
- In `InitializeAsync`, **before** `StartAppAsync`:
  1. Derive the dev connection string from `src/AutoNate.Web/appsettings.Development.json`
     (or a known constant); build a **maintenance** conn (swap `Database=postgres`) and a
     **test** conn (`Database=AutoNate_E2E`).
  2. On the maintenance connection: `DROP DATABASE IF EXISTS "AutoNate_E2E" WITH (FORCE);`
     then `CREATE DATABASE "AutoNate_E2E";`.
  3. On the test connection: execute the contents of
     `infra/postgres/init/02-create-autonate-app-schema.sql` (locate via repo root, same
     `FindRepoRoot()` walk already present). This creates `local_users` + seeds `admin`.
- In `StartAppAsync`, add env override: `info.Environment["ConnectionStrings__Default"] =
  <test conn>`. The app boots against `AutoNate_E2E`; `DatabaseSchemaInitializer` seeds the
  remainder (roles/menus/sample project) and the SuperAdmin backfill makes `admin` a
  super-admin.
- In `DisposeAsync`, optionally `DROP DATABASE AutoNate_E2E` (leave behind on failure for
  debugging — gate on an env flag like `E2E_KEEP_DB=1`).
- Requires the dev Postgres role to have CREATEDB (the dev `autonate` user via docker-compose
  is the DB superuser — confirm in `infra/` compose during implementation).

### 0.2 Test helpers (new files under `tests/AutoNate.E2E.Tests/Support/`)
- **`E2ETestBase.cs`** — abstract base: holds the fixture, exposes `NewSignedInPageAsync()`
  (new context → page → `SignInAsAdminAsync`) to cut boilerplate every class repeats.
- **`ApiSeeder.cs`** — helpers that use the signed-in `page.APIRequest` (admin cookie) to
  create prerequisite data fast, returning ids/keys:
  - `CreateRecordTypeAsync(...)` → `POST /api/record-types/`
  - `CreateRecordAsync(typeId, name, …)` → `POST /api/records/`
  - `CreateWorkflowAsync(...)` / start execution → `POST /api/workflows/`
  - `CreateUserAsync(username, password)` → `POST /api/users/` (for gating tests)
  - `GrantAsync(principal, action, selector)` → permission-grant endpoint
  - (Confirm exact request DTOs against `src/AutoNate.Web/Endpoints/*Endpoints.cs` — e.g.
    `UserEndpoints.CreateUserRequest(username, first, last, password, email)`.)
- **`Selectors`/naming discipline** — unique names per test (`$"e2e-{Guid.NewGuid():N}"`)
  so tests stay independent within a shared run.
- **`SignInAsUserAsync(page, username, password)`** already exists as `SignInAsync` — reuse.

### 0.3 Tooling & docs
- `make e2e` target: ensure infra up (`make infra-up`), build the test project, install
  chromium (the `dotnet exec … Microsoft.Playwright.dll install chromium` dance from the
  README, made idempotent), then `dotnet test tests/AutoNate.E2E.Tests`.
- Update `tests/AutoNate.E2E.Tests/README.md`: document the dedicated test DB, the helpers,
  `make e2e`, and correct the `user1` myth.

---

## Phases 1–10 — Test buildout

Each phase = one or more test classes in `tests/AutoNate.E2E.Tests/`. All inherit
`E2ETestBase`, use `[Collection(AutoNateE2ECollection.Name)]`, seed via `ApiSeeder`, assert
via UI. Counts are targets, not contracts.

### Phase 1 — Auth & shell (~6 tests) `AuthShellTests.cs`
Expand beyond current login: logout via user menu returns to `/`; protected route while
unauthenticated redirects to login; `/api/auth/me` reflects state; nav menu renders expected
top-level items for admin; unknown route renders the 404 page (`"404"` heading); session
persists across reload.

### Phase 2 — Records CRUD (core, ~10 tests) `RecordsCrudTests.cs`
The highest-value journey. Seed a record type via API, then drive UI:
- List page `/records/{code}` renders, "New {TypeName}" button present (`aria-label="New
  {name}"`).
- Create: click New → `/records/{code}/new` ("New {name}" heading) → fill `RecordForm` →
  Create → lands on `/record/{key}`.
- Edit a field in the Details tab → Save → reload → value persisted.
- Watch toggle (`aria-label="Watch"`/`"Unwatch"`) flips.
- Comments tab: add a comment (textarea → Save) → appears; "Show deleted" switch present.
- History tab renders the create/edit entries.
- Delete (`aria-label="Delete"` → confirm modal) → record gone from list. (Safe now — own DB.)
- Filter + Columns popovers open (`"Filters"`, `"Columns"` buttons).

### Phase 3 — Record types & edge types (~5 tests) `RecordTypeTests.cs`, `EdgeTypeTests.cs`
- `/record-types`: create type via inline modal, edit in `/record-types/:id`, define a field,
  archive/restore.
- `/record-relationship-types`: create an edge type; verify legacy `/record-edge-types`
  redirect. Then on a record's Edges tab, link two records via the edge dialog and remove it.

### Phase 4 — Workflows & executions (~6 tests) `WorkflowExecutionTests.cs`
(Keep/extend existing `WorkflowOverrideTests`.) Smoke `/workflow` (Studio mounts, no crash).
API-seed a workflow + start an execution, then UI:
- `/workflow-executions` shows the run; status stat cards render (RUNNING/COMPLETED/…).
- Row click opens execution detail modal / `/executions/:id` ("Execution" heading).
- Cancel execution (`aria-label="Cancel execution {name}"` → confirm) → status flips.
- Delete execution / delete-all now safe on the isolated DB.

### Phase 5 — Forms (~4 tests) `FormsTests.cs`
`/admin/config/forms`: create form (modal → shortCode+name) → editor `/admin/config/forms/:id`
mounts → save draft → publish. Then `/form/{shortCode}` renders the published form for a
signed-in user and a submit succeeds (assert via API or success UI).

### Phase 6 — Notes & Documents (smoke + API-backed, ~6 tests) `NotesTests.cs`, `DocumentsTests.cs`
- Notes: `/notes` mounts with sidebar/explorer; create a notebook+page via the modals (these
  are plain Mantine modals, safe to drive); opening a note mounts the editor pane (smoke — no
  BlockNote typing). `/projects` table renders.
- Documents: `/documents` project picker renders; open a project → `/documents/p/:id` folder
  view; create a folder (modal) and a document (modal); `/documents/edit/:id` editor page
  mounts (smoke — no docx ProseMirror automation); template gallery renders.

### Phase 7 — Admin config (~10 tests) `AdminConfigTests.cs`, `ManageUsersTests.cs`
- ConfigLayout sidebar groups expand/collapse; each major section route mounts with its
  heading: General, Features, Appearance, Status Appearance, External Connections, Pages/Menus,
  Events, System Health, Plugins, Projections, Chatbot Models, Security (Users/Groups/Roles/
  Permissions/Permission Checker).
- Manage Users: create a user (modal), verify row appears, reset-password affordance present.
- Roles/Groups/Grants: create a role, create a group, assign a grant (these drive the real
  IAM UI). Site settings: toggle a setting in a `SiteSettingsForm` group → Save → reload →
  persisted.
- (Extend existing `AgentSidebarTests` External-Connections coverage rather than duplicate.)

### Phase 8 — Agent sidebar (~3 tests) extend `AgentSidebarTests.cs`
Already covers open/close + external-connections modal. Add: composer textarea accepts input
(`placeholder="Ask AutoNate..."`); resize handle present (`aria-label="Resize chatbot"`);
Cmd/Ctrl+K opens the chat palette modal. (No real LLM round-trip.)

### Phase 9 — Notifications / profile / misc smoke (~5 tests) `MiscPagesTests.cs`
`/notifications` (bell, "Mark all as read"), `/user-profile` ("User Profile" heading, shows
admin), `/bus-watcher` mounts, dashboard mounts. Render/no-error-banner level.

### Phase 10 — Permission-gating multi-user (~5 tests) `PermissionGatingTests.cs`
Using `ApiSeeder.CreateUserAsync` to make a fresh limited user (no SuperAdmin — see Context):
- Sign in as limited user → admin-only routes (`/admin/config/...`) 403 / hidden.
- Record delete button absent for a user without `record:delete`.
- Grant a single permission via API → assert the corresponding affordance now appears.
This complements (does not duplicate) the API-level `AutoNate.Web.Tests/Authorization/` suite
by proving the SPA renders gates correctly.

---

## Selector reference (verified, for the implementer)
- Headings: `GetByRole(Heading, Name="Automation Dashboard" | "Workflow Executions" |
  "User Profile" | "Documents" | "Forms" | "Pages / Menus" | "Manage Users" | …)`.
- Record New button: `aria-label="New {typeName}"`; row open: `aria-label="Open {key}"`.
- Record detail: `aria-label="Watch"/"Unwatch"`, `aria-label="Delete"`; tabs "Details",
  "Edges", "History".
- Executions: `aria-label="Cancel execution {name}"`, `aria-label="Delete execution {name}"`;
  buttons "Refresh", "Delete All Executions".
- Agent: `aria-label="Open AutoNate assistant"`/`"Close assistant"`, `aria-label="Resize
  chatbot"`, composer `placeholder="Ask AutoNate..."`.
- Confirm modals use Mantine `Dialog` role with action buttons named "Create"/"Save"/
  "Delete"/"Cancel"/"Keep".

## Critical files
- `tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs` — DB bootstrap + conn-string override (0.1).
- `tests/AutoNate.E2E.Tests/AutoNate.E2E.Tests.csproj` — add `Npgsql`.
- `tests/AutoNate.E2E.Tests/Support/{E2ETestBase,ApiSeeder}.cs` — new helpers (0.2).
- `tests/AutoNate.E2E.Tests/*Tests.cs` — new per-phase test classes.
- `tests/AutoNate.E2E.Tests/README.md`, `Makefile` — `make e2e`, docs (0.3).
- Read-only references: `src/AutoNate.Web/Endpoints/*Endpoints.cs` (request DTOs for seeder),
  `infra/postgres/init/02-create-autonate-app-schema.sql` (replayed by fixture),
  `src/AutoNate.Spa/src/**` page components (selector confirmation).

## Verification
- `make e2e` (or `dotnet test tests/AutoNate.E2E.Tests`) runs green from a clean state.
- Run twice back-to-back → still green (proves DB isolation + name uniqueness; no order
  dependence).
- During dev of a phase, run headed (`PWDEBUG=1 dotnet test --filter <Class>`) to watch flows.
- Confirm `AutoNate_E2E` is created fresh and the dev `AutoNate` DB is untouched.

## Risks & mitigations
- **DB role lacks CREATEDB** → confirm dev Postgres user privileges in `infra/` compose; if
  not superuser, grant CREATEDB or run bootstrap as the postgres superuser.
- **Init-script drift** → fixture replays the real `02-...sql`, so it tracks the source of
  truth; if schema seeding moves into the app initializer later, drop the replay step.
- **First-run cost** (SPA build + browser install + DB seed) ~30–60s — unchanged from today;
  documented in README/`make e2e`.
- **Heavy-editor flakiness** avoided by the smoke+API strategy (decision #2).
- **Shared single app boot** across classes is retained (build-race rationale); per-run DB
  freshness + unique names keep tests independent without per-class app restarts.
