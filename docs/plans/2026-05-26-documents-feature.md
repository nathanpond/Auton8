# Documents Feature — Architecture Plan

> **Note**: Per `reference_plan_location` memory, after exiting plan mode this should be moved to `./docs/plans/2026-05-26-documents-feature.md`.

## Context

AutoNate needs a Google-Docs-style **Documents** subsystem alongside the existing notes/pages feature. It shares projects with notes but has its own hierarchy (folders), its own editor (rich, DOCX-export-capable), comment workflow, live data bindings, document templates, and deep AI integration. The collab + auth foundations are already in place (Hocuspocus running on :1234 with Postgres persistence; `IContentAuthorizer` doing closest-ancestor override resolution; `AgentSidebar` + `/api/agent/*` for AI). The work is mostly **new feature surface area** that plugs into existing infrastructure, not a foundational refactor.

### Decisions (from user)
- **Editor**: docx-editor (TipTap-based) as a dedicated documents editor. Persist as structured Yjs/JSON internally; DOCX is an *export* format only.
- **Folder model**: Single self-referential `Folder` table, unlimited nesting, separate from the existing Cabinet/Notebook layout used by notes.
- **Roles**: Add `commenter` as a 4th project-level role (Owner / Contributor / Commenter / Viewer), applied to *all* content kinds.
- **Permission overrides**: Full ACL per folder/document — can GRANT or DENY relative to inherited, including granting non-project-members read/comment access.
- **Templating**: Templates and documents both carry live bindings; **bindings resolve on open into a snapshot + manual per-binding refresh + "Refresh all" header action** (no auto-poll). Yjs holds the snapshot so collaborators see the same render.
- **Document vs Template**: One `Document` entity with a `kind` discriminator (`document` | `template`). Documents created from templates carry a `template_id` reference and an independent copy of the binding configs.
- **AI in v1**: All four — inline writing assist, chat-with-document, NL→AQL binding suggestion, AI doc generation from template.
- **v1 scope**: Versioning (mirror `PageVersion`), DOCX + PDF server-side export, **DOCX / DOTX import on create** (`.docx` → new document, `.dotx` → new template), open-editor-in-new-tab distraction-free shell.
- **Deferred to v2**: External / anonymous link sharing; unifying folders with notes' cabinet/notebook tree; **importing a `.docx` into an existing document with diff/merge UI** (paragraph-level diff, accept/reject per chunk, applied as a Yjs transaction).

---

## Architecture

### 1. Server-side data model

New EF entities in `src/AutoNate.Web/Persistence/Scaffolded/` (mirror the scaffolded style of `Page.cs`, `Note.cs`):

| Entity | Key fields | Notes |
|---|---|---|
| `Folder` | `Id`, `ProjectId` (fk), `ParentFolderId` (fk, nullable), `Name`, `Description`, `Icon`, `SortOrder`, audit | Self-referential; root folders have null parent. Path resolved via recursive CTE. |
| `Document` | `Id`, `ProjectId` (fk), `FolderId` (fk, nullable for project-root docs), `Kind` (`document`/`template`), `TemplateId` (fk, nullable), `Title`, `Description`, `BodyJsonb`, `CurrentVersionNumber`, audit | `BodyJsonb` holds the canonical JSON snapshot synced from Hocuspocus. |
| `DocumentVersion` | mirror `PageVersion` | Each save (or webhook-driven snapshot) inserts a row. |
| `DocumentBinding` | `Id`, `DocumentId` (fk), `BlockId` (TipTap node id), `Kind` (`aql-table`/`aql-chart`/`record-field`/`workflow-data`), `ConfigJsonb` (query/source), `LastResolvedValueJsonb`, `LastResolvedAt`, `LastResolvedByUserId` | Source of truth for binding values; editor JSON references by `BlockId`. |
| `DocumentComment` | `Id`, `DocumentId` (fk), `BlockId`, `ThreadId`, `ParentCommentId` (fk, nullable), `AuthorId`, `BodyMarkdown`, `ResolvedAt`, `ResolvedByUserId`, audit | Threaded; `ThreadId` groups a top-level comment + replies. |
| `DocumentExport` | `Id`, `DocumentId`, `Format` (`pdf`/`docx`), `Status`, `ResultPath`, `RequestedByUserId`, `RequestedAt`, `CompletedAt`, `ErrorMessage` | Async export queue; rendered files in `./data/documents/exports/{documentId}/`. |

DbSets registered in `src/AutoNate.Web/Persistence/AutoNateDbContext.cs` (next to lines 93–95 where `Projects` / `ProjectMembers` register).

A single EF migration adds all of the above plus indexes on `(ProjectId, ParentFolderId)`, `(FolderId)`, `(DocumentId, BlockId)`.

### 2. Disk layout under `./data/documents/`

```
./data/documents/
  ├── exports/{documentId}/{exportId}.{pdf|docx}    # server-rendered exports
  ├── images/{documentId}/{imageId}.{ext}            # embedded images (large or AI-generated)
  └── binding-cache/{documentId}/{bindingId}.json    # large binding payloads (when inline jsonb too big)
```

Path resolution extends `DataPaths.cs` with `DocumentExportsRoot()` etc. The existing `ContentAttachmentOptions` pattern is the template; mirror it.

### 3. Roles + permissions

**`src/AutoNate.Web/Services/Content/ProjectRole.cs`** — insert `Commenter`:
```csharp
public enum ProjectRole { Viewer = 0, Commenter = 1, Contributor = 2, Owner = 3 }
```
(Renumbers `Contributor` and `Owner`; safe because `project_members.role` stores the lowercase *string*, not the enum int. Verify no code persists the int form before editing — `ProjectRoleNames.TryParse` / `ToWire` are the wire boundary.)

Add `ProjectRoleNames.Commenter = "commenter"` and update `ToWire` / `TryParse`. Update the role→action mapping (wherever `IContentAuthorizer` derives baseline actions from role) so that Commenter gets `View` + `Comment` on all content kinds. Notes/pages get `Comment` as a recognized action even though they don't have a comment UI yet — keep the vocabulary uniform.

**`src/AutoNate.Web/Services/Content/EntityKinds.cs`** — register two new kinds:
- `Folder` — ancestor chain: closest `Folder` → … → `Project`.
- `Document` — ancestor chain: `Folder` (if any) → … → `Project`.

Templates are documents with `Kind = "template"`; they share the same `EntityKind` and authorizer (no separate kind). Actions per `Document`: `View`, `Edit`, `Comment`, `Delete`, `Manage` (override grants + sharing), `Export`, `RefreshBindings`. Actions per `Folder`: `View`, `Edit` (rename/move), `CreateDocument`, `CreateFolder`, `Delete`, `Manage`.

The existing override mechanic (`permission_grants` keyed by `entity_kind` + `resource_id`) already supports both DENY and GRANT, including granting to non-project-members — no engine change needed. Just register the kinds.

### 4. Endpoints

Mirror the layout under `src/AutoNate.Web/Endpoints/Content*Endpoints.cs`:

- `ContentFolderEndpoints.cs` — `GET/POST/PATCH/DELETE /api/content/folders[/{id}]`, `GET /api/content/folders/{id}/children` (folders + documents in one shape).
- `ContentDocumentEndpoints.cs` — CRUD; `POST /api/content/documents/from-template/{templateId}` to clone a template into a new document (copies bindings configs, *not* values, then triggers initial refresh).
- `ContentDocumentVersionEndpoints.cs` — list + restore (mirror page-version pattern).
- `ContentDocumentCommentEndpoints.cs` — CRUD + resolve/unresolve.
- `ContentDocumentBindingEndpoints.cs` — `POST /api/content/documents/{id}/bindings/{bindingId}/refresh`, `POST /api/content/documents/{id}/bindings/refresh-all`. Refresh runs `DocumentBindingResolver`, persists the new value, and pushes the update through the Hocuspocus Yjs doc (so all open editors see it).
- `ContentDocumentExportEndpoints.cs` — `POST /api/content/documents/{id}/export?format=pdf|docx` enqueues; `GET .../export/{exportId}` polls + downloads.

Every endpoint uses `RequirePermission(EntityKinds.Document|Folder, Actions.X, "id")` exactly like `NoteEndpoints.cs:40`.

### 5. Hocuspocus integration

The existing sidecar (`services/hocuspocus/src/index.ts` + `auth.ts`) already authorizes per-document by hitting an ASP.NET internal endpoint. Required changes:

- **Auth hook**: extend the document-id prefix matching to recognize `documents:{documentId}` and call the .NET internal-authorize endpoint with `entity_kind = "document"` and a requested action (`view`/`edit`/`comment`). Return Hocuspocus's `readOnly: true` flag for view-only or comment-only sessions; the editor uses that signal to disable content edits while still allowing comment-thread updates (comments aren't part of the Yjs doc — they go through REST).
- **Persistence**: no schema change; the existing `yjs_documents` table keys by string, just a new prefix.
- **Webhook**: on `onDisconnect` / periodic snapshot the Node side already POSTs to .NET; add a handler for the new prefix that writes `Document.BodyJsonb` and inserts a `DocumentVersion` row (same pattern as pages).
- **Binding push**: When the server resolves a binding refresh, broadcast a `binding-updated` awareness event (or a small Yjs map mutation) so open editors update without re-fetching the whole doc.

Add an envvar `HOCUSPOCUS_DOCUMENTS_ENABLED=true` (default true) so the new prefix can be flagged off in case of trouble.

### 6. Editor — docx-editor wrapper

**Vendor cautiously.** docx-editor (`github.com/eigenpal/docx-editor`) is a Yjs-aware TipTap-based editor monorepo. Approach:
1. Add `@docx-editor/editor` (and `@docx-editor/agents` for AI primitives) as deps, pinned to a known version. If the upstream API surface is unstable, fork into `vendor/docx-editor/` and pin a hash.
2. **Bundle isolation**: BlockNote uses TipTap internally; version mismatch with docx-editor's TipTap could cause duplicate-module crashes. Lazy-load docx-editor only on the editor route (`React.lazy` + `Suspense`) and configure Vite `optimizeDeps.include` to deduplicate `@tiptap/*` packages. If dedup fails, mount the documents editor inside a Vite-time separate chunk that does *not* import BlockNote.
3. **Wrap** in `src/AutoNate.Spa/src/components/documents/editor/DocxEditor.tsx`:
   - Construct a `Y.Doc` and connect via `HocuspocusProvider` to `ws://localhost:1234/yjs?token=...&docId=documents:{id}` (existing pattern from BlockNote — reuse `src/AutoNate.Spa/src/lib/yjs/` helpers).
   - Read-only / comment-only mode driven by the `readOnly` flag returned from auth.
   - Configure docx-editor's AI provider config to point at `/api/agent/documents/*` rather than its bundled defaults (replace the eigenpal LLM client with a thin adapter against our existing `/api/agent/conversations` endpoint).

**Custom TipTap nodes for bindings** (`src/AutoNate.Spa/src/components/documents/editor/nodes/`):
- `AqlTableNode` — attrs `{ bindingId }`; renders `<AqlTableBindingView />` which reads cached value + refresh button.
- `ChartNode` — attrs `{ bindingId }`; same pattern, renders chart via existing chart components.
- `RecordFieldNode` — inline binding to a single record field; renders text.
- `WorkflowDataNode` — workflow execution data binding (optional v1.1).

Each node's view fetches its `DocumentBinding` row via `useDocumentBinding(bindingId)`, renders the cached value, and shows a "Refresh" icon button that hits the refresh endpoint. Toolbar: "Refresh all bindings" button calls the bulk endpoint and shows a progress indicator.

### 7. Routes + shell

`src/AutoNate.Spa/src/routes/appRoutes.tsx`:

| Route | Component | Shell |
|---|---|---|
| `/documents` | `DocumentsHomePage` (project picker + recent documents) | normal AppShell |
| `/documents/p/:projectId` | `ProjectDocumentsPage` (folder tree + breadcrumb + root view) | normal AppShell |
| `/documents/p/:projectId/folder/:folderId` | `FolderViewPage` (Drive-style grid of children) | normal AppShell |
| `/documents/templates` | `TemplateGalleryPage` | normal AppShell |
| `/documents/edit/:documentId` | `DocumentEditorPage` | **minimal shell — no NavMenu, no Footer, full-width** |

**Minimal shell**: `src/AutoNate.Spa/src/shell/AppShell.tsx` already wraps everything. The cleanest way to give the editor its own chrome is to check the route in `AppShell.tsx` and render a different layout subtree for `/documents/edit/*` — or, preferred, register `/documents/edit/:documentId` *outside* the AppShell route group so it never enters the shell. The editor route renders its own `<DocumentEditorShell>` with just: title bar (rename, sharing, versions, refresh-all, export, AI panel toggle), main editor area, optional right-side chat panel.

**Open-in-new-tab**: Folder views' "Open document" button uses `target="_blank"` + the editor route, so users get the Google-Docs new-tab UX. The editor route works fine in-tab too (deep-linkable).

**Sidebar folder tree**: a persistent collapsible `FolderTree` (`src/AutoNate.Spa/src/components/documents/FolderTree.tsx`) renders on `/documents/p/:projectId/**` — lazy-loaded children to handle deep trees.

**NavMenu**: Add a top-level "Documents" item to `src/AutoNate.Spa/src/shell/NavMenu.tsx`. Mirror how notes is presented (FA icon, route).

### 8. AI integration

Reuse existing `/api/agent/*` infra. Add server services + endpoints:

- `src/AutoNate.Web/Services/Agent/DocumentAgentService.cs` — wraps the LLM client with document-scoped helpers.
- Endpoints (new file `AgentDocumentEndpoints.cs`):
  - `POST /api/agent/documents/{id}/inline-assist` — `{ prompt, selectionText, surroundingContext }` → streamed completion. Authorized via `Document.Edit`.
  - `POST /api/agent/documents/{id}/chat` — Q&A over the document's content. Authorized via `Document.View`. Reuses `AgentConversation` so chat history persists; uses a `documentId` scope on conversations.
  - `POST /api/agent/documents/{id}/bindings/suggest` — `{ naturalLanguage }` → suggested `{ kind, configJsonb }` (leans on the existing AQL grammar/schema tooling visible in the recent commits — `AQL NOW` token, schema responses). Authorized via `Document.Edit`.
  - `POST /api/agent/documents/templates/{templateId}/generate-document` — `{ prompt, projectId, folderId }` creates a new document, runs AI to fill body content, returns the new doc id. Authorized via `Folder.CreateDocument` on the target folder + `Document.View` on the template.

Client integration:
- Slash-menu and floating toolbar inline-assist (`InlineAssist.tsx`).
- `DocumentChatPanel.tsx` — adapts the existing `AgentSidebar` pattern but scoped to a single document. Register a `PageContextProvider` for the editor route (per `add-page-context-provider` skill) so the chatbot sees the doc state.
- Binding insert dialog (`InsertBindingDialog.tsx`) — NL textarea → calls `/bindings/suggest` → preview the suggested table/chart → confirm to insert.
- Template gallery has "Generate document from prompt" button.

### 9. Server-side export

`src/AutoNate.Web/Services/Documents/DocumentRenderService.cs`:
- **DOCX**: use `DocumentFormat.OpenXml` (Microsoft's OpenXML SDK, NuGet) to walk the document JSON (TipTap node tree) and emit OpenXML. Bindings expand to their cached `LastResolvedValueJsonb` (or trigger a refresh-on-publish — opt-in flag).
- **PDF**: render HTML from the same JSON, then headless Chromium / Puppeteer. Check `src/AutoNate.Web/` for an existing PDF service first — if absent, add Puppeteer-Sharp or proxy to a Node helper inside the existing Hocuspocus container.

Exports run via an `IHostedService` queue worker (mirror any existing background queue; otherwise a `Channel<T>`-backed worker). Outputs land in `./data/documents/exports/{documentId}/{exportId}.{ext}` and are downloadable by users with `Document.Export` permission.

### 9b. DOCX / DOTX import (v1 — create-only)

`src/AutoNate.Web/Services/Documents/DocumentImportService.cs` — parallel to (and sharing the OpenXML dependency with) `DocumentRenderService`. One service, two operations: render (out) and parse (in).

**Flow**
1. **Upload endpoint**: `POST /api/content/documents/import` (multipart) — takes `{ file, projectId, folderId?, title?, kind? }`. The `.docx` ↔ `kind='document'` and `.dotx` ↔ `kind='template'` mapping is inferred from the extension and MIME (`application/vnd.openxmlformats-officedocument.wordprocessingml.document` vs `…wordprocessingml.template`). `kind` in the body is optional and only used when extension/MIME is ambiguous. Authorized via `Folder.CreateDocument` on the target folder (or project-root equivalent).
2. **Parser**: `DocumentImportService.ParseAsync(Stream)` opens the OpenXML package and walks the body:
   - `w:p` (paragraph) → TipTap `paragraph` (with style → heading level mapping for `Heading1`..`Heading6`).
   - `w:r` (run) → text node + marks (`bold`, `italic`, `underline`, `strike`, `code` from styles).
   - `w:tbl` → TipTap `table` with `tableRow` / `tableCell` children.
   - `w:hyperlink` → `link` mark.
   - Lists (`w:numPr` referencing `numbering.xml`) → `bulletList` / `orderedList`.
   - Images (`w:drawing` referencing `media/`) → save to `./data/documents/images/{newDocumentId}/{guid}.{ext}`, emit `image` node with the local URL.
   - **Unsupported** (complex floats, content controls, embedded OLE objects, equations) → skip with a structured warning collected into the response; lossy round-trip is acceptable for v1.
3. **Persist**: insert `Document` row (`Kind = 'document' | 'template'`, `BodyJsonb = parsedTree`), insert initial `DocumentVersion` row (version 1, source-of-truth = "import"), copy images to disk. Return the new document id + a list of import warnings.
4. **UI**: "New" menu in folder view + template gallery surfaces "Import from .docx / .dotx" → file picker → progress modal → on completion, open the new document in the editor. Warnings show in a dismissible banner inside the editor on first open.

**Yjs note**: import populates `BodyJsonb` directly; the next Hocuspocus connect initializes the Y.Doc from this snapshot (existing behavior — no special handling needed).

**v2 (deferred)**: `POST /api/content/documents/{id}/import-merge` — same parser, but instead of creating a new row, the parsed tree is diffed against the current document body. Diff UI presents accept/reject per paragraph/block; accepted hunks are applied as a single Yjs transaction so collaborators see them as one atomic change. Out of scope for v1; the parser written here is the reusable foundation.

### 10. Critical files reference

**Existing files to modify**
- `src/AutoNate.Web/Services/Content/ProjectRole.cs` — add `Commenter`.
- `src/AutoNate.Web/Services/Content/EntityKinds.cs:36-39` — add `Folder`, `Document`.
- `src/AutoNate.Web/Services/Content/IContentAuthorizer.cs` — register ancestor chains for new kinds; verify role→action map covers `Comment` action.
- `src/AutoNate.Web/Persistence/AutoNateDbContext.cs:93-95` — register new DbSets.
- `src/AutoNate.Spa/src/routes/appRoutes.tsx:144` (alongside notes) — add document routes.
- `src/AutoNate.Spa/src/shell/AppShell.tsx` — route guard for minimal shell on `/documents/edit/*`.
- `src/AutoNate.Spa/src/shell/NavMenu.tsx` — top-level Documents item.
- `services/hocuspocus/src/auth.ts` — recognize `documents:` prefix, call .NET auth with new entity kind.
- `services/hocuspocus/src/webhook.ts` — handle document-prefix snapshots.

**Existing utilities to reuse**
- `IContentAuthorizer` for all permission gating — never bypass it.
- `useProjects` hook in `src/AutoNate.Spa/src/hooks/useContent.ts` for the project picker.
- `RequirePermission` endpoint filter (see `NoteEndpoints.cs:40`).
- `DataPaths.cs` for `./data/documents/*` resolution.
- `src/AutoNate.Spa/src/lib/yjs/` helpers for Hocuspocus provider creation.
- `AgentConversation` / `AgentMessage` entities for chat persistence.
- `PageContextRegistry` pattern (per `add-page-context-provider` skill) for chatbot context.
- `DataTable` (`src/components/data-table/DataTable.tsx`) for folder/document lists.
- AQL grammar + schema helpers (recent commits, `c4e53135`) for NL→AQL binding suggest.

**New files** (high-level — exact tree omitted for scan-ability)
- Server: 7 endpoint files (incl. `ContentDocumentImportEndpoints.cs`), 6 EF entities + migration, 4 services (`DocumentBindingResolver`, `DocumentRenderService`, `DocumentImportService`, `DocumentAgentService`).
- SPA: ~5 page components, ~11 component files (folder tree, breadcrumbs, editor wrapper, 4 binding node views, inline-assist, chat panel, insert-binding dialog, import-from-docx dialog), 1 API client (`documents.ts`), 1 hooks file (`useDocuments.ts`), 1 minimal shell (`DocumentEditorShell.tsx`).
- Hocuspocus: edits to `auth.ts` + `webhook.ts`.

### 11. Suggested rollout (PR-sized phases)

| # | Scope | Why this slice |
|---|---|---|
| 1 | `Commenter` role + EntityKinds + Folder entity + Folder CRUD + Folder tree UI on `/documents/p/:projectId` (no documents yet) | Smallest shippable: navigate folder structure, full auth. |
| 2 | `Document` entity + REST CRUD + version snapshots + minimal editor route showing read-only JSON | Documents exist and persist; no fancy editor yet. |
| 3 | docx-editor integration + Hocuspocus document prefix + collab editing | The editor lights up. |
| 4 | Comments + comment-only Hocuspocus mode | Comment role becomes useful. |
| 5 | Bindings (entity, endpoints, AQL resolver, in-editor nodes, refresh UX) | Live data in documents. |
| 6 | Templates (kind discriminator, gallery, "create from template" flow) | Template authoring + reuse. |
| 7 | DOCX + PDF export + export queue + download UI + **DOCX/DOTX import on create** (shares the OpenXML dep with export — implement both directions in the same PR) | Distribution outside the app, plus on-ramp from existing Word files. |
| 8 | AI: inline assist + chat-with-doc + NL→AQL bindings + generate-from-template | Full AI scope. |
| 9 | Polish: open-in-new-tab editor shell, "Refresh all bindings", history/restore UI, override-permission editor for folders/documents | Tightens the user-facing UX. |

(External link sharing and DOCX-into-existing-document merge/diff are intentionally not in the rollout — v2.)

### 12. Risks + open items

- **docx-editor maturity**: package may be alpha; pin a version, plan for forking. Validate licensing — confirm the agents package license is compatible (the BlockNote-side memory note `project_editor_stack.md` flagged the BlockNote `xl-ai` package as GPL/blocked; do the same diligence here).
- **TipTap version dedup**: BlockNote depends on TipTap; docx-editor depends on a (possibly different) TipTap. If dedup fails, the documents editor route must lazy-load and isolate.
- **Hocuspocus comment-only mode**: Hocuspocus has `readOnly` but no native "comment" tier in the Yjs doc itself. Solution: in comment-only mode, the editor is loaded `readOnly: true` (no Yjs writes), and the comment UI hits REST instead of writing to Y. Worth a short prototype.
- **Folder permission perf**: closest-ancestor resolution requires walking the parent chain. For deep nesting, materialize the ancestor path (`Folder.MaterializedPath` text column) or cache resolved permissions per `(userId, resourceId)` with invalidation on grant changes. Start simple (recursive CTE), add caching if hot.
- **Binding refresh + Yjs**: pushing a binding update server-side into the Y.Doc requires the server (or a server-driven Hocuspocus client) to write into the doc — easier route is to update the `DocumentBinding` row, broadcast a Hocuspocus awareness ping with the binding id, and have each connected editor refetch + re-render. Confirm during phase 5.
- **Export of bound content**: decide policy — does "export to PDF" trigger a binding refresh first, or snapshot the current cached values? Recommend a "refresh-before-export" toggle on the export dialog, defaulted on for `Publish`, off for `Quick download`.
- **Import fidelity**: DOCX → TipTap is lossy by definition (OpenXML has features TipTap has no concept of: content controls, complex floats, equations, embedded objects, revision tracking). v1 strategy is "best-effort, surface warnings, never silently drop." Build a small fixture suite of real-world `.docx` files (a clean Word doc, a Google-Docs export, a doc with images + tables + lists + headings) and assert what survives. Any file type that produces too many warnings should fail loud with a "this file uses features we don't support yet" error rather than half-import.
- **Memory note refresh**: `project_collab_foundation.md` currently says "decided, not built" — Hocuspocus is in fact built and running. Update post-plan-exit.

---

## Verification

End-to-end checks for each phase before merge:

1. **Backend** — `dotnet build && dotnet test` clean; apply EF migration to a dev DB and verify schema; `audit-authorization` skill confirms gating on every new endpoint.
2. **Hocuspocus** — `cd services/hocuspocus && npm test` (or equivalent); start the sidecar locally; check authorize hook logs as a viewer + editor + commenter try to connect.
3. **SPA build** — `npm run build` from `src/AutoNate.Spa/`; `tsc -b --force` (per `feedback_unused_ts_module_verification.md`) to surface any duplicate-TipTap dedup errors.
4. **Manual happy path** (per `run` skill, logging in as `admin/admin` per `reference_dev_login.md`):
   - Create a folder under a project; add a sub-folder; create a document in the sub-folder.
   - Open the editor in a new tab; type; refresh — content persists; second tab as a second user sees live edits.
   - Add a comment; assign a non-project user as commenter via the override editor; verify they can comment but not edit.
   - Create a template with an AQL table binding; create a document from the template; refresh the binding; verify cached value persists in Yjs.
   - Click "AI inline assist", "Chat with this document", "/insert table of X" — all four AI features round-trip.
   - Export to PDF and DOCX; verify the files open in Acrobat / Word.
   - Import a `.docx` file → new document opens in editor with content + images + tables preserved; warnings banner reports any unsupported features. Import a `.dotx` → lands in the template gallery as a new template.
   - Round-trip a non-trivial doc: export current doc to .docx, re-import as a new doc, eyeball the diff (acceptable losses noted in `Import fidelity` risk).
   - Restore a prior version; verify content reverts.
5. **Playwright e2e** — covering the golden path above. Reuse the `verify` skill's Playwright patterns.
6. **Performance smoke** — a folder with 200 documents loads in under a second; a document with 20 bindings refreshes within a few seconds.
