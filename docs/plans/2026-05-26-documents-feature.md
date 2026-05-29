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
| `/documents/preview/:documentId` | `DocumentPreviewPage` (read-only, bindings fully resolved into inline content; see §9c) | **minimal shell — no NavMenu, no Footer, full-width** |

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

Bindings + export — **current state (post-Phase 5+7) vs target state**: in v1 the body stores `{{binding:UUID}}` as raw ProseMirror text and the bindings plugin paints chips over those nodes as decorations. Decorations are render-only — `docx-editor.save()` serializes the raw PM doc, so exported `.docx` files contain the placeholder *text*, not the resolved values. The "refresh-before-export" toggle still gates a binding refresh, but refresh only updates `DocumentBinding.LastResolvedValueJsonb` — it doesn't put the value into the body. The target shape (Phase 10) is to replace the text-plus-decoration model with first-class PM nodes (see §9e) so the resolved content lives *inside* the document tree and round-trips through OOXML automatically. As a near-term bridge before Phase 10 lands, see §9d for a resolve-before-save shim.

### 9b. DOCX / DOTX import (v1 — create-only)

`@eigenpal/docx-editor-react` exposes a `documentBuffer?: ArrayBuffer | Uint8Array | Blob | File` prop. Passing an uploaded `.docx` to that prop parses + renders it directly inside the editor — full OOXML fidelity for free, no custom server-side parser.

Phase 7 implementation:
1. **Upload endpoint**: `POST /api/content/documents/import` (multipart) — accepts `{ file, projectId, folderId?, title?, kind? }`. Server creates a `Document` row with empty `body_jsonb` + stashes the uploaded bytes to `./data/documents/imports/{documentId}.docx` (transient — discarded after first editor open commits the parsed JSON via the normal autosave path). Returns the new `documentId`. Authorized via `Folder.CreateDocument` / `Project.Edit`.
2. **Client flow**: SPA opens `/documents/edit/{id}?import=1`; the editor route fetches the uploaded buffer once, passes it as `documentBuffer={buffer}` AND `externalContent={false}` for the first mount (lets docx-editor parse it into its internal state). On first autosave (Hocuspocus webhook → `body_jsonb`), the import file is deleted; subsequent opens use the Yjs path normally.
3. **UI**: "New" menu in folder view + template gallery surfaces "Import from .docx / .dotx" → file picker → upload progress → open in editor.

**Why this is so much smaller than the original §9b plan**: the entire OpenXML parser is inside docx-editor's core. The original plan had us writing a `DocumentImportService.ParseAsync` walking `w:p` / `w:r` / `w:tbl` etc. — that's now eigenpal's problem, not ours.

**v2 (deferred)**: same idea as before — import a `.docx` *into* an existing document with diff/merge UI. docx-editor's suggesting mode plus a diff-against-current ProseMirror tree can produce the suggestions; v1 doesn't ship this.

### 9c. Read-only Preview (Phase 11)

A web preview surface that renders the document with every binding fully resolved into inline content. Same chrome-free shell the editor uses, but with no toolbar, no commenting, no edit affordances — purely a "what does this document look like when populated against current data" surface. Mirrors the role exported `.docx` files used to play before users realized exports also need resolved content (§9 / §9d).

**Route**: `/documents/preview/:documentId` — outside the AppShell, mounts a new `DocumentPreviewPage`. The editor's title-bar gains a sibling "Preview" link next to the "Download .docx" button so the user can flip between editing and seeing the populated output without leaving context. The preview page itself has a "Open in editor" link + "Refresh bindings" button (forces a server-side refresh-all before re-rendering) + the "Download .docx" button (same export pipeline, also resolved).

**Rendering pipeline** (assumes Phase 10 has shipped — see "Pre-Phase-10 fallback" below if it hasn't):
1. Fetch the document + the bindings list (each with its `LastResolvedValueJsonb`).
2. Mount `<DocxEditor>` with `document={pmJsonFromBody}`, `externalContent={false}` (no Yjs), `readOnly`, `mode='viewing'`, no `toolbarExtra`, no `agentPanel`.
3. With node-view bindings (Phase 10), the body already carries resolved-value nodes — the preview is just a read-only mount of the live doc. No transformation step needed.

**Pre-Phase-10 fallback** (if we want the preview before the node-view refactor lands):
- Add a client-side `resolveBindingsInPmDoc(pmJson, bindings) → pmJson` helper that walks the PM JSON and replaces `{{binding:UUID}}` text nodes with resolved content (an inline text run for `record-field`, a real `table` node for `aql-table`, an error chip for any binding that's unresolved or denied).
- Preview mounts the editor with the transformed JSON. Same helper feeds the resolve-before-save export shim in §9d — write it once, share it across both surfaces.
- Cost: ~200–300 LoC of PM-node construction matching docx-editor's schema, plus a small set of unit tests over the transformer.

**Permissions**: gated by `Document.View` (same as the editor's read path). The bindings the preview pulls in are subject to the same `IDocumentBindingResolver` per-row authorization as the live editor's "Refresh all" action — a viewer with no access to the underlying records sees the binding's "denied" placeholder, not its cached value.

### 9d. Resolve-before-save export (interim bridge, optional)

If the preview ships before Phase 10 (via the §9c pre-Phase-10 fallback), the same `resolveBindingsInPmDoc` helper plugs into the editor's existing `Download .docx` button to give users a populated export *today*:

1. Snapshot the current PM doc (so we can roll back after save).
2. Dispatch a single PM transaction that replaces every `{{binding:UUID}}` text node with the resolved content (via the helper).
3. Call `editorRef.current.save()` → `Blob` → trigger download (existing path).
4. Dispatch a reverse transaction to restore the original placeholder text — collaborators on the same Yjs doc don't see a frozen snapshot replace their live bindings.

This is a tactical shim, not a long-term shape. Phase 10 deletes both the snapshot/restore dance and the helper itself: with node-view bindings, the body already holds resolved content (or the node view renders from the registry on the fly), so `save()` produces correct OOXML on the first try.

### 9e. Binding node-view refactor (Phase 10 — long-term fix)

The Phase 5 bindings plugin paints chips as ProseMirror **decorations** over `{{binding:UUID}}` text. Decorations are visual-only — they don't affect the underlying doc, and serialization (OOXML export, ProseMirror JSON snapshots, RAG embedding pipelines) sees the raw placeholder text. Two user-visible consequences:

1. Exported `.docx` files contain the literal `{{binding:UUID}}` string, not the resolved value (§9 / current "Bindings + export" note).
2. docx-editor's paged renderer materializes text from the doc model and bypasses inline decorations entirely — the chip and the placeholder text both render side-by-side in the editor view.

**Target shape**: replace the text + decoration model with **first-class ProseMirror nodes**, one per binding kind:

- `record_field_binding` — inline node (`group: "inline"`, `inline: true`, `atom: true`), `attrs: { bindingId }`, renders the resolved scalar value inline. NodeSpec includes `toDOM` for HTML rendering + `parseDOM` for round-trip. In OOXML, serialize as a `<w:r>` run containing the resolved text — no special token, just normal Word content.
- `aql_table_binding` — block node (`group: "block"`), `attrs: { bindingId }`, `content: "table_row+"` or absorbs the resolved rows directly via a custom NodeView that mounts the table content. NodeSpec serializes through docx-editor's standard `table` shape so Word can read it natively.
- Both nodes carry their own NodeView that subscribes to the bindings registry, so a "Refresh all" or a server-driven binding update re-renders only the affected node instances (no full editor reflow).

**Migration**:
- Schema bump: add the two node types to docx-editor's schema via the `externalPlugins` channel (it accepts custom nodes through a schema extension — confirm exact API in their docs; if unsupported, the alternative is a docx-editor fork or upstream contribution).
- Body migration job: walk every existing document's `body_jsonb`, replace each `{{binding:UUID}}` text node with the matching node-view JSON. Idempotent — re-running over an already-migrated doc is a no-op. Sidecar's `trySeedFromBodyMirror` keeps working because it's schema-agnostic.
- Delete `bindingsPlugin.ts` + `bindingPlaceholderText` helper + the regex scanner. Side panel's "Insert at cursor" dispatches a node insertion (`tr.replaceSelectionWith(newNode)`) instead of text insertion.
- Delete the `resolveBindingsInPmDoc` shim from §9c/§9d if it exists by then — `save()` produces correct output on the first try.

**Why this isn't in Phase 5**: shipping decorations was a deliberate v1 shortcut — node views require schema cooperation from docx-editor (it owns the schema), and Phase 5's goal was "make bindings work in the editor view", not "make bindings round-trip through every output format." With Phases 5-9 shipped, the cost of the refactor is well-bounded and the requirements are pinned by real usage (preview + export both want resolved nodes in the tree).

**Spike verdict (2026-05-28)** — the original "add two custom node types" plan is **superseded**; we don't need schema extension at all:

- docx-editor exposes **no** schema-extension / nodeViews prop. You cannot add node types after the schema is built. So inventing `record_field_binding` / `aql_table_binding` is a non-starter without forking.
- BUT the schema **already ships a `field` node** (Word field primitive): inline, atom, attrs `{ fieldType, instruction, displayText, fieldKind, fldLock, dirty }`. Verified round-trip: `toDOM` → `<span class="docx-field" data-instruction="…">value</span>`, `parseDOM` → identical attrs. It's docx-editor's native Word-field representation, so it serializes to OOXML `<w:fldSimple>`/`<w:instrText>` for free.
- **record-field bindings → `field` node**: GO. Put the binding id in `instruction` (e.g. `AUTONATE_BINDING <uuid>`), the resolved value in `displayText`. Renders the value inline (no chip-beside-placeholder), exports + RAG-serializes as real text, refresh = update the matching field's `displayText`.
- **aql-table bindings → NO clean node**: `sdt` (content control) is `content: "inline*"` — inline only, can't wrap a block table; there is no block-level content-control node. Approach: insert the resolved `table` node directly (round-trips natively as a real Word table) and track the binding↔table association out-of-band — e.g. a leading inline `field` marker (`AUTONATE_TABLE_BINDING <uuid>`) immediately before the table; refresh locates the marker and replaces the following table.
- One residual unknown to confirm during implementation, not a blocker: that a real `save()` → `.docx` preserves the custom `instruction` text (Word field instructions are arbitrary, so expected to survive).

**Refined Phase 10 split**:
- **10a (inline)**: migrate `record-field` bindings from `{{binding:UUID}}` text+decoration → `field` nodes. Update the bindings side panel's insert path, the resolver/refresh path (set `displayText`), and the body-migration job (text placeholder → field node). Delete the inline half of `bindingsPlugin.ts`. This kills the chip-beside-text render bug AND the export-shows-raw-token bug for the common case.
- **10b (block)**: `aql-table` bindings via direct table insertion + `field` marker. Bigger; can land as a follow-up.

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
| 4 | Comments + comment-only mode | ✅ Shipped. Threaded comments wired end-to-end: `ContentDocumentCommentEndpoints.cs` (create/reply/resolve/reopen require `Actions.Comment`; list requires `View`) + `useDocumentComments` + docx-editor's controlled `comments`/`onCommentsChange` in `DocxDocumentEditor`. Comment-only gating: backend mints role `commenter` for Comment-but-not-Edit users; SPA sets `mode='viewing'`; **the Hocuspocus sidecar now enforces `connection.readOnly` for every non-editor role** (was only `viewer` — fixed 2026-05-28, see §11b #1) so the body is locked server-side while REST-based comments stay live. Covered by `DocumentCommentAuthorizerTests` + `FolderAuthorizerTests`. |
| 5 | Bindings (entity, endpoints, AQL resolver, in-editor ProseMirror plugins, refresh UX) | ✅ Shipped (record-field + aql-table kinds, decoration plugin, side panel; known v1 limitations tracked for **Phase 10**: docx-editor's page renderer bypasses inline decorations so chips render next to the placeholder text in-editor, AND export / RAG serialization sees the raw `{{binding:UUID}}` text instead of the resolved value). |
| 6 | Templates (kind discriminator, gallery, "create from template" flow) | ✅ Shipped (clone endpoint copies body + bindings with fresh ids, rewrites `{{binding:UUID}}` placeholders, snapshots an initial version; cross-project clones rejected; SPA `TemplateGalleryPage` at `/documents/templates` for cross-project listing + use/edit/rename/delete; templates filtered out of regular folder grids; sidecar `trySeedFromBodyMirror` extended to seed Yjs from `documents.body_jsonb` on cold load; 4 backend tests passing). |
| 7 | **DOCX/DOTX import + export** — both directions ride docx-editor's built-in OOXML round-trip (`documentBuffer` prop for import, `ref.current.save()` for export). No OpenXML SDK pipeline needed; the entire §9 / §9b plan slimmed dramatically. | ✅ Shipped. Export: title-bar `Download .docx` button (and docx-editor's File→Save menu) call `editorRef.current.save()` and trigger a browser download. Import: `POST /api/content/documents/import` (multipart) auto-routes `.docx` → `Document` and `.dotx` → `Template` based on file extension, stashes bytes to `data/document-imports/{id}.docx` via `IDocumentImportStorage`, sniffs OOXML container before persisting. Editor route reads `?import=1`, fetches the stash via `GET /{id}/import-buffer`, mounts in single-user **import mode** (`externalContent=false`, `documentBuffer={buf}`, no `ySyncPlugin`) so docx-editor parses OOXML directly; after a 500 ms debounce the editor extracts `view.state.doc.toJSON()`, PATCHes it into `body_jsonb`, DELETEs the stash, and navigates without `?import=1` so the next mount uses the standard Yjs path (sidecar's `trySeedFromBodyMirror` seeds the Y.Doc from the freshly-written `body_jsonb` mirror). `ImportDocxButton` wired into folder views (`.docx`) and the template gallery (`.dotx`). 5 new backend tests, 47 doc-adjacent tests still green. |
| 8 | AI via docx-editor's `agentPanel` slot wired to our `/api/agent/documents/*` endpoints. Inline assist piggybacks on suggesting mode (AI proposals = tracked-change suggestions). | 🚧 v1 shipped (doc-scoped chat). New `DocumentChatPanel` mounts inside docx-editor's `agentPanel` slot — reuses existing `/api/agent/conversations/*` + SSE streaming, per-document `pageKey=document:UUID` so threads scope to the open doc. `useDocumentEditorPageContext` registers a PageContextProvider so the agent's `inspect_page` skill sees the doc's title + bindings catalog + on-demand body text via the live EditorView. Local `PageContextRegistryProvider` mounted inside `DocumentEditorPage` because the editor route lives outside the AppShell. Verified end-to-end: chat answers "what's the title?" correctly using the page context, conversation persists across reloads. **8b shipped**: inline-assist via tracked-change suggestions. Page-context snapshot now surfaces the user's live selection (`data.selection` = { paraId, selectedText, before, after }); new `suggest_text_replacement` action ({ paraId, search, replaceWith }, with oldText/newText/find/replacement aliases tolerated) routes through docx-editor's `proposeChange` ref → renders as a Word-style tracked change (strikethrough old + underline new) the user accepts/rejects in the review UI. Verified end-to-end: agent proposed a replacement, PM model showed paired insertion+deletion marks. Note: the server-side `apply_page_action` skill still enforces its describe-then-confirm gate, so a suggestion takes one "yes" turn before applying — acceptable since the tracked change is itself reviewable. **Remaining follow-ups 8c–8d**: NL→AQL binding suggest dialog, and template+prompt → generate-document. |
| 9 | Polish: full-bleed editor shell (already in place — `/documents/edit/:id` mounts outside AppShell), "Refresh all bindings", version-history sidebar, override-permission editor for folders/documents. | ✅ Shipped (2026-05-28). Full-bleed shell + refresh-all bindings were already in. **Version-history sidebar** (list + read-only view via `/documents/preview/:id?version=N`; restore deferred). **Override-permission editor** — resource-owner self-service grants on documents + folders (`ContentPermissionOverrideEndpoints`, gated by `Document.Edit`/`Folder.Edit`, escalation-clamped) with a `PermissionsDialog` wired into the editor title bar + folder-tree menu. See §11b #3. (Note: docx-editor's only "history" hook is in-session undo/redo, not doc versions — the sidebar is our own against the REST versions API.) |
| 10 | **Binding node-view refactor** (§9e). Replace the text-placeholder + decoration model with first-class PM nodes so the resolved content lives inside the document tree and round-trips through every output format (DOCX export, RAG embedding, search). | ✅ Shipped. **10a** (record-field bindings). Spike found docx-editor allows no schema extension but already ships a `field` node (Word field primitive) — repurposed it instead of inventing a node type. `bindingFieldNode.ts`: builds/parses `field` nodes (`instruction = AUTONATE_BINDING <uuid>`, `displayText = resolved value`) + `syncRecordFieldNodes` (one idempotent pass that migrates legacy `{{binding:UUID}}` text → field node AND refreshes stale `displayText`). Insert path emits a field node for record-field bindings; aql-table keeps the text+decoration path (10b). Sync runs on `bindingRows` change + on editor-view-ready (handles either arrival order); editor-role only; skipped in import mode; transactions marked direct-edit + `addToHistory:false`. Sidecar `jsonNodeToYjs` now copies node attrs so field nodes survive cold-load seeding (needs sidecar restart). Verified in browser: legacy placeholder auto-migrated to field node on open, body renders just the value (no chip, no raw token), and the PM tree that `save()`/RAG serialize carries the resolved value instead of `{{binding:UUID}}`. **10b shipped**: aql-table bindings render as real Word tables. `bindingTableNode.ts`: `buildAqlTableNode` (styled table — header row from columns, body from rows, half-pt gray borders + light-gray bold header, matching the markdown-table styling), `buildAqlTableBlocks` (a caption-marker paragraph + the table), `insertAqlTableBinding` (inserts the block pair after the cursor's top-level block — block content can't live inside a paragraph), and `syncAqlTableNodes` (migrate legacy inline placeholders + refresh-in-place). The marker is a `field` node whose instruction is pipe-delimited `AUTONATE_TABLE_BINDING|<uuid>|<resolvedAtUtc>` and whose displayText is the binding label (visible caption); refresh compares the stored timestamp to `binding.lastResolvedAtUtc`, and on mismatch replaces the following table + bumps the marker. Both sync passes (table block-level first, then record-field inline) run via `runBindingSync` on bindings-change + view-ready, marked direct-edit + `addToHistory:false`. **Cleanup**: deleted `bindingsPlugin.ts` + `bindingsRegistry.ts`, removed the decoration plugin from `externalPlugins`, dropped the registry effect — no binding kind uses text+decoration anymore. Verified in browser: insert renders a real 4×26 table (no chip, no raw token); export tree carries the `table` node not `{{binding:UUID}}`; refresh advanced the marker timestamp + replaced the table in place (count stayed 1, no duplication). Phase 10 complete. |
| 11 | **Read-only preview surface** (§9c). New `/documents/preview/:documentId` route mounting docx-editor in read-only viewing mode with bindings fully resolved into inline content. Editor's title bar gains a "Preview" sibling next to "Download .docx" for one-click flip from authoring to populated view. | ✅ Shipped (2026-05-28). `previewMode` prop on `DocxDocumentEditor` (read-only viewing, no toolbar/AI/bindings chrome, binding sync skipped); `DocumentPreviewPage` + `/documents/preview/:documentId` route (full-bleed, outside AppShell) with its own slim header (PREVIEW badge + title + Back-to-project + Edit) since docx-editor hides its title bar when the toolbar is off; "Preview" button added to the editor title bar. Shipped post-Phase-10, so the `resolveBindingsInPmDoc` helper (§9c/§9d) was never needed. Also hardened the dispatch wrapper against torn-down views (fixes a transient y-prosemirror `matchesNode` error on the preview↔editor remount). See §11b #2. |

(External link sharing and DOCX-into-existing-document merge/diff are intentionally not in the rollout — v2.)

### 11b. Remaining work — recommended order (as of 2026-05-28)

Phases 1–3, 5–7, and 10 (10a + 10b) are shipped, plus the binding **edit dialog**, **hover-highlight**, and **click-to-navigate** enhancements added on top of Phase 10. What's left, in the order I'd tackle it and why:

0. **Housekeeping — drop stale pivot files.** `git rm --cached` the three staged-then-deleted leftovers from the TipTap→docx-editor pivot (`DocumentEditor.css`, `DocumentEditor.tsx`, `DocumentEditorToolbar.tsx`). Trivial; do it first so the tree is clean before new work lands.

1. ~~**Finish Phase 4 — comment-only mode gating.**~~ ✅ **Done (2026-05-28).** Audited the full path: backend mints role `commenter` for documents where the user has Comment-but-not-Edit (`YjsEndpoints` ticket mint + `/internal/yjs-auth` re-check), comment REST endpoints require `Actions.Comment`, and the SPA puts commenters in docx-editor `mode='viewing'`. The backend role contract is covered by `DocumentCommentAuthorizerTests` + `FolderAuthorizerTests`. **Found + fixed a server-enforcement gap**: the Hocuspocus sidecar (`services/hocuspocus/src/auth.ts`) only set `connectionConfig.readOnly` for role `viewer`, so a commenter's Y.Doc body connection was *not* read-only server-side (client `mode='viewing'` was the only thing stopping edits). Changed to fail-closed — `readOnly` for any role that isn't `editor`. Comments are unaffected (they ride REST, not the Y.Doc). Sidecar `tsc` builds clean. **Deploy note**: the sidecar runs in Docker (port 1234), so the running container must be rebuilt/restarted to pick this up. (Decision context in §12 "Comment-only mode".)

2. ~~**Phase 11 — read-only preview.**~~ ✅ **Done (2026-05-28).** As predicted, Phase 10 made it cheap — no `resolveBindingsInPmDoc` helper needed; the resolved values already live in the tree, so preview is a plain read-only mount. Added a `previewMode` prop to `DocxDocumentEditor` (forces `readOnly` + `mode='viewing'`, hides toolbar/ruler/zoom/AI-panel/bindings-panel, and **skips binding sync so preview never mutates the doc**), a `DocumentPreviewPage` at `/documents/preview/:documentId` (outside the AppShell, full-bleed), and a "Preview" button in the editor title bar (beside "Download .docx"). docx-editor hides its own title bar when the toolbar is off, so the preview page renders its own slim header (PREVIEW badge + title + Back-to-project + Edit). Also hardened the dispatch wrapper to no-op on a torn-down view (`docView == null`), fixing a transient `matchesNode`-of-null thrown by y-prosemirror's awareness observer during the preview↔editor remount. Verified in browser: populated table renders read-only, no editable surface, clean round-trip both ways with zero console errors.

3. **Phase 9 — polish.** Independent, medium value.
   - ✅ **Version-history sidebar (list + view) — done (2026-05-28).** New `VersionHistorySidePanel` lists every snapshot (number, kind badge, author, time, note) from the existing versions API; it shares the right rail with the bindings panel via a single `activePanel` state (mutually exclusive, toggled from `toolbarExtra`). "View" opens the version read-only in the preview surface at `/documents/preview/:id?version=N` — `DocumentVersionView` rebuilds the stored PM JSON against docx-editor's own `schema` and converts it with the library's `fromProseDoc`, then mounts a standalone read-only editor (no Yjs). Empty snapshots (e.g. the auto "Initial version" that stores `{}`) render as a blank page. (Note: docx-editor's only "history" hook is in-session undo/redo, not document versions — the sidebar is fully our own against the REST API.) **Restore is deferred** per the v1 decision (live-apply into the Yjs doc is the non-trivial part).
   - ✅ **Folder/document override-permission editor — done (2026-05-28), resource-owner self-service.** New `ContentPermissionOverrideEndpoints` exposes GET/POST/DELETE under `/api/content/{documents|folders}/{id}/permissions`, gated by `Document.Edit` / `Folder.Edit` (not site-admin). It reuses the same `IPermissionGrantStore` as the admin Grants page but clamps it so an editor can't escalate: the selector is **forced** to `/{kind}/{id}`, effect is **forced** to `allow` (denies stay admin-only), the action must be in a per-kind allowlist (docs: view/comment/edit; folders: view/edit/create — no delete/archive) **and** the caller must already hold that action on the resource (re-checked via `IContentAuthorizer.AuthorizeAsync`); list/delete only see/touch grants whose selector matches the resource. Frontend: `api/resourcePermissions.ts` + `useResourcePermissions` hooks + a kind-agnostic `PermissionsDialog` (lists current overrides with revoke, plus an add form: principal-kind → user/group/role picker → action), wired into the document editor title bar ("Permissions", editor-role only) and the folder-tree context menu. Folder grants cascade to descendants via `content_ancestors`. **Verified**: backend builds, SPA typechecks, the Permissions button + dialog render in-browser. Full create/list/delete round-trip needs the Rider-managed backend restarted to pick up the new routes (currently 404 against the pre-build process).

4. ~~**Phase 10c — text-binding control polish.**~~ ✅ **Done (2026-05-28), cleanup only — no visual change.** Investigated and deliberately scoped to nothing visual. Findings: bound content already renders acceptably (record-field value reads inline as normal text; aql-table shows a caption line above the table) and the binding `field` nodes are already atomic (docx-editor sets `user-select:none` + a faint outline on `.docx-field`). Critically, that `.docx-field` element with its outline is the **off-screen editable copy** — the *visible* content is the paged `.layout-*` projection, which carries no field class or binding marker, so there's no CSS hook to shade visible bindings. Making bindings visually distinct would require either baking a highlight mark into the content (which would then persist in the exported `.docx`) or a persistent paged-overlay per binding (like the hover-highlight, but always-on and busy) — both disproportionate for "polish." Decided the current rendering is fine. Only change: removed the dead Phase 5 `.doc-binding*` decoration-chip CSS (obsolete since the decoration plugin was deleted in Phase 10).

5. **Phase 8c — NL→AQL "suggest a binding" dialog.** Enhances the bindings UX; smaller and more self-contained than 8d.

6. **Phase 8d — template + prompt → generate-a-document.** Largest remaining AI piece; do last.

**Still deferred to v2** (not scheduled): external/anonymous link sharing, unifying folders with the notes cabinet/notebook tree, and importing a `.docx` into an *existing* document with diff/merge UI.

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
- **docx-editor schema extensibility (blocks Phase 10)**: the node-view refactor (§9e) needs to register two custom node types in docx-editor's ProseMirror schema. Confirm during Phase 10 scoping whether the library exposes a schema-extension API (additional `nodes` / `marks` via a prop or factory) or whether we need to fork / contribute upstream. Without an extension path, the alternative is a node-views-via-decorations approach (use `Decoration.widget` to render React node views over text placeholders) — that still bypasses OOXML serialization, so the export problem doesn't go away. The fallback there is the resolve-before-save shim (§9d) made permanent. Decide before committing to Phase 10.
- **Body-migration safety for Phase 10**: rewriting every existing document's `body_jsonb` to swap text placeholders for node-view JSON needs to be transactionally safe AND idempotent (so re-runs are no-ops). Run it as a one-shot CLI / admin endpoint, not an EF migration — JSONB rewrites in-place can race with live Hocuspocus snapshots otherwise. Confirm Hocuspocus debounce window vs migration scan timing during Phase 10.
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
   - **Preview (Phase 11)**: open `/documents/preview/:id` for a doc with both `record-field` and `aql-table` bindings — page renders read-only with each binding replaced by its resolved value (inline text for record-field, real table for aql-table). "Refresh bindings" in the preview header forces a server-side refresh-all and re-renders. "Open in editor" returns to the live editor; placeholders show their chip/text again.
   - **Populated export (Phase 10 acceptance)**: with a doc containing both binding kinds, click "Download .docx" — open in Word and confirm the file contains resolved values (no `{{binding:UUID}}` strings anywhere in the body). Repeat after the binding configuration is changed to verify the next export reflects the new values, not a stale cache.
   - Import a `.docx` file → new document opens in editor with content + images + tables preserved; warnings banner reports any unsupported features. Import a `.dotx` → lands in the template gallery as a new template.
   - Round-trip a non-trivial doc: export current doc to .docx, re-import as a new doc, eyeball the diff (acceptable losses noted in `Import fidelity` risk).
   - Restore a prior version; verify content reverts.
5. **Playwright e2e** — covering the golden path above. Reuse the `verify` skill's Playwright patterns.
6. **Performance smoke** — a folder with 200 documents loads in under a second; a document with 20 bindings refreshes within a few seconds.
