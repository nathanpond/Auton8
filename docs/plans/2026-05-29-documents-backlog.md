# Documents — remaining backlog

> Carve-out from `2026-05-26-documents-feature.md`, which is essentially shipped
> (Phases 1–7, 9, 10, 11 done; Phase 8 done through 8a/8b/8c). This file captures
> the few items left so the main plan can be put to bed. Nothing here is in
> flight. See the original plan for full architecture/context.
>
> Status as of 2026-05-29.

---

## 1. Phase 8d — Template + prompt → generate a document

**What**: From a template (or blank) plus a natural-language prompt, have the AI
generate a full document — draft the prose and fill the template's structure —
landing as a new document the user can then edit.

**Why it's last**: largest AI piece; benefits from everything else being stable.

**Building blocks already in place**:
- Templates (Phase 6): kind discriminator, gallery, clone-from-template flow that
  copies body + bindings with fresh ids (`/documents/templates`).
- Markdown → ProseMirror insertion: `markdownToPmNodes.ts` (styleId resolution,
  real tables, list handling) + the editor's `apply_page_action` (`append_markdown`,
  `insert_markdown_at_cursor`).
- One-shot LLM call pattern: `AqlSuggestionService` (resolve provider → constrained
  prompt → parse) is a clean template for a server-side generate call.
- Doc chat (`DocumentChatPanel`) + page-context plumbing if a conversational shape
  is preferred over a one-shot dialog.

**Sketch / open questions**:
- Surface: a "Generate" dialog (pick template + prompt) on the templates gallery or
  the New-document flow → create the document, then stream/insert generated content.
- One-shot vs streamed generation; how strongly the template constrains output
  (free draft vs fill-the-placeholders).
- Whether generated content can propose **bindings** (tie into 8c's NL→AQL) — e.g.
  "insert a table of all open cars" → an aql-table binding rather than static text.
- Cost/length controls; the existing context-overflow + web-fetch caps already help.

---

## 2. Version restore (deferred from Phase 9)

**What**: restore a historical version into the live document. The version-history
sidebar shipped as **list + read-only view**; restore was deliberately deferred.

**Why deferred**: the body lives in **Yjs** (live, via Hocuspocus). The backend
`POST /api/content/documents/{id}/versions/{n}/restore` overwrites the REST
`body_jsonb` mirror, but that mirror only seeds the Y.Doc on a **cold load** — so
restoring while anyone is connected is a no-op (and the warm Y.Doc would re-persist
the old content, clobbering the restore). Correct restore needs a **client-side
live-apply**.

**Building blocks already in place**:
- Backend restore endpoint exists and snapshots the current state before overwriting
  (every restore is itself a version).
- `useRestoreDocumentVersion` hook exists.
- `VersionHistorySidePanel` (list + View) shipped; `DocumentVersionView` already
  rebuilds a version's PM JSON → docx-editor `Document` (schema + `fromProseDoc`).

**Sketch**: add a "Restore" action to each version row (editor-role only, confirm
dialog) → call the backend restore → then **replace the live editor's document
content** from the restored snapshot via a ProseMirror transaction, so the change
flows through Yjs to all connected clients and persists. The doc-replacement
transaction is the non-trivial part (whole-doc replace under y-prosemirror;
mind tracked-changes/suggestion interactions and `addToHistory`).

---

## 3. v2 / parked (no commitment)

These were explicitly out of the v1 rollout and remain unscheduled:

- **Unify Documents folders with the notes cabinet/notebook tree** — today Documents
  has its own folder hierarchy separate from the notes cabinet/notebook structure.
- **Import a `.docx` into an *existing* document with diff/merge UI** — paragraph-level
  diff, accept/reject per chunk, applied as a Yjs transaction. docx-editor's
  suggesting mode + a diff-against-current PM tree can produce the tracked-change
  suggestions. (v1 import is create-only.)
- **Programmatic PDF export** — browser print-to-PDF works today via docx-editor's
  print flow; a headless-Chromium/Puppeteer server-side PDF is deferred until there's
  a concrete use case.

> (External / anonymous link sharing was removed from scope entirely — not wanted.)

---

## 4. Tangential — surfaced during 8c testing (not a Documents item)

**`Flows` "last activity" filter gap.** There's no filterable per-flow last-action
timestamp: `Flows` only exposes `StartDate`/`EndDate` as filterable dates, and
`CURRENTSTEP(...)` is projection-only (can't be used in `WHERE`). So "flows with any
activity in the last N weeks" can't be expressed per-flow — it only works at the
event level (`FROM WorkflowHistory WHERE EventTime >= -Nw`, grouped by `InstanceId`).
A `LastActivityDate` rollup column on `Flows` (max event time) would make the per-flow
filter possible. This is an AQL/query-entity enhancement, noted here only so it isn't
lost — it belongs with the query work, not Documents.
