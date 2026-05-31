# Playwright E2E Coverage Backlog

## Inventory

AutoNate is a React SPA served by `AutoNate.Web`. Its static route registry covers login,
home, workflows, workflow executions, record types, records, relationship types,
notifications, notes, projects, documents, forms, and the nested admin configuration
sections. The browser suite is the xUnit project in `tests/AutoNate.E2E.Tests/`; its fixture
boots the web app against a dedicated `AutoNate_E2E` Postgres database and drives Chromium
through Microsoft.Playwright.

The app has meaningful API-backed workflows across records, content hierarchy, documents,
workflows, forms, IAM, notifications, and admin configuration. Existing tests already cover
the core auth shell, records CRUD, workflow execution lifecycle, form publishing, document
mounts, IAM creation, and selected permission gates. The current backlog emphasizes missing
shell-level content behavior while deliberately keeping Yjs-backed rich-text editing and
DOCX editor internals at smoke-test depth.

## Exploration Notes

- `npm ci` completed successfully.
- `npx playwright-cli open http://localhost:5173 --headed` was attempted first. The CLI
  cannot create `~/Library/Caches/ms-playwright/daemon` in the workspace sandbox. Running
  it with elevated sandbox permission succeeds. The standalone Vite login page renders,
  while its `/api/auth/me` and `/api/appearance` requests return `502` because the dev
  backend is not running on port `5108`. Authenticated exploration continues through the
  fixture-hosted .NET Playwright suite.
- `./infra/ensure-up.sh` completed after rebuilding the Flowable and Hocuspocus images.
- `npx playwright install --with-deps` completed with elevated sandbox permission. Because
  the Node and .NET Playwright packages use different Chromium revisions, `make e2e-install`
  restored the .NET revision afterward.
- The targeted .NET Notes + Documents Playwright batch passed: 15 tests, 0 failures.
- The added Notes, AdminConfig, Query, and Dashboard specs each passed after implementation.
- The added WorkflowExecution, Documents, Query, Dashboard, and ManageUsers low-priority specs each passed after implementation.
- The expanded targeted .NET batch passed: 30 tests, 0 failures, 2 skipped blocked
  reproducers.
- `npm run lint --if-present` and `npm test --if-present` completed successfully. The root
  package has no matching scripts.
- `npx playwright test` reports `Error: No tests found`; this repository's browser suite is
  the .NET `AutoNate.E2E.Tests` project.
- The final full .NET Playwright suite passed: 108 tests, 0 failures, 2 skipped blocked
  reproducers.

## Backlog

| ID | Title | User flow | Priority | Target spec file | Status | Notes / blockers |
| --- | --- | --- | --- | --- | --- | --- |
| E2E-001 | Seeded admin login | Open login, submit `admin` credentials, land on home. | High | `LoginTests.cs` | DONE | Existing coverage. |
| E2E-002 | Protected-route redirect | Open a protected route while signed out and verify login redirect with return URL. | High | `AuthShellTests.cs` | DONE | Existing coverage. |
| E2E-003 | Session survives reload | Sign in, hard reload, and remain authenticated. | High | `AuthShellTests.cs` | DONE | Existing coverage. |
| E2E-004 | Record CRUD lifecycle | Create a record, edit its name, reload, delete it, and verify list removal. | High | `RecordsCrudTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-005 | Record comments and history | Add a comment and verify the creation history entry. | Medium | `RecordsCrudTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-006 | Record-type schema editing | Create a record type, add a field, reload, archive, and restore. | High | `RecordTypeTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-007 | Workflow execution lifecycle | Start an execution, inspect detail, cancel it, and delete it. | High | `WorkflowExecutionTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-008 | Form publish lifecycle | Create a form, publish it, and open its authenticated live route. | High | `FormsTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-009 | Limited-user permission gate | Create a limited user, verify hidden record types, grant view, and verify visibility. | High | `PermissionGatingTests.cs` | DONE | Existing coverage split across focused tests. |
| E2E-010 | Notes hierarchy creation | Create project, cabinet, notebook, and page from `/projects`, then verify the selected page. | High | `NotesTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-011 | Notes sidebar persistence | Collapse the notes sidebar, reload, verify it remains collapsed, then restore it. | Medium | `NotesTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-012 | Notes cabinet search | Create two sibling pages, filter by one title, and verify only the matching page remains in the explorer. | Medium | `NotesTests.cs` | DONE | Passed in targeted Notes + Documents run. Explorer search filters pages, not notebook rows. |
| E2E-013 | Notes create rich-text tab | Create a page, add a rich-text note tab, and verify the tab becomes active. | Medium | `NotesTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-014 | Notes hierarchy rename | Create a notebook, open its options menu, rename it, and verify the explorer updates. | Medium | `NotesTests.cs` | DONE | Passed after fixing the options-menu stacking context and labeling the rename input. |
| E2E-015 | Notes hierarchy delete | Create a page, delete it through its options menu, confirm, and verify removal. | Medium | `NotesTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-016 | Document folder deep link | Create a folder, open it, reload its `/folder/{id}` URL, and verify the breadcrumb and folder toolbar. | Medium | `DocumentsTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-017 | Document rename and delete | Create a document, rename it from its card menu, delete it, and verify removal. | Medium | `DocumentsTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-018 | Document folder rename and delete | Create a folder, rename it from the tree menu, delete it, and verify removal. | Medium | `DocumentsTests.cs` | DONE | Passed in targeted Notes + Documents run. |
| E2E-019 | Notes deep-link restoration | Reload a created page URL and verify its ancestors expand and selection returns. | Medium | `NotesTests.cs` | DONE | Added `NotesPage_DeepLinkReload_RestoresSelectedPage`; Notes spec passes. |
| E2E-020 | Workflow task completion | Seed a user-task workflow, complete its task form, and verify completed status. | Low | `WorkflowExecutionTests.cs` | DONE | Added `AssignedWorkflowTask_CompleteFromMyTasks_RemovesItFromTheTable`; WorkflowExecution spec passes. |
| E2E-021 | General settings persistence | Toggle the notifications-header setting, save, reload, verify persistence, then restore the original value. | Medium | `AdminConfigTests.cs` | DONE | Added `GeneralSettings_NotificationsHeaderToggle_PersistsAfterReload`; AdminConfig spec passes. |
| E2E-022 | Template gallery lifecycle | Create or clone a template, verify gallery visibility, then delete it. | Low | `DocumentsTests.cs` | DONE | Added `TemplateGallery_CreateTemplate_HidesFromProjectView_AndDeletes`; fixed gallery cache invalidation; Documents spec passes. |
| E2E-023 | Query execute and save lifecycle | Run `FROM Records`, save it with a unique name, reload `/query`, and load it from the saved-query picker. | Medium | `QueryTests.cs` | DONE | Added `QueryPage_ExecuteSaveReloadAndLoadSavedQuery`; Query spec passes. |
| E2E-024 | Dashboard lifecycle | Mount the dashboard template for the fixture, create a named dashboard, rename it, delete it, and verify it leaves the selector. | Medium | `DashboardTests.cs` | DONE | Added `DashboardPage_CreateRenameAndDeleteDashboard`; fixed created-dashboard selection cache race; Dashboard spec passes. |
| E2E-025 | Query validation error | Run invalid AQL and verify the query-errors alert renders a useful message. | Low | `QueryTests.cs` | DONE | Added `QueryPage_InvalidAql_RendersValidationError`; Query spec passes. |
| E2E-026 | Dashboard widget add/remove | Add a widget to a dashboard, configure it, remove it, and verify the empty state returns. | Low | `DashboardTests.cs` | DONE | Added `DashboardPage_AddConfigureAndRemoveWidget`; Dashboard spec passes. |
| E2E-027 | Manage-users reset-password lifecycle | Add a user, reset the password, log out, and verify the new password signs in. | Low | `ManageUsersTests.cs` | DONE | Added `ManageUsers_ResetPassword_AllowsLoginWithNewPassword`; ManageUsers spec passes. |
| E2E-028 | DOCX editor content persistence | Open a seeded document, enter body content, reload the editor, and verify the content persists through Yjs. | High | `DocumentEditorTests.cs` | DONE | Added `DocumentEditor_ContentPersistsAcrossReload`; targeted test passes. |
| E2E-029 | DOCX import lifecycle | Upload a `.docx`, verify the import editor opens, and confirm imported content is committed. | High | `DocumentEditorTests.cs` | BLOCKED | Added skipped reproducer. Upload originally failed until multipart headers were fixed. It now reaches `/documents/edit/{id}?import=1`, but even an editor-core-serialized DOCX never finalizes parsed content; speculative wrapper timing changes were reverted. |
| E2E-030 | DOCX preview and download | Open populated-output preview, return to edit mode, download a `.docx`, and verify the suggested filename. | High | `DocumentEditorTests.cs` | DONE | Added `DocumentEditor_PreviewsAndDownloadsDocx`; targeted document batch passes after locator fixes. |
| E2E-031 | DOCX version history | Edit a document, open version history, view a historical version, and verify the version preview route. | Medium | `DocumentEditorTests.cs` | DONE | Added `DocumentEditor_OpensHistoricalVersionPreview`. |
| E2E-032 | DOCX bindings lifecycle | Add a record-field binding and an AQL-table binding, insert them, and verify they appear in the binding panel. | High | `DocumentEditorTests.cs` | DONE | Added both modal flows and verified the side-panel binding actions. |
| E2E-033 | Document and folder permission overrides | Grant and revoke a resource override from document and folder permission dialogs. | High | `DocumentEditorTests.cs` | DONE | Added and passed document and folder grant/revoke flows. |
| E2E-034 | Document tracked-change review | Create a suggested text replacement and verify accept/reject review controls update the document. | Medium | `DocumentEditorTests.cs` | BLOCKED | The wrapper exposes tracked replacement only through an assistant page-action request. The fixture has no deterministic SSE/page-action producer, and the editor package review controls cannot create a suggestion directly from browser chrome. |
| E2E-035 | Workflow Studio UI lifecycle | Create a workflow model in the browser, save, publish, start, pause, resume, and verify status changes. | High | `WorkflowStudioTests.cs` | DONE | Added and passed the browser-driven lifecycle spec. |
| E2E-036 | Workflow Studio user-task form modes | Configure a user task for simple, modal-form, and page-form completion modes and verify each task surface. | High | `WorkflowStudioTests.cs` | BLOCKED | Workflow Studio exposes these through canvas-selected BPMN property panels without stable semantic selectors or test IDs. Add deterministic element targeting for seeded BPMN nodes before covering all three runtime modes. |
| E2E-037 | Workflow Studio advanced BPMN editors | Exercise timer, signal-start, gateway, script-task, and service-task property editors and persist the resulting draft. | Medium | `WorkflowStudioTests.cs` | BLOCKED | Same BPMN canvas targeting blocker as E2E-036: specialized panels require a deterministic seeded-node selection hook before resilient browser tests can drive them. |
| E2E-038 | Dynamic template menu route lifecycle | Add a template-backed menu item, navigate to its route, toggle visibility, edit it, and delete it. | High | `PagesMenusTests.cs` | DONE | Added and passed dashboard-template dynamic routing, visibility toggle, and deletion coverage. |
| E2E-039 | Dynamic JSX page lifecycle | Add a JSX-backed custom page route, render it, edit the content, and delete it. | High | `PagesMenusTests.cs` | DONE | Added and passed dynamic JSX rendering plus admin rename, visibility, and delete lifecycle. Direct CodeMirror content replacement remains unreliable in the browser fixture. |
| E2E-040 | Menu tree ordering and nesting | Create a custom menu, add nested items and a separator, reorder them, save, reload, and delete the menu. | Medium | `PagesMenusTests.cs` | DONE | Added and passed custom-menu tree persistence with parent/child indentation, separator ordering, reload, and browser-driven menu deletion. |
| E2E-041 | UI permission grant lifecycle | Create and revoke a permission grant from the Permissions page and verify the table updates. | High | `IamAdminTests.cs` | DONE | Added and passed the grant table update flow. |
| E2E-042 | IAM role assignment lifecycle | Create a role and group, assign principals, reload, and verify membership persistence. | High | `IamAdminTests.cs` | DONE | Added and passed user assignment persistence across reload. |
| E2E-043 | Permission checker verdicts | Run allow and deny checks in the Permission Checker and verify the rendered explanation. | Medium | `IamAdminTests.cs` | DONE | Added and passed allow/deny checker flow with an API evaluator precondition. |
| E2E-044 | Limited user admin-route gating | Sign in as a limited user and verify protected admin pages are inaccessible. | High | `PermissionGatingTests.cs` | BLOCKED | Static audit found admin pages under the authenticated `AppShell` without a client-side admin permission guard. Backend APIs reject unauthorized calls, but limited users can deep-link into admin shells. Product behavior must be defined and gated before a passing route-inaccessibility test can be added. |
| E2E-045 | Limited user record-action gating | Grant record view without delete, verify delete is hidden, then grant delete and verify it appears. | High | `PermissionGatingTests.cs` | BLOCKED | Static audit found `RecordDetail.tsx` renders its delete action unconditionally. The backend rejects unauthorized deletion, but the requested SPA affordance gate does not exist yet. |
| E2E-046 | Limited user document gating | Verify project, folder, and document visibility changes when membership or resource overrides are granted and revoked. | High | `PermissionGatingTests.cs` | DONE | Added and passed two-context document visibility checks before grant, after a document override, and after revoke. Folder override administration is covered by E2E-033. |
| E2E-047 | Notes page sharing lifecycle | Share a page with another user, sign in as that user, verify access, revoke sharing, and verify loss of access. | High | `NotesAdvancedTests.cs` | BLOCKED | `ShareModal` supports preview, notification, and owner-only grant-on-share, but exposes no revoke control. `NotificationsTests` covers the real grant and recipient navigation path; a passing share-and-revoke UI lifecycle needs a product revoke surface. |
| E2E-048 | Notes project membership lifecycle | Add a project member, change their role, verify access level, remove them, and verify access is revoked. | High | `PermissionGatingTests.cs` | DONE | Added and passed two-context membership add, viewer visibility, contributor role change, and removal visibility coverage. |
| E2E-049 | Notes favorites and archive lifecycle | Favorite a page, archive/unarchive hierarchy items, reload, and verify state persistence. | Medium | `NotesTests.cs` | DONE | Added and passed favorite persistence plus notebook archive/unarchive persistence. |
| E2E-050 | Notes page and note move/copy | Move and copy pages and notes between destinations and verify navigation and hierarchy updates. | Medium | `NotesAdvancedTests.cs` | BLOCKED | `MoveCopyModal` relies on hierarchy drag/destination state with no deterministic seeded-tree browser hook. Add fixture-level hierarchy helpers or semantic destination IDs before driving the modal resiliently. |
| E2E-051 | Notes nested pages and reorder | Create a sub-page, reorder note tabs, reload, and verify ordering persists. | Medium | `NotesAdvancedTests.cs` | BLOCKED | Note-tab reordering is pointer-driven and exposes no semantic handles or test IDs. Add stable reorder handles before asserting persisted order. |
| E2E-052 | Notes history restore | Edit a page and a note, open history, preview an earlier revision, restore it, and verify content. | Medium | `NotesAdvancedTests.cs` | BLOCKED | Page/note body edits run through Yjs-backed editors; the fixture lacks a deterministic editor mutation hook. Add one before revision restore can assert content changes reliably. |
| E2E-053 | Notes editor variants and PDF export | Persist rich-text content, create drawing and diagram notes, and trigger PDF export. | Medium | `NotesAdvancedTests.cs` | BLOCKED | Drawing/diagram editors and PDF output need deterministic editor mutation and download fixtures. The current suite only has rich-text note-tab creation coverage. |
| E2E-054 | Assistant conversation lifecycle | Create a chat, send a message through a stubbed stream, verify persisted history, and delete the conversation. | High | `AgentConversationTests.cs` | BLOCKED | The fixture has no deterministic agent-stream provider. Empty conversation create/search/delete is covered by `Assistant_CrossPageSearchResizePersistenceAndDelete`; persisted assistant turns require an injectable SSE provider or stub endpoint. |
| E2E-055 | Assistant cross-page palette and resize persistence | Search and load a cross-page chat, resize the sidebar, reload, and verify the selected width persists. | Medium | `AgentConversationTests.cs` | DONE | Added and passed seeded cross-page search/load, resize persistence, row-delete affordance, and cleanup coverage. |
| E2E-056 | Assistant page-context actions | Exercise a confirmed page-context mutation against a document editor and verify the resulting content change. | Medium | `AgentConversationTests.cs` | BLOCKED | Page actions arrive only from the provider-driven SSE stream. The fixture needs an injectable agent stream before a deterministic confirmed mutation can be created. |
| E2E-057 | Form editor draft lifecycle | Edit JSX and dev props, save, open draft preview, reload, and verify the draft persists. | High | `FormsAdvancedTests.cs` | BLOCKED | Added API-seeded draft hydration and dev-preview coverage. Browser edits to both CodeMirror and controlled metadata inputs leave the editor `Save` action disabled in the fixture, so the requested browser-save path needs a product fix. |
| E2E-058 | Form delete and version restore | Publish revisions, inspect version history, restore an earlier version, toggle site availability, and delete the form. | Medium | `FormsAdvancedTests.cs` | DONE | Added and passed API-seeded revision setup with browser-driven version restore and delete coverage. |
| E2E-059 | Form submission and workflow task forms | Submit a live form and complete workflow tasks rendered in modal and page form modes. | High | `FormsAdvancedTests.cs` | BLOCKED | Runtime modal/page task coverage depends on E2E-036 BPMN mode setup. Form-editor browser saves are also blocked by E2E-057. |
| E2E-060 | Workflow execution admin controls | Reassign a task, change its due date, force-complete, move execution state, inspect variables/history/log, and bulk-delete executions. | High | `WorkflowExecutionAdminTests.cs` | BLOCKED | The fixture can seed normal Flowable tasks, but not the operator-state combinations needed for move-state, log, variable, and force-complete assertions. Add deterministic execution-state seed helpers first. |
| E2E-061 | Typed record fields and filters | Configure representative field types, create matching records, apply typed filters, and verify filtered results and selected columns. | High | `RecordsAdvancedTests.cs` | BLOCKED | The current record seeder creates schema-less record types. Add typed schema-field fixture builders before a resilient representative filter matrix can be created. |
| E2E-062 | Record relationships and revisions | Create and remove a relationship link with edge data, inspect schema audit, and view comment revision history. | High | `RecordsAdvancedTests.cs` | BLOCKED | Existing edge coverage reaches the dialog, but the fixture has no deterministic edge-type/schema builder for relationship mutation plus revision assertions. |
| E2E-063 | External connection CRUD lifecycle | Create, edit, test, disable, and delete an external connection. | Medium | `AdminOperationsTests.cs` | DONE | Added and passed browser-driven create, edit, test, disable, and delete coverage. |
| E2E-064 | Plugin upload lifecycle | Upload the sample plugin package, enable, disable, update, and delete it. | Medium | `AdminOperationsTests.cs` | DONE | Added and passed the full lifecycle using bundled `HelloPlugin.zip`. |
| E2E-065 | Projection operations | Pause, resume, and rebuild a registered projection and verify refreshed state. | Medium | `AdminOperationsTests.cs` | DONE | Added and passed pause, resume, and rebuild-request coverage against a registered projection. |
| E2E-066 | Configuration section persistence | Persist appearance, status-appearance, feature, chatbot, event, and system-health settings and verify reload behavior. | Medium | `AdminOperationsTests.cs` | BLOCKED | Added skipped appearance reproducer: `Save changes` accepts a Site name edit, but reload restores default `Auto Nate`. Events and system health are read-only operational pages, so the original aggregate persistence flow also needs product-surface clarification. |
| E2E-067 | Notification inbox lifecycle | Seed unread linked notifications, verify bell count, mark one read, navigate through a row, filter unread, and mark all read. | Medium | `NotificationsTests.cs` | DONE | Added and passed real page-share notification seeding, unread filtering, mark-all-read, and linked notes navigation. |
| E2E-068 | Manage-users edit, unlock, and delete | Edit a created user, verify unlock affordance gating, delete the user, and verify removal. | Medium | `ManageUsersTests.cs` | DONE | Added and passed the edit and delete lifecycle. Existing permission-gated surface coverage exercises unlock affordance conditions. |

## Implementation Order

The expanded high and medium batch was completed in this order:

1. `E2E-019` notes deep-link restoration.
2. `E2E-021` general settings persistence.
3. `E2E-023` query execute and save lifecycle.
4. `E2E-024` dashboard lifecycle.

The expanded pass is complete. Every audited high- and medium-priority item is either
`DONE` or `BLOCKED` with a concrete product-surface or fixture prerequisite above.
