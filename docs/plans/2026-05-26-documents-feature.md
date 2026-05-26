# Documents Feature — Architecture Plan

> **Status as of 2026-05-26**: Phases 1, 2, 3 shipped. Phase 3 pivoted mid-flight from a vanilla-TipTap editor to `@eigenpal/docx-editor-react` (ProseMirror-based) because the user wanted Word-grade DOCX round-trip, native tracked-changes / suggesting mode, and a built-in AI panel — features that would have been 10+ engineering weeks combined to build on TipTap. The pivot kept the entire backend stack (Hocuspocus, .NET auth/webhook, ContentVersionService, permissions) unchanged; only the SPA editor wrapper was swapped. Sections below have been refreshed to reflect the docx-editor reality where it materially changed the implementation; sections describing infrastructure already in place are left in their original (planning-tense) form for historical legibility.

## Context

AutoNate needs a Google-Docs-style **Documents** subsystem alongside the existing notes/pages feature. It shares projects with notes but has its own hierarchy (folders), its own editor (rich, DOCX-export-capable), comment workflow, live data bindings, document templates, and deep AI integration. The collab + auth foundations are already in place (Hocuspocus running on :1234 with Postgres persistence; `IContentAuthorizer` doing closest-ancestor override resolution; `AgentSidebar` + `/api/agent/*` for AI). The work is mostly **new feature surface area** that plugs into existing infrastructure, not a foundational refactor.

### Decisions (from user)
- **Editor**: `@eigenpal/docx-editor-react` (ProseMirror-based; their core handles OOXML parsing + paged layout). Mounted with `externalContent: true` + Yjs via `y-prosemirror`'s `ySyncPlugin` so our existing Hocuspocus sidecar drives content. Persist as ProseMirror JSON in `documents.body_jsonb` (docx-editor's richer schema, same JSON-in-Postgres storage model). DOCX is generated on demand via `ref.current.save()`; uploaded via the `documentBuffer` prop. ~~docx-editor (TipTap-based) as a dedicated documents editor.~~
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

### 6. Editor — `@eigenpal/docx-editor-react` wrapper (SHIPPED)

**Package**: `@eigenpal/docx-editor-react` (Apache-2.0, currently 1.0.3, actively maintained on `eigenpal/docx-editor`). It's built on ProseMirror primitives, not TipTap. Plays cleanly with Yjs because `externalContent: true` lets us swap its internal content layer for `y-prosemirror`'s `ySyncPlugin`.

**Implementation**: `src/AutoNate.Spa/src/components/documents/DocxDocumentEditor.tsx`. Lazy-loaded by `pages/documents/DocumentEditorPage.tsx` so the editor chunk doesn't enter bundles for other routes.

**Yjs wiring** — reuses the same `useYjsDocument(documents:{id})` hook BlockNote uses on the notes side, so the entire ticket / WS / awareness / IndexedDB-cache / reconnect-on-focus path is unchanged:
- Pass `externalContent: true` + a stable `createEmptyDocument()` schema seed (hoisted to module scope so each render doesn't rebuild the schema).
- `externalPlugins: [ySyncPlugin(doc.getXmlFragment("default")), yCursorPlugin(awareness, { cursorBuilder })]`.
- Fragment name is `"default"` to match the sidecar's `documentMaterializer` — keeps `body_jsonb` snapshotting working without a sidecar change.

**Role / readOnly gotcha (gotcha worth remembering)**: docx-editor latches the `readOnly` prop value at mount and ignores subsequent changes. Since `useYjsDocument` starts with the pessimistic `role="viewer"` (to avoid an editor flashing edit-capable before the ticket fetch confirms) and upgrades to `"editor"` after the ticket resolves, a naive consumer ends up with a permanently read-only editor. Fix: `<DocxEditor key={role} … />` remounts when the role flips; the `useMemo` for `externalPlugins` includes `role` in its deps so y-prosemirror plugins re-bind to the freshly created EditorView. Yjs state survives the remount because the Y.Doc + provider live in the parent's `useYjsDocument` scope.

**Tailwind preflight gotcha**: docx-editor's `styles.css` has zero `button` rules — it relies on the host app having Tailwind preflight (or Normalize.css) handle the browser `<button>` user-agent reset. AutoNate is Mantine-only with no global button reset, so the browser default `border: 2px outset Buttonface` leaked through and gave every toolbar button a chunky dark border. Fix lives in `DocxDocumentEditor.css`: a scoped reset under `.ep-root button { border: 0; … }` that re-allows the border when the library opts in via Tailwind utility classes (font picker dropdowns etc.).

**What we get out-of-box (no consumer code needed)**:
- Word-style toolbar (file/format/insert/help menus, font picker + size + color, B/I/U/S, alignment, lists, link/image/table/horizontal rule, clear formatting)
- Horizontal + vertical rulers
- Zoom control
- Document outline sidebar
- Editing / Suggesting / Viewing mode dropdown (**Suggesting = Word's tracked changes — author attribution + accept/reject sidebar**)
- Threaded comments anchored to text ranges
- Find & replace, print preview, page setup dialogs
- Editable document title in the title bar (we wire it through `documentName` + `onDocumentNameChange` to our REST `useUpdateDocument`)
- Right slot in the title bar for our "Back to project" link via `renderTitleBarRight`

**Custom ProseMirror plugins for bindings (Phase 5)** — same plan, different schema. Plugins live in `src/AutoNate.Spa/src/components/documents/editor/plugins/`:
- `AqlTablePlugin` — ProseMirror node spec for `aql-table` blocks; attrs `{ bindingId }`. NodeView renders `<AqlTableBindingView />` which reads cached value + refresh button.
- `ChartPlugin`, `RecordFieldPlugin`, `WorkflowDataPlugin` — same pattern.

Bindings are registered via docx-editor's plugin host (`pluginSidebarItems` + `externalPlugins` extensions). Toolbar adds a "Refresh all bindings" entry via `toolbarExtra`.

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

Reuse existing `/api/agent/*` infra on the server side. Use docx-editor's built-in `agentPanel` prop as the chrome on the client side — it ships the panel shell (header, resize handle, close button, localStorage width-persistence); we supply the chat UI body via `render`.

Server services + endpoints (unchanged from the plan):
- `src/AutoNate.Web/Services/Agent/DocumentAgentService.cs` — wraps the LLM client with document-scoped helpers.
- Endpoints (new file `AgentDocumentEndpoints.cs`):
  - `POST /api/agent/documents/{id}/inline-assist` — `{ prompt, selectionText, surroundingContext }` → streamed completion. Authorized via `Document.Edit`.
  - `POST /api/agent/documents/{id}/chat` — Q&A over the document's content. Authorized via `Document.View`. Reuses `AgentConversation` so chat history persists; uses a `documentId` scope on conversations.
  - `POST /api/agent/documents/{id}/bindings/suggest` — `{ naturalLanguage }` → suggested `{ kind, configJsonb }`. Authorized via `Document.Edit`.
  - `POST /api/agent/documents/templates/{templateId}/generate-document` — creates a doc from a template + prompt.

Client integration:
- `agentPanel={{ render: ({ close }) => <DocumentChatPanel documentId={id} onClose={close} /> }}` — `DocumentChatPanel` wraps our existing `AgentConversation` API so chat history persists alongside record-context chats. Register a `PageContextProvider` for the editor route (per `add-page-context-provider` skill) so the agent sees the open doc's state.
- **Inline assist via suggesting mode**: this is the killer combination. docx-editor's agents package supports proposing edits AS tracked-change suggestions instead of direct edits — the user reviews them in the existing accept/reject sidebar. No custom diff UI needed.
- Binding insert dialog (`InsertBindingDialog.tsx`) — NL textarea → calls `/bindings/suggest` → preview the suggested table/chart → confirm to insert.
- Template gallery has "Generate document from prompt" button.

### 9. DOCX export (mostly client-side via docx-editor)

`@eigenpal/docx-editor-react` ships full OOXML round-trip in its `core` package — we do **not** need a server-side OpenXML SDK pipeline. The editor's imperative ref exposes `save(options)` which returns an `ArrayBuffer` of the serialized `.docx` file:

```ts
const buffer = await editorRef.current?.save();
```

Phase 7 implementation:
- "File → Download as DOCX" toolbar action calls `editorRef.current.save()`, wraps the ArrayBuffer in a Blob, and triggers a browser download. No server round-trip required for export.
- For server-rendered exports (e.g. an admin batch export, a scheduled report, signed download links), we can optionally POST the buffer to `POST /api/content/documents/{id}/export?format=docx` which stores it at `./data/documents/exports/{documentId}/{exportId}.docx` and emits a `DocumentExport` row for downloadable, audited delivery. Decide later whether this server path is needed.
- **PDF**: docx-editor includes a `print` flow + `PrintPreview` dialog → browser's native print-to-PDF works without any server-side renderer. For programmatic PDF, we'd still need headless Chromium / Puppeteer (deferred until there's a real use case).

Bindings + export: the materialized binding values live in the JSON tree at export time, so they round-trip into DOCX automatically. The "refresh-before-export" toggle still matters; it just gates a binding refresh before the user clicks Download.

### 9b. DOCX / DOTX import (v1 — create-only)

`@eigenpal/docx-editor-react` exposes a `documentBuffer?: ArrayBuffer | Uint8Array | Blob | File` prop. Passing an uploaded `.docx` to that prop parses + renders it directly inside the editor — full OOXML fidelity for free, no custom server-side parser.

Phase 7 implementation:
1. **Upload endpoint**: `POST /api/content/documents/import` (multipart) — accepts `{ file, projectId, folderId?, title?, kind? }`. Server creates a `Document` row with empty `body_jsonb` + stashes the uploaded bytes to `./data/documents/imports/{documentId}.docx` (transient — discarded after first editor open commits the parsed JSON via the normal autosave path). Returns the new `documentId`. Authorized via `Folder.CreateDocument` / `Project.Edit`.
2. **Client flow**: SPA opens `/documents/edit/{id}?import=1`; the editor route fetches the uploaded buffer once, passes it as `documentBuffer={buffer}` AND `externalContent={false}` for the first mount (lets docx-editor parse it into its internal state). On first autosave (Hocuspocus webhook → `body_jsonb`), the import file is deleted; subsequent opens use the Yjs path normally.
3. **UI**: "New" menu in folder view + template gallery surfaces "Import from .docx / .dotx" → file picker → upload progress → open in editor.

**Why this is so much smaller than the original §9b plan**: the entire OpenXML parser is inside docx-editor's core. The original plan had us writing a `DocumentImportService.ParseAsync` walking `w:p` / `w:r` / `w:tbl` etc. — that's now eigenpal's problem, not ours.

**v2 (deferred)**: same idea as before — import a `.docx` *into* an existing document with diff/merge UI. docx-editor's suggesting mode plus a diff-against-current ProseMirror tree can produce the suggestions; v1 doesn't ship this.

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

| # | Scope | Status |
|---|---|---|
| 1 | `Commenter` role + EntityKinds + Folder entity + Folder CRUD + Folder tree UI on `/documents/p/:projectId` (no documents yet) | ✅ Shipped (5 new auth tests; 1275 → 1275 passing) |
| 2 | `Document` entity + REST CRUD + version snapshots + minimal editor route showing read-only JSON | ✅ Shipped (5 new doc tests + 1 version-restore happy-path; 1275 → 1280 passing) |
| 3 | docx-editor integration + Hocuspocus document prefix + collab editing | ✅ Shipped (pivoted from TipTap to `@eigenpal/docx-editor-react` mid-phase; backend tests unchanged; verified end-to-end in browser: empty cold-load doc, role-aware readOnly via `key={role}` remount, Yjs round-trip with v-counter bump, tracked-changes mode toggle visible in toolbar) |
| 4 | Comments + comment-only mode | Next up. docx-editor ships threaded comments + a comments-sidebar toggle out of box; we wire its controlled `comments={[]}` + `onCommentsChange` to a Yjs sync channel (or REST — TBD) and gate Commenter-role users to suggesting/viewing mode. |
| 5 | Bindings (entity, endpoints, AQL resolver, in-editor ProseMirror plugins, refresh UX) | Pending. Plugins now ride docx-editor's `externalPlugins` channel instead of being TipTap extensions. |
| 6 | Templates (kind discriminator, gallery, "create from template" flow) | Pending. |
| 7 | **DOCX/DOTX import + export** — both directions ride docx-editor's built-in OOXML round-trip (`documentBuffer` prop for import, `ref.current.save()` for export). No OpenXML SDK pipeline needed; the entire §9 / §9b plan slimmed dramatically. | Pending. |
| 8 | AI via docx-editor's `agentPanel` slot wired to our `/api/agent/documents/*` endpoints. Inline assist piggybacks on suggesting mode (AI proposals = tracked-change suggestions). | Pending. |
| 9 | Polish: full-bleed editor shell (already in place — `/documents/edit/:id` mounts outside AppShell), "Refresh all bindings", version-history sidebar (docx-editor has hooks for this), override-permission editor for folders/documents. | Pending. |

(External link sharing and DOCX-into-existing-document merge/diff are intentionally not in the rollout — v2.)

### 12. Risks + open items

- ~~**docx-editor maturity**~~ → **Resolved**. `@eigenpal/docx-editor-react@1.0.3` is actively published (release this week), Apache-2.0, full-feature parity with the website claims. Track upstream releases; keep version pinned with `--save-exact` so a 1.x.y bump doesn't sneak in without review.
- ~~**TipTap version dedup**~~ → **Resolved by replacement**. docx-editor is ProseMirror-based, not TipTap-based — the dedup risk applied only to the prior plan. (The npm `overrides` block pinning `@tiptap/core@3.23.4` is still useful for BlockNote's transitive TipTap and stays in place.)
- **docx-editor `readOnly` prop is not reactive**: it latches at mount and ignores subsequent changes. Use `<DocxEditor key={role} … />` to force a remount on role transitions; include `role` in the `externalPlugins` useMemo deps so y-prosemirror plugins re-bind to the new EditorView. Documented in the new memory note `feedback_docx_editor_button_reset.md`'s sibling note.
- **Tailwind preflight missing in host app**: docx-editor's stylesheet assumes the consumer has a global `<button>` reset; AutoNate (Mantine-only) doesn't. Scoped fix via `.ep-root button { border: 0; … }` in `DocxDocumentEditor.css`. Suspect this class of issue first if anything else in the editor surface looks visually off.
- **Vite dep optimizer needs a force-rebuild after heavy ESM installs**: adding `@eigenpal/docx-editor-react` (1.3MB unpacked) wedged Vite's optimizer cache, returning 504s on the optimized chunks. Recovery: `rm -rf node_modules/.vite && npm run dev -- --force`.
- **Comment-only mode (Phase 4)**: docx-editor's mode dropdown is Editing / Suggesting / Viewing — no native "comment-only" tier. Two viable shapes: (a) `mode='viewing'` + still allow commenting via the controlled `comments` prop (matches our original plan literally), or (b) put Commenters in Suggesting mode (every edit becomes a tracked change Commenters can't merge themselves). Decide during Phase 4.
- **Folder permission perf**: closest-ancestor resolution requires walking the parent chain. For deep nesting, materialize the ancestor path or cache resolved permissions. Start simple (recursive CTE), add caching if hot.
- **Binding refresh + Yjs**: pushing a binding update server-side into the Y.Doc requires the server (or a server-driven Hocuspocus client) to write into the doc — easier route is to update the `DocumentBinding` row, broadcast a Hocuspocus awareness ping with the binding id, and have each connected editor refetch + re-render. Confirm during phase 5.
- **Export of bound content**: decide policy — does "export to PDF" trigger a binding refresh first, or snapshot the current cached values? Recommend a "refresh-before-export" toggle on the export dialog, defaulted on for `Publish`, off for `Quick download`.
- ~~**Import fidelity**~~ → Largely a non-issue now: docx-editor owns the OOXML parser, so import fidelity is whatever they ship. Their site promises "pixel-perfect OOXML rendering ... round-trips your .docx without quality loss." We may still want a fixture suite to catch regressions in their upstream releases, but we're no longer writing the parser.
- ~~**Memory note refresh**: `project_collab_foundation.md` currently says "decided, not built"~~ → Already done.

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
