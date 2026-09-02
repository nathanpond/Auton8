import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { DocxEditor, createEmptyDocument } from "@eigenpal/docx-editor-react";
import type { DocxEditorRef } from "@eigenpal/docx-editor-react";
import { ySyncPlugin, yCursorPlugin } from "y-prosemirror";
import type { EditorView } from "prosemirror-view";
import type { Transaction } from "prosemirror-state";
import { Box, Group, Button, ActionIcon, Tooltip } from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { useMe } from "@/hooks/useMe";
import {
  useCreateDocumentComment,
  useDeleteDocumentComment,
  useDocumentComments,
  useReplyToDocumentComment,
  useResolveDocumentComment
} from "@/hooks/useDocumentComments";
import { useDocumentBindings } from "@/hooks/useDocumentBindings";
import type { DocumentBindingDto } from "@/api/documentBindings";
import {
  buildRecordFieldNode,
  recordFieldDisplayText,
  syncRecordFieldNodes
} from "./bindingFieldNode";
import {
  insertAqlTableBinding,
  syncAqlTableNodes
} from "./bindingTableNode";
import BindingsSidePanel from "./BindingsSidePanel";
import VersionHistorySidePanel from "./VersionHistorySidePanel";
import PermissionsDialog from "./PermissionsDialog";
import {
  bindingHighlightPlugin,
  setHoveredBinding,
  scrollToBinding
} from "./bindingHighlightPlugin";
import DocumentChatPanel from "./DocumentChatPanel";
import {
  useDocumentEditorPageContext,
  SELECTION_CONTEXT_LIMIT
} from "./useDocumentEditorPageContext";
import { markdownToProseMirrorSlice } from "./markdownToPmNodes";
import "@eigenpal/docx-editor-react/styles.css";
// Local override file MUST be imported AFTER the library's stylesheet so
// our scoped button reset wins the cascade.
import "./DocxDocumentEditor.css";

// Phase 3 (post-switch) document editor. We swapped vanilla TipTap for
// @eigenpal/docx-editor-react to get DOCX round-trip, tracked changes
// ("suggesting" mode), and the agent-panel chrome for AI — three features
// that would have been multi-week investments to build on bare TipTap.
//
// The editor is mounted with `externalContent: true` so its ProseMirror
// state is driven entirely by Yjs via y-prosemirror's `ySyncPlugin`. The
// Y.Doc + HocuspocusProvider come from our existing `useYjsDocument`
// hook (same plumbing used by BlockNote on the notes side), so .NET's
// ticket/auth/webhook flow is unchanged.
//
// Fragment name is "default" so the sidecar's `documentMaterializer`
// (services/hocuspocus/src/materializers.ts) keeps reading the same
// XmlFragment without any change.

type Props = {
  documentId: string;
  // Title shown in docx-editor's own title bar. Editing it triggers the
  // server-side rename callback so the change persists through the
  // documents REST endpoint, not through Yjs.
  documentTitle: string;
  onRenameDocument?: (newTitle: string) => void;
  // Anything to render in docx-editor's title bar's right slot — we
  // pass the "Back to project" link here so the title bar carries both
  // the doc name and the breadcrumb-style nav target.
  titleBarRight?: ReactNode;
  // Server-decided role — comes from the Yjs ticket. "editor" gets the
  // full toolbar; anything else flips the editor to viewing mode.
  // Suggesting mode (tracked changes) is exposed via the editor's own
  // mode toggle — we don't force it from outside.

  // Phase 7 import mode. When `importBuffer` is provided, the editor
  // mounts in single-user IMPORT MODE: docx-editor parses the OOXML
  // buffer into its internal ProseMirror state directly (externalContent
  // off, no ySyncPlugin / cursor plugin). After parsing settles, the
  // editor extracts the PM JSON via the captured EditorView and hands
  // it back to the parent through `onImportFinalized` — the parent then
  // PATCHes body_jsonb + DELETEs the stash + navigates to the same route
  // without `?import=1`. The next mount uses the normal Yjs path, where
  // the sidecar's `trySeedFromBodyMirror` populates the Y.Doc from the
  // freshly-written body_jsonb mirror on first connect.
  importBuffer?: ArrayBuffer | null;
  onImportFinalized?: (bodyJsonb: string) => void;
  // Phase 11 read-only preview. When true, the editor mounts as a
  // chrome-free, read-only "populated output" view of the live doc:
  // forced readOnly + viewing mode, no toolbar / AI panel / bindings
  // panel, and binding sync is skipped (preview must not mutate the doc).
  // Because Phase 10 put resolved binding values directly in the document
  // tree, no resolve step is needed — it's just a read-only mount.
  previewMode?: boolean;
};

// Empty schema seed. docx-editor needs a Document object on mount to
// build its ProseMirror schema even when `externalContent: true` tells
// it not to load that document's content into the editor. Hoist to
// module scope so the reference is stable — passing a fresh
// createEmptyDocument() result on every render would re-build the
// schema on every parent re-render.
const SCHEMA_SEED_DOCUMENT = createEmptyDocument();

// docx-editor stores comment bodies as `Paragraph[]` to match OOXML's
// `w:p` structure inside `w:comment`. Each Paragraph holds Run[], and
// each Run holds TextContent[]. Our backend stores comments as plain
// text — most comments are short prose; richer formatting is a polish
// decision. Walk the two-level tree (paragraph → run → text) and join.
function extractCommentText(c: { content: unknown }): string {
  const paragraphs = Array.isArray(c.content) ? c.content : [];
  const parts: string[] = [];
  for (const para of paragraphs) {
    if (!isObj(para)) continue;
    const runs = (para as { content?: unknown }).content;
    if (!Array.isArray(runs)) continue;
    for (const run of runs) {
      if (!isObj(run)) continue;
      // Hyperlink / Insertion / etc. nest their text further; for v1
      // we look only at plain Run nodes, the dominant shape from the
      // editor's add-comment UI.
      const runType = (run as { type?: unknown }).type;
      if (runType !== "run") continue;
      const textNodes = (run as { content?: unknown }).content;
      if (!Array.isArray(textNodes)) continue;
      for (const node of textNodes) {
        if (!isObj(node)) continue;
        const nodeType = (node as { type?: unknown }).type;
        const text = (node as { text?: unknown }).text;
        if (nodeType === "text" && typeof text === "string") {
          parts.push(text);
        }
      }
    }
    parts.push("\n");
  }
  return parts.join("").trim();
}

function isObj(x: unknown): x is Record<string, unknown> {
  return typeof x === "object" && x !== null;
}

// docx-editor's suggestion-mode plugin wraps any doc-changing transaction
// that LACKS this meta flag in tracked-change (insertion) marks while the
// editor is in "suggesting" mode. Our markdown-insertion actions are
// authoring-on-the-user's-behalf, not suggestions — they should land as
// direct edits regardless of mode. Setting this meta makes the plugin's
// appendTransaction skip the transaction (it matches the editor's own
// internal `y="suggestionModeApplied"` bypass string). Coupled to a
// docx-editor internal — if a version bump breaks insertion-while-in-
// suggesting-mode, re-grep the bundle for the PluginKey("suggestionMode")
// neighbourhood to confirm the meta string. (suggest_text_replacement is
// unaffected — it goes through the editor's own proposeChange ref, which
// is SUPPOSED to be tracked.)
const SUGGESTION_MODE_BYPASS_META = "suggestionModeApplied";

function markAsDirectEdit(tr: Transaction): void {
  tr.setMeta(SUGGESTION_MODE_BYPASS_META, true);
}

// Run both binding-node sync passes against the live view. Table (block)
// pass first in its own transaction, then record-field (inline) — keeps
// block + inline position math from tangling. No-ops when the view is
// gone or there's nothing to change. Wrapped so a failure in one pass
// can't take down the editor.
function runBindingSync(
  view: EditorView | null,
  bindings: DocumentBindingDto[],
  bindingsLoaded: boolean
): void {
  if (!view) return;
  try {
    syncAqlTableNodes(view, bindings, markAsDirectEdit, bindingsLoaded);
  } catch (err) {
    console.warn("[bindings] aql-table sync failed", err);
  }
  try {
    syncRecordFieldNodes(view, bindings, markAsDirectEdit, bindingsLoaded);
  } catch (err) {
    console.warn("[bindings] record-field sync failed", err);
  }
}

// Make docx-editor's accept/reject tracked-change buttons mode-safe.
//
// Problem: docx-editor's acceptChange/rejectChange commands build a
// transaction that strips the insertion/deletion mark + deletes the
// other side's text, but they DON'T stamp the suggestion-tracker's
// bypass meta. In Suggesting mode the tracker's appendTransaction then
// re-intercepts that transaction's delete and re-marks the text instead
// of removing it — so "reject" visibly does nothing (and "accept" is
// flaky). In Editing mode the tracker is inactive so it works; the bug
// only bites in Suggesting mode.
//
// Fix: wrap the view's dispatch and, for any transaction that REMOVES an
// `insertion`/`deletion` mark (the structural signature of an accept or
// reject), stamp the bypass meta so the tracker leaves it alone. No-op
// in Editing mode (the tracker ignores the meta when inactive), correct
// in Suggesting mode. Idempotent — guarded so a key={role} remount that
// re-fires onEditorViewReady doesn't double-wrap.
type PatchableView = EditorView & {
  __acceptRejectModeSafe?: boolean;
  dispatch: (tr: Transaction) => void;
};

function makeAcceptRejectModeSafe(view: EditorView): void {
  const v = view as PatchableView;
  if (v.__acceptRejectModeSafe) return;
  v.__acceptRejectModeSafe = true;
  const original = v.dispatch.bind(v);
  v.dispatch = (tr: Transaction) => {
    // A late transaction can arrive after the view is torn down — e.g.
    // y-prosemirror's awareness/meta observer firing during a route
    // remount (preview ↔ editor). prosemirror-view nulls `docView` on
    // destroy, and dispatching then throws deep in updateState
    // (`matchesNode` of null). Skip — the view is going away.
    if ((v as { docView?: unknown }).docView == null) return;
    if (transactionRemovesTrackedMark(tr)) {
      tr.setMeta(SUGGESTION_MODE_BYPASS_META, true);
    }
    original(tr);
  };
}

// True when the transaction strips an insertion/deletion mark — the
// distinguishing step of an accept/reject (vs. an ordinary edit, which
// never removes those marks). We read each step's serialized form so the
// check survives minification (no `instanceof RemoveMarkStep` against a
// bundled class).
function transactionRemovesTrackedMark(tr: Transaction): boolean {
  for (const step of tr.steps) {
    const json = (step as { toJSON?: () => unknown }).toJSON?.() as
      | { stepType?: string; mark?: { type?: string } }
      | undefined;
    if (
      json?.stepType === "removeMark" &&
      (json.mark?.type === "insertion" || json.mark?.type === "deletion")
    ) {
      return true;
    }
  }
  return false;
}

export default function DocxDocumentEditor({
  documentId,
  documentTitle,
  onRenameDocument,
  titleBarRight,
  importBuffer = null,
  onImportFinalized,
  previewMode = false
}: Props) {
  const importMode = importBuffer != null;
  // Right-side panel selection. Bindings and version-history share the
  // right rail and are mutually exclusive — `activePanel` tracks which (if
  // any) is open. Defaults to "bindings" so users coming from Phase 5
  // don't lose the surface they're used to. Toolbar toggles (rendered via
  // docx-editor's `toolbarExtra`) flip it; each panel's header X clears it.
  // Both are suppressed in import + preview modes.
  const [activePanel, setActivePanel] = useState<"bindings" | "versions" | null>(
    "bindings"
  );
  const [permissionsOpen, setPermissionsOpen] = useState(false);
  const yjsName = useMemo(() => `documents:${documentId}`, [documentId]);
  // Import mode skips Yjs entirely on this mount — the editor is single-user
  // until the buffer is parsed and PATCHed into body_jsonb. The hook accepts
  // `null` to no-op (no Hocuspocus connection, no awareness writes). Once the
  // parent finalizes the import + navigates away from `?import=1`, the next
  // mount has `importMode=false` and the hook reconnects normally.
  const { handle, role } = useYjsDocument(importMode ? null : yjsName);
  const { data: me } = useMe();
  const authorName = useMemo(() => {
    if (!me || me.authenticated !== true) return "User";
    const full = `${me.firstName ?? ""} ${me.lastName ?? ""}`.trim();
    return full || me.username || "User";
  }, [me]);

  // Comments live in REST, not Yjs (Phase 4 design choice — permissions,
  // RAG, and audit all favor a server-of-truth model). The Y.Doc still
  // carries the commentRangeStart/End markers because those are body
  // content. The metadata array we feed to docx-editor's `comments`
  // prop comes from our React Query cache.
  const { data: commentRows = [] } = useDocumentComments(documentId);
  const createComment = useCreateDocumentComment();
  const replyComment = useReplyToDocumentComment();
  const resolveComment = useResolveDocumentComment();
  const deleteComment = useDeleteDocumentComment();

  // docx-editor passes back Comment objects keyed on the numeric
  // `number` we stamped at create time; resolve/delete callbacks give us
  // that number too. Maintain a number→canonical Guid index so we can
  // hit the right REST row without an extra fetch. Refs (not state) to
  // avoid re-renders that don't affect editor output.
  const numberToIdRef = useRef<Map<number, string>>(new Map());

  // Translate our DTOs into docx-editor's Comment shape (number id +
  // Paragraph[] body + parentId number). Build the index in the same
  // pass — both have O(N) cost, keep them aligned. useMemo so the
  // controlled `comments` prop has stable reference identity when
  // nothing actually changed (avoids docx-editor reconciling on every
  // unrelated re-render).
  const comments = useMemo(() => {
    const idx = new Map<number, string>();
    const result = commentRows.map((row) => {
      idx.set(row.number, row.id);
      // OOXML structure: Paragraph.content holds Run[], Run.content holds
      // TextContent[]. Plain-text comments become a single paragraph with
      // a single run holding the text.
      const paragraph = {
        type: "paragraph" as const,
        content: row.bodyText
          ? [
              {
                type: "run" as const,
                content: [{ type: "text" as const, text: row.bodyText }]
              }
            ]
          : []
      };
      return {
        id: row.number,
        author: row.authorName ?? "User",
        date: row.createdAtUtc,
        content: [paragraph],
        // parentCommentId is a Guid; we need the parent row's `number`
        // to satisfy docx-editor's Comment.parentId shape. Look it up
        // from the same list. (O(N²) worst case for very deep threads —
        // fine in practice for chat-style comment volumes.)
        parentId:
          row.parentCommentId != null
            ? commentRows.find((p) => p.id === row.parentCommentId)?.number
            : undefined,
        done: row.resolvedAtUtc != null
      };
    });
    numberToIdRef.current = idx;
    return result;
  }, [commentRows]);

  // Mode decision: editors get full edit, commenters get a locked body
  // + open comments sidebar (mode='viewing'; readOnly=false so the
  // commenting UI stays interactive), viewers get fully read-only.
  // Preview mode forces fully read-only viewing regardless of role.
  const mode: "editing" | "suggesting" | "viewing" =
    !previewMode && role === "editor" ? "editing" : "viewing";
  const readOnly = previewMode || role === "viewer";

  // Live data bindings. As of Phase 10 both kinds render as first-class
  // PM nodes (record-field → `field` node; aql-table → real table +
  // caption marker), kept in sync with the REST bindings list by the
  // effect below — no more text placeholders or decoration plugin.
  const { data: bindingRows = [], isSuccess: bindingsLoaded } =
    useDocumentBindings(documentId);

  // Phase 10: keep binding nodes in sync with the REST bindings list.
  // Two idempotent passes — aql-table (block: tables + caption markers)
  // first, then record-field (inline: field nodes) — each migrating any
  // legacy `{{binding:UUID}}` placeholder and refreshing stale rendered
  // values. Editor-role only (viewers don't mutate the body); skipped in
  // import mode (body not committed yet).
  //
  // Triggering is split across two places to handle either arrival
  // order of (view-ready, bindings-loaded):
  //   • This effect fires on every bindingRows change — covers refresh
  //     and the case where bindings load AFTER the view is ready.
  //   • onEditorViewReady runs a one-shot sync via bindingRowsRef —
  //     covers the case where the view mounts AFTER bindings loaded.
  // Both defer to the next tick so we don't dispatch into docx-editor's
  // own mount-time transactions, and both no-op when there's nothing to
  // change (the functions are idempotent). The table pass runs first +
  // in its own transaction so its block edits don't tangle position
  // math with the inline field pass.
  const bindingRowsRef = useRef(bindingRows);
  bindingRowsRef.current = bindingRows;
  const bindingsLoadedRef = useRef(bindingsLoaded);
  bindingsLoadedRef.current = bindingsLoaded;
  useEffect(() => {
    if (importMode || previewMode || role !== "editor") return;
    if (!editorViewRef.current) return;
    const id = window.setTimeout(() => {
      runBindingSync(
        editorViewRef.current,
        bindingRowsRef.current,
        bindingsLoadedRef.current
      );
    }, 0);
    return () => window.clearTimeout(id);
    // bindingsLoaded in the deps so the false→true load transition fires
    // the sync even when the list is genuinely empty (orphan cleanup on
    // a doc whose bindings were all deleted).
  }, [bindingRows, bindingsLoaded, role, importMode, previewMode]);

  // Phase 8: register the document with the chatbot's PageContextRegistry
  // so the in-editor agent panel sees the doc's title + bindings + body
  // preview. Body text is read on-demand from the live EditorView at
  // snapshot time (called by the chat panel's send action) — no React
  // state plumbing, no polling. Skipped in import mode because the body
  // isn't committed yet and the chat panel isn't mounted either.
  useDocumentEditorPageContext({
    documentId,
    documentTitle,
    bindings: bindingRows,
    getBodyText: () => {
      const view = editorViewRef.current;
      if (!view) return null;
      try {
        const doc = view.state.doc;
        return doc.textBetween(0, doc.content.size, "\n", "\n");
      } catch {
        return null;
      }
    },
    // Read the user's current selection on demand so the agent's
    // page-context snapshot includes it. Walks the doc from the
    // selection's $from anchor to find the enclosing textblock (always
    // a paragraph in docx-editor's schema), reads its paraId, slices a
    // window of surrounding context. Returns null when nothing is
    // selected, the selection is collapsed (cursor only, no range),
    // the enclosing block lacks a paraId, or the view isn't mounted.
    getSelection: () => {
      const view = editorViewRef.current;
      if (!view) return null;
      try {
        const { from, to } = view.state.selection;
        if (from === to) return null; // collapsed cursor — no selection
        const $from = view.state.doc.resolve(from);
        // Walk up to the enclosing textblock. depth=0 is the doc itself;
        // the paragraph is typically at depth=1.
        let blockDepth = $from.depth;
        while (blockDepth > 0 && !$from.node(blockDepth).isTextblock) {
          blockDepth--;
        }
        if (blockDepth === 0) return null;
        const block = $from.node(blockDepth);
        const paraId = (block.attrs as { paraId?: unknown })?.paraId;
        if (typeof paraId !== "string" || paraId.length === 0) return null;
        const blockStart = $from.start(blockDepth);
        const blockEnd = $from.end(blockDepth);
        const safeFrom = Math.max(from, blockStart);
        const safeTo = Math.min(to, blockEnd);
        const selectedText = view.state.doc.textBetween(safeFrom, safeTo, "\n");
        const beforeFull = view.state.doc.textBetween(blockStart, safeFrom, "\n");
        const afterFull = view.state.doc.textBetween(safeTo, blockEnd, "\n");
        // Trim surrounding context — front-half of the trailing chunk,
        // back-half of the leading chunk, so we stay near the selection.
        const before =
          beforeFull.length > SELECTION_CONTEXT_LIMIT
            ? `…${beforeFull.slice(-SELECTION_CONTEXT_LIMIT)}`
            : beforeFull;
        const after =
          afterFull.length > SELECTION_CONTEXT_LIMIT
            ? `${afterFull.slice(0, SELECTION_CONTEXT_LIMIT)}…`
            : afterFull;
        return { paraId, selectedText, before, after };
      } catch {
        return null;
      }
    },
    // Apply chatbot-driven mutations against the live EditorView. The
    // transactions flow through ySyncPlugin into Yjs, so collaborators
    // see the change in real time. Edits are gated to editor-role users
    // because the agent calls `apply_page_action` first with
    // confirmed=false (no-op, just describes the change) and the model
    // only mutates after the user confirms in chat — that confirmation
    // round-trip is what the skill enforces, not us.
    onAction: async (req) => {
      const view = editorViewRef.current;
      if (!view) {
        return {
          ok: false,
          error: "page_unreachable",
          message: "Document editor view is not mounted yet."
        };
      }
      if (role !== "editor") {
        return {
          ok: false,
          error: "forbidden",
          message: `Your role (${role}) doesn't permit edits to this document.`
        };
      }

      // Phase 8b: tracked-change suggestion. Routes through docx-editor's
      // own proposeChange ref method, which inserts the proposal as a
      // Word-style tracked change regardless of the editor's current
      // mode (editing/suggesting/viewing). The user reviews + accepts/
      // rejects in the existing tracked-changes UI — that's the preview,
      // so we don't gate this behind the describe-then-confirm pattern.
      if (req.action === "suggest_text_replacement") {
        // Accept common arg-name aliases — models reliably vary between
        // search/oldText/find and replaceWith/newText/replacement even
        // when the schema names one set. Being liberal here avoids a
        // wasted round-trip where the model guesses the "wrong" name.
        const a = (req.args ?? {}) as Record<string, unknown>;
        const pickString = (...keys: string[]): string | undefined => {
          for (const k of keys) {
            if (typeof a[k] === "string") return a[k] as string;
          }
          return undefined;
        };
        const paraId = pickString("paraId", "paragraphId", "para_id");
        const search = pickString("search", "oldText", "find", "target", "old");
        // replaceWith may legitimately be "" (delete) — pick even empty.
        const replaceWith = pickString(
          "replaceWith",
          "newText",
          "replacement",
          "new",
          "with"
        );
        if (!paraId || paraId.length === 0) {
          return {
            ok: false,
            error: "bad_request",
            message:
              "paraId is required (use data.selection.paraId from the page snapshot)."
          };
        }
        if (!search || search.length === 0) {
          return {
            ok: false,
            error: "bad_request",
            message:
              "search is required — the unique substring of the target paragraph to replace (aliases: oldText, find)."
          };
        }
        if (replaceWith === undefined) {
          return {
            ok: false,
            error: "bad_request",
            message:
              "replaceWith is required, use \"\" to delete the matched text (aliases: newText, replacement)."
          };
        }
        const ref = editorRef.current;
        if (!ref) {
          return {
            ok: false,
            error: "page_unreachable",
            message: "Editor ref not ready yet; try again in a moment."
          };
        }
        const ok = ref.proposeChange({
          paraId,
          search,
          replaceWith,
          author: authorName
        });
        if (!ok) {
          // proposeChange returns false for: missing paraId, missing or
          // ambiguous search (substring not found or matched twice), or
          // attempt to layer on an existing tracked change. Telling the
          // agent which case it was would require a richer return — for
          // now, the union-of-causes message is enough for it to retry
          // with a longer / more unique search string.
          return {
            ok: false,
            error: "propose_failed",
            message:
              "Couldn't apply the suggestion. Most likely the search string wasn't a unique substring " +
              "of that paragraph (matched zero or multiple times) — try a longer, more distinctive " +
              "span. The paraId may also be stale, or the target range may already carry a tracked change."
          };
        }
        return {
          ok: true,
          summary:
            replaceWith.length === 0
              ? `Proposed a tracked-change deletion of "${search.slice(0, 60)}${search.length > 60 ? "…" : ""}" — the user can accept or reject it in the review UI.`
              : `Proposed a tracked-change replacement: "${search.slice(0, 40)}${search.length > 40 ? "…" : ""}" → "${replaceWith.slice(0, 40)}${replaceWith.length > 40 ? "…" : ""}" — the user can accept or reject it in the review UI.`
        };
      }

      const args = (req.args ?? {}) as { markdown?: unknown };
      const markdown = typeof args.markdown === "string" ? args.markdown : "";
      if (!markdown.trim()) {
        return {
          ok: false,
          error: "bad_request",
          message: "args.markdown is required and must be a non-empty string."
        };
      }
      // Parse markdown into a PM Slice against the editor's actual
      // schema — that way headings + bold + italic + lists map to the
      // matching docx-editor styles instead of landing as literal
      // `## Heading` text inside plain paragraphs. We also pass the
      // live document's style table so `styleId="Heading1"` resolves
      // to the actual rPr (bold, fontSize, fontFamily, etc.) defined
      // in this document — matching what `commands.applyStyle` would
      // do when the user picks Heading 1 from the toolbar. Without
      // this lookup, the runs render at body defaults regardless of
      // the document's heading styling.
      const { schema } = view.state;
      // docx-editor's getDocument() returns its Document object with a
      // `package.styles` table. We accept any duck-typed shape with
      // matching field names; the converter's resolver only reads
      // styleId / basedOn / rPr fields and tolerates missing ones.
      const liveDoc = editorRef.current?.getDocument() as
        | { package?: { styles?: import("./markdownToPmNodes").StyleTable } }
        | null
        | undefined;
      const styleTable = liveDoc?.package?.styles;
      let slice;
      try {
        slice = markdownToProseMirrorSlice(markdown, schema, styleTable);
      } catch (err) {
        console.error("[apply_page_action] markdown parse failed", err);
        return {
          ok: false,
          error: "parse_failed",
          message: `Failed to parse markdown: ${err instanceof Error ? err.message : "unknown error"}.`
        };
      }
      if (slice.size === 0) {
        return {
          ok: false,
          error: "empty_result",
          message: "Markdown parsed to an empty slice — nothing to insert."
        };
      }

      if (req.action === "append_markdown") {
        // Insert at the very end of the doc body. Using tr.replace lets
        // the slice's open boundaries merge cleanly with the trailing
        // paragraph; tr.insert would force a paragraph break first.
        const endPos = view.state.doc.content.size;
        const tr = view.state.tr.replace(endPos, endPos, slice);
        markAsDirectEdit(tr);
        view.dispatch(tr);
        return {
          ok: true,
          summary: `Appended ${slice.content.childCount} block${slice.content.childCount === 1 ? "" : "s"} of formatted content to the end of the document.`
        };
      }
      if (req.action === "insert_markdown_at_cursor") {
        const { from, to } = view.state.selection;
        const tr = view.state.tr.replace(from, to, slice);
        markAsDirectEdit(tr);
        view.dispatch(tr);
        view.focus();
        return {
          ok: true,
          summary: `Inserted ${slice.content.childCount} block${slice.content.childCount === 1 ? "" : "s"} of formatted content at the cursor.`
        };
      }
      return {
        ok: false,
        error: "unsupported_action",
        message: `Document editor doesn't support action '${req.action}'. Available: append_markdown, insert_markdown_at_cursor, suggest_text_replacement.`
      };
    }
  });

  // Hold onto the EditorView so the side panel's "Insert at cursor"
  // action can dispatch a ProseMirror transaction that drops a
  // `{{binding:UUID}}` placeholder where the cursor is.
  const editorViewRef = useRef<EditorView | null>(null);

  // Phase 7: export ref. `ref.current.save()` returns the canonical
  // OOXML ArrayBuffer — the download helper wraps it in a Blob + an
  // <a download> click so the user gets a real .docx file. Bound to
  // both the title-bar Download button and docx-editor's own
  // File → Save menu (via `onSave`) so either entry point works.
  const editorRef = useRef<DocxEditorRef | null>(null);
  const downloadAsDocx = useCallback(async () => {
    try {
      const buf =
        editorRef.current && (await editorRef.current.save());
      if (!buf) {
        notifications.show({
          message: "Document not ready to export. Try again in a moment.",
          color: "yellow"
        });
        return;
      }
      const blob = new Blob([buf], {
        type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
      });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      // Sanitize the title for a filesystem-safe filename. The editor's
      // own File menu uses a similar shape; we keep them consistent so
      // a user who hits both ends up with the same artifact.
      const safeName = (documentTitle || "document").replace(/[\\/:*?"<>|]/g, "_");
      anchor.download = `${safeName}.docx`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error("[export] failed", err);
      notifications.show({
        message: "Failed to export document.",
        color: "red"
      });
    }
  }, [documentTitle]);

  // Phase 7: import finalize plumbing. After docx-editor parses the
  // OOXML buffer it dispatches the "load content" transaction(s); we
  // wait a beat for the parse to settle (debounced via the latest
  // `onChange`), then extract `view.state.doc.toJSON()` and hand the
  // serialized ProseMirror JSON back to the parent. The parent owns
  // the PATCH + DELETE + navigation so this component can stay
  // controller-free.
  //
  // Guard the finalize to fire exactly once per import session — extra
  // transactions after the first parse (e.g. layout-driven onChange
  // events docx-editor sometimes emits) shouldn't trigger a second
  // round-trip.
  const finalizedRef = useRef(false);
  const finalizeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    // Cleanup pending timers when the component unmounts mid-import
    // (e.g. user navigates away before finalize fires).
    return () => {
      if (finalizeTimerRef.current) clearTimeout(finalizeTimerRef.current);
    };
  }, []);

  // Import finalize is driven by watching the editor's own document settle,
  // not by docx-editor's `onChange` (archived-173).
  //
  // The finalize used to hang off `onChange`, on the assumption that the
  // OOXML parse pass would surface as change events. It does not — the
  // parse populates the view without the library ever calling that prop, so
  // the debounce never armed, `onImportFinalized` never fired, the URL sat
  // on `?import=1` forever and the document reloaded empty. Measured:
  // `onEditorViewReady` fires (twice), `onChange` never does.
  //
  // Polling the view is deliberately dumber than any event and survives the
  // library changing which events it emits. We wait for the parsed content
  // to stop growing rather than firing on the first non-empty read, because
  // a long document arrives across several transactions.
  const IMPORT_SETTLE_INTERVAL_MS = 250;
  const IMPORT_SETTLE_STABLE_TICKS = 2;
  const IMPORT_SETTLE_TIMEOUT_MS = 20_000;

  const scheduleImportFinalize = (view: EditorView) => {
    if (!importMode || !onImportFinalized || finalizedRef.current) return;
    if (finalizeTimerRef.current) clearTimeout(finalizeTimerRef.current);

    const startedAt = Date.now();
    let lastSize = -1;
    let stableTicks = 0;

    const tick = () => {
      if (finalizedRef.current) return;
      // The view can be torn down mid-poll (user navigates away).
      if ((view as { docView?: unknown }).docView == null) return;

      // An empty ProseMirror doc is one empty paragraph — content.size 2.
      // Anything at or below that is "nothing parsed yet".
      const size = view.state.doc.content.size;
      if (size > 2 && size === lastSize) {
        stableTicks += 1;
      } else {
        stableTicks = 0;
        lastSize = size;
      }

      if (stableTicks >= IMPORT_SETTLE_STABLE_TICKS) {
        finalizedRef.current = true;
        try {
          onImportFinalized(JSON.stringify(view.state.doc.toJSON()));
        } catch (err) {
          console.error("[import] finalize serialization failed", err);
          finalizedRef.current = false;
        }
        return;
      }

      if (Date.now() - startedAt > IMPORT_SETTLE_TIMEOUT_MS) {
        // Deliberately does NOT finalize. Committing an empty body here
        // would clear `?import=1` and destroy the server-side stash, which
        // is the one copy of the user's upload left. Leaving import mode
        // in place keeps the stash, so a reload retries the parse.
        console.error(
          "[import] gave up waiting for parsed content to settle; " +
            "the import stash is left intact so a reload can retry."
        );
        return;
      }

      finalizeTimerRef.current = setTimeout(tick, IMPORT_SETTLE_INTERVAL_MS);
    };

    finalizeTimerRef.current = setTimeout(tick, IMPORT_SETTLE_INTERVAL_MS);
  };

  const insertBindingPlaceholder = (binding: DocumentBindingDto) => {
    const view = editorViewRef.current;
    if (!view) return;

    // Phase 10: both binding kinds insert first-class nodes (no more
    // `{{binding:UUID}}` text + decoration chip).
    //   record-field → inline `field` node (value in displayText).
    //   aql-table    → a [caption-marker paragraph, real table] block
    //                  pair inserted after the cursor's top-level block.
    if (binding.kind === "record-field") {
      const fieldNode = buildRecordFieldNode(
        view.state.schema,
        binding.id,
        recordFieldDisplayText(binding)
      );
      const tr = view.state.tr.replaceSelectionWith(fieldNode, false);
      markAsDirectEdit(tr);
      view.dispatch(tr);
      view.focus();
      return;
    }

    if (binding.kind === "aql-table") {
      insertAqlTableBinding(view, binding, markAsDirectEdit);
      return;
    }
  };

  // Build the y-prosemirror plugin list once the Y.Doc + awareness are
  // available. The fragment name "default" matches the sidecar
  // materializer; changing it would silently break body_jsonb snapshots.
  //
  // `role` is in the deps so when the server-side role flips from the
  // pessimistic initial "viewer" to the actual "editor" (after the Yjs
  // ticket fetch resolves), we recreate the plugin instances. docx-editor
  // remounts on `key={role}` below and we need fresh ySyncPlugin/
  // yCursorPlugin instances bound to the new EditorView — the old ones
  // were attached to the now-destroyed view.
  const externalPlugins = useMemo(() => {
    // In import mode the editor runs single-user against the parsed
    // OOXML state — no Yjs sync/cursor plugins, and no binding plugins
    // (Phase 10 renders bindings as real nodes, no decoration plugin).
    if (importMode) {
      return [];
    }
    if (!handle) return [];
    const fragment = handle.doc.getXmlFragment("default");
    // ySyncPlugin owns content sync; yCursorPlugin adds awareness.
    // bindingHighlightPlugin paints the hover-highlight for bound content
    // (driven by the side panel) — view-only, no doc mutation.
    const plugins = [ySyncPlugin(fragment), bindingHighlightPlugin()];
    // HocuspocusProvider types awareness as `Awareness | null`. The null
    // state is theoretical at runtime (the constructor populates it
    // eagerly), but yCursorPlugin's signature won't accept null, so
    // gate the plugin's inclusion rather than ?? undefined.
    const awareness = handle.provider.awareness;
    if (awareness) {
      plugins.push(
        yCursorPlugin(awareness, {
          // Display each remote user's name + a stable color above their
          // cursor. The agent SDK uses the same `user` map for comment /
          // tracked-change author attribution downstream.
          cursorBuilder: (user: { name?: string; color?: string }) => {
            const el = document.createElement("span");
            el.classList.add("docx-remote-cursor");
            el.setAttribute(
              "style",
              `border-left: 2px solid ${user.color ?? "#1c7ed6"}; margin-left: -1px;`
            );
            const label = document.createElement("div");
            label.classList.add("docx-remote-cursor-label");
            label.setAttribute(
              "style",
              `background: ${user.color ?? "#1c7ed6"}; color: white; padding: 1px 6px; border-radius: 3px 3px 3px 0; font-size: 11px; font-weight: 600; position: absolute; top: -1.4em; left: -1px; white-space: nowrap;`
            );
            label.textContent = user.name ?? "User";
            el.appendChild(label);
            return el;
          }
        })
      );
    }
    return plugins;
  }, [handle, role, importMode]);

  // While the Yjs connection is establishing, render a placeholder so
  // the editor surface isn't blank — docx-editor's own placeholder
  // prop covers this once it mounts, but the very first paint can
  // race ahead of `handle` being set. Skipped in import mode (no Yjs
  // connection to wait for).
  if (!importMode && !handle) {
    return (
      <div style={{ padding: 32, color: "var(--mantine-color-dimmed)" }}>
        Connecting to document…
      </div>
    );
  }

  return (
    <Box style={{ display: "flex", height: "100%", minHeight: 0 }}>
      <Box style={{ flex: 1, minWidth: 0, minHeight: 0 }}>
        <DocxEditor
      // Force a remount when role transitions OR when leaving import
      // mode. docx-editor latches the readOnly prop at mount and ignores
      // subsequent changes — without this key the editor would stay
      // locked at the pessimistic initial "viewer" role even after the
      // ticket fetch promotes the user to "editor". Yjs state survives
      // the remount because the Y.Doc + provider live in useYjsDocument
      // one level up. The `importMode` segment forces a clean remount
      // when the parent transitions us from import → live (typically
      // after a navigation that strips `?import=1`).
      key={`${importMode ? "import" : "live"}:${role}`}
      // Import mode: feed the OOXML buffer + a null `document` so the
      // editor takes the buffer as the parse source. externalContent
      // is OFF so docx-editor parses + manages the PM state directly.
      // Live mode: schema seed only; ySyncPlugin feeds content from Yjs.
      document={importMode ? null : SCHEMA_SEED_DOCUMENT}
      documentBuffer={importMode ? importBuffer : undefined}
      externalContent={!importMode}
      externalPlugins={externalPlugins}
      // Debounced finalize for import mode. The first transactions
      // come from docx-editor's parse pass; we want to fire `onImport
      // Finalized` *after* the editor stops dispatching them. 500 ms
      // of quiet is plenty for a typical document. Use onEditorViewReady
      // to capture the view so we can call `state.doc.toJSON()`; the
      // editor's own `onChange` returns a docx-editor Document, not PM
      // JSON, so we go through the view instead.
      // Secondary kick only. docx-editor does not call this during the
      // OOXML parse pass (see scheduleImportFinalize above), so the settle
      // poll is what actually finalizes; this re-arms it if the library
      // ever does emit a change first.
      onChange={
        importMode && onImportFinalized
          ? () => {
              const view = editorViewRef.current;
              if (view) scheduleImportFinalize(view);
            }
          : undefined
      }
      author={authorName}
      // Server-decided role drives chrome:
      //   editor    → mode='editing', readOnly=false (full toolbar)
      //   commenter → mode='viewing', readOnly=false (body locked,
      //               comments sidebar interactive)
      //   viewer    → readOnly=true (no edits, no comments)
      // The editor's own mode toggle (editing / suggesting / viewing)
      // is still user-driven for editor-role users via the toolbar.
      mode={mode}
      readOnly={readOnly}
      // Controlled comments — feed our REST-backed array and forward
      // every mutation back through the React Query hooks so the
      // server stays the source of truth. Each callback receives a
      // `Comment` whose `id` is the per-document numeric ID; we
      // resolve it to the canonical Guid via numberToIdRef.
      comments={comments}
      onCommentAdd={(c) => {
        const bodyText = extractCommentText(c);
        if (!bodyText) return;
        const parentId = c.parentId;
        if (parentId != null) {
          const parentGuid = numberToIdRef.current.get(parentId);
          if (!parentGuid) {
            console.warn(
              `[comments] reply parentId=${parentId} not in numberToIdRef; skipping`
            );
            return;
          }
          replyComment.mutate({
            documentId,
            parentCommentId: parentGuid,
            number: c.id,
            bodyText
          });
        } else {
          createComment.mutate({
            documentId,
            number: c.id,
            bodyText
          });
        }
      }}
      onCommentReply={(reply, _parent) => {
        const bodyText = extractCommentText(reply);
        const parentGuid =
          reply.parentId != null
            ? numberToIdRef.current.get(reply.parentId)
            : undefined;
        if (!bodyText || !parentGuid) return;
        replyComment.mutate({
          documentId,
          parentCommentId: parentGuid,
          number: reply.id,
          bodyText
        });
      }}
      onCommentResolve={(c) => {
        const guid = numberToIdRef.current.get(c.id);
        if (!guid) return;
        resolveComment.mutate({ documentId, commentId: guid });
      }}
      onCommentDelete={(c) => {
        const guid = numberToIdRef.current.get(c.id);
        if (!guid) return;
        deleteComment.mutate({ documentId, commentId: guid });
      }}
      // Surface the docx-editor's full Word-style toolbar. Hidden in
      // preview — preview is a chrome-free, read-only "output" view.
      showToolbar={!previewMode}
      showRuler={!previewMode}
      showZoomControl={!previewMode}
      // Custom toolbar slot — renders to the right of the built-in
      // controls. We use it for the Bindings panel toggle so it sits
      // next to docx-editor's own "Open assistant" button. Hidden in
      // import mode (no bindings panel mounted during import either).
      toolbarExtra={
        importMode || previewMode ? undefined : (
          <Group gap={4} wrap="nowrap">
            <Tooltip
              label={activePanel === "bindings" ? "Hide bindings panel" : "Show bindings panel"}
              withArrow
              openDelay={350}
            >
              <ActionIcon
                variant={activePanel === "bindings" ? "filled" : "subtle"}
                color={activePanel === "bindings" ? "blue" : "gray"}
                size="md"
                onClick={() =>
                  setActivePanel((p) => (p === "bindings" ? null : "bindings"))
                }
                aria-label="Toggle bindings panel"
                aria-pressed={activePanel === "bindings"}
              >
                <i className="fa fa-database" aria-hidden />
              </ActionIcon>
            </Tooltip>
            <Tooltip
              label={activePanel === "versions" ? "Hide version history" : "Show version history"}
              withArrow
              openDelay={350}
            >
              <ActionIcon
                variant={activePanel === "versions" ? "filled" : "subtle"}
                color={activePanel === "versions" ? "blue" : "gray"}
                size="md"
                onClick={() =>
                  setActivePanel((p) => (p === "versions" ? null : "versions"))
                }
                aria-label="Toggle version history"
                aria-pressed={activePanel === "versions"}
              >
                <i className="fa fa-clock-rotate-left" aria-hidden />
              </ActionIcon>
            </Tooltip>
          </Group>
        )
      }
      // Phase 8: doc-scoped AI chat in docx-editor's built-in agentPanel
      // slot. The library renders a toolbar toggle button + a resizable
      // right-side panel; we provide the panel content. Width persists
      // to localStorage automatically. Hidden in import mode because the
      // page context provider doesn't register until the editor's live
      // EditorView is up — chat would have no body to reference yet.
      agentPanel={
        importMode || previewMode
          ? undefined
          : {
              title: "AI Assistant",
              render: ({ close }) => (
                <DocumentChatPanel documentId={documentId} onClose={close} />
              )
            }
      }
      // Title bar wiring: the docx-editor renders the doc name + an
      // optional right slot we use for the "Back to project" link.
      // Renames flow through our REST documents endpoint via the
      // parent's callback; the doc name itself is NOT a Yjs property.
      documentName={documentTitle}
      documentNameEditable={
        !previewMode && role === "editor" && Boolean(onRenameDocument)
      }
      onDocumentNameChange={onRenameDocument}
      // Compose: caller's slot (typically "Back to project") +
      // a Download button that exports the current state as .docx.
      // Hidden in import mode because the user hasn't committed the
      // imported content yet — they should finalize first.
      renderTitleBarRight={() => (
        <Group gap="sm" wrap="nowrap">
          {titleBarRight}
          {!importMode && !previewMode && role === "editor" ? (
            <Button
              size="xs"
              variant="default"
              leftSection={<i className="fa fa-user-lock" aria-hidden />}
              onClick={() => setPermissionsOpen(true)}
            >
              Permissions
            </Button>
          ) : null}
          {!importMode ? (
            <Button
              size="xs"
              variant="default"
              leftSection={<i className="fa fa-download" aria-hidden />}
              onClick={downloadAsDocx}
            >
              Download .docx
            </Button>
          ) : null}
        </Group>
      )}
      // Capture the EditorView so the side panel can dispatch
      // transactions to insert binding placeholders at the cursor.
      onEditorViewReady={(view) => {
        // docx-editor can call this repeatedly with the same view (the
        // prop is an inline arrow). Guard so we wrap dispatch + run the
        // one-shot sync exactly once per view instance — otherwise we'd
        // loop and dispatch into a half-updated view.
        if (editorViewRef.current === view) return;
        editorViewRef.current = view;
        makeAcceptRejectModeSafe(view);
        // Import mode: start watching for the parse to settle. This is the
        // trigger that actually fires — unlike onChange.
        scheduleImportFinalize(view);
        // One-shot binding sync for the case where bindings were already
        // loaded before the view mounted. Deferred a tick so we don't
        // dispatch into docx-editor's mount transactions.
        if (!importMode && !previewMode && role === "editor") {
          window.setTimeout(() => {
            if (editorViewRef.current === view) {
              runBindingSync(view, bindingRowsRef.current, bindingsLoadedRef.current);
            }
          }, 0);
        }
      }}
      // Capture the imperative ref so the title-bar Download button +
      // docx-editor's own File → Save menu both flow through one
      // download helper. Note: in import mode we still capture the
      // ref (so the user can download a re-export of the imported doc
      // before finalize), but the save() output represents the parsed
      // PM state, not the original .docx bytes verbatim.
      ref={editorRef}
      // The editor's File → Save menu fires `onSave` with the OOXML
      // buffer. Routing it through the same Blob+anchor helper as the
      // title-bar Download button keeps both entry points consistent.
      onSave={(buf) => {
        try {
          const blob = new Blob([buf], {
            type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
          });
          const url = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = url;
          const safeName = (documentTitle || "document").replace(/[\\/:*?"<>|]/g, "_");
          a.download = `${safeName}.docx`;
          document.body.appendChild(a);
          a.click();
          a.remove();
          URL.revokeObjectURL(url);
        } catch (err) {
          console.error("[export] onSave failed", err);
        }
      }}
      // Match Mantine surface tokens so the editor blends with the rest
      // of the app shell. The library inherits CSS vars for finer
      // control; this `style` just sets the outer container fill.
      style={{ height: "100%", background: "var(--mantine-color-gray-0)" }}
        />
      </Box>
      {/* Bindings side panel — visible by default; user can hide via
          the panel's own close button or the toolbar toggle. For
          viewers we still surface it (when open) so they can see live
          data their grants permit. Suppressed entirely in import mode. */}
      {activePanel === "bindings" && !importMode && !previewMode ? (
        <BindingsSidePanel
          documentId={documentId}
          canEdit={role === "editor"}
          onInsert={insertBindingPlaceholder}
          onHoverBinding={(id) => setHoveredBinding(editorViewRef.current, id)}
          onNavigateBinding={(id) => scrollToBinding(editorViewRef.current, id)}
          onClose={() => setActivePanel(null)}
        />
      ) : null}
      {activePanel === "versions" && !importMode && !previewMode ? (
        <VersionHistorySidePanel
          documentId={documentId}
          onClose={() => setActivePanel(null)}
        />
      ) : null}
      {!importMode && !previewMode ? (
        <PermissionsDialog
          kind="documents"
          resourceId={documentId}
          resourceName={documentTitle}
          opened={permissionsOpen}
          onClose={() => setPermissionsOpen(false)}
        />
      ) : null}
    </Box>
  );
}
