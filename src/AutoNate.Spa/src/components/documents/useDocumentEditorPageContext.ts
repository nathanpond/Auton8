import { useCallback, useMemo, useRef } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import type {
  PageActionDefinition,
  PageActionRequest,
  PageActionResult,
  PageContextProviderEntry,
  PageSnapshot
} from "@/agent/pageContext/types";
import type { DocumentBindingDto } from "@/api/documentBindings";

// Registers the open document with the chatbot's PageContextRegistry so
// the agent panel inside the editor can see the document's title +
// bindings catalog + a body preview. Per-document pageKey (`document:UUID`)
// scopes the chat thread to this document — the editor's agentPanel queries
// the registry with the same key.
//
// v1 keeps this read-only: no onPageQuery (we don't expose the live PM
// state via tool calls) and no onPageAction (no AI-driven mutations into
// the doc yet). Phase 8b adds inline-assist by extending this with action
// handlers that dispatch ProseMirror transactions for AI proposals.

type Args = {
  documentId: string;
  documentTitle: string;
  // Pulls the current body text out of the live EditorView at snapshot
  // time. Implemented by the editor wrapper as
  // `view.state.doc.textBetween(0, view.state.doc.content.size, "\n")`.
  // Returns null when the view isn't mounted yet (initial render or
  // import-mode pre-finalize). Called from getSnapshot, which fires on
  // every chat send — so the snapshot is always fresh, no polling
  // or React state plumbing required.
  getBodyText: () => string | null;
  // Bindings catalog, sourced from the same React Query hook the bindings
  // side panel uses. We surface the kind + label + last resolved value so
  // the agent can reason about what data is referenced in the doc.
  bindings: DocumentBindingDto[];
  // Dispatches a chatbot-requested document mutation against the live
  // EditorView. Receives the structured action and returns a
  // PageActionResult — success carries a human-readable summary the
  // assistant relays back to the user. The editor wrapper supplies this
  // closure so it can operate on the captured ProseMirror view directly.
  // Omit (or return unsupported) to keep the document read-only for the
  // agent.
  onAction?: (req: PageActionRequest) => Promise<PageActionResult>;
};

// Hard cap on the body preview to keep snapshots well under the 64KB
// server-side limit. ~8KB of text is enough for a useful summary; longer
// docs get truncated with a clear marker.
const BODY_PREVIEW_LIMIT = 8000;

export function pageKeyForDocument(documentId: string): string {
  return `document:${documentId}`;
}

// Catalog the agent sees in the page snapshot under `data.actions`. The
// `name` matches what the agent passes to `apply_page_action`; the
// description is the contract the model reads. Keep these short + verb
// first so the agent's "describe-then-confirm" pattern reads naturally
// ("I'll **append a paragraph**…").
export const DOCUMENT_PAGE_ACTIONS: PageActionDefinition[] = [
  {
    name: "append_markdown",
    description:
      "Append rich content to the end of the open Word-style document. " +
      "Args: { markdown: string } — full GitHub-flavored Markdown. " +
      "Headings (`# H1`, `## H2`, `### H3`), bold (`**...**`), italic " +
      "(`*...*`), strikethrough, blockquotes, bullet + numbered lists, " +
      "tables, links, and inline code are mapped to the document's " +
      "matching paragraph styles + character formatting. Use this whenever " +
      "you'd otherwise consider 'append_paragraph' — pass the full " +
      "markdown source instead of pre-flattened plain text, so the " +
      "document picks up proper headings + lists + emphasis instead of " +
      "literal `**` and `##` characters in the body."
  },
  {
    name: "insert_markdown_at_cursor",
    description:
      "Insert rich Markdown content at the user's current cursor position. " +
      "Args: { markdown: string } — same Markdown contract as " +
      "append_markdown. Use this when the user explicitly asks for an " +
      "insertion at the cursor; otherwise prefer append_markdown."
  }
];

export function useDocumentEditorPageContext({
  documentId,
  documentTitle,
  getBodyText,
  bindings,
  onAction
}: Args): void {
  const pageKey = pageKeyForDocument(documentId);

  // getBodyText typically arrives as an inline arrow, which would churn
  // identity across renders and re-register the provider every time.
  // Pin it through a ref so the registered entry stays stable while
  // still reading the freshest body at snapshot time.
  const bodyTextRef = useRef(getBodyText);
  bodyTextRef.current = getBodyText;
  const onActionRef = useRef(onAction);
  onActionRef.current = onAction;

  const getSnapshot = useCallback((): PageSnapshot => {
    const raw = bodyTextRef.current();
    const trimmedPreview =
      raw == null
        ? null
        : raw.length > BODY_PREVIEW_LIMIT
          ? `${raw.slice(0, BODY_PREVIEW_LIMIT)}…[truncated; full body lives in the live editor]`
          : raw;

    return {
      pageKey,
      schemaVersion: 1,
      version: hashFingerprint(documentTitle, raw, bindings),
      // Forceful summary — included verbatim in the agent's system
      // prompt. The agent has access to notes-creation tools
      // (create_page_from_markdown, etc.) that DO NOT belong on this
      // surface; without an explicit policy here it sometimes picks
      // those over the document-scoped apply_page_action and ends up
      // creating a notebook page when the user asked to mutate the
      // document. Be loud and specific about the right tool for any
      // content-insertion request.
      summary:
        `Open Word-style document "${documentTitle}" (docx-editor; NOT a notes page or a notebook). ` +
        `Carries ${bindings.length} live data binding${bindings.length === 1 ? "" : "s"}. ` +
        `Any request to add, append, or insert content into this document MUST go through ` +
        `apply_page_action — use action="append_markdown" to append rich content to the end ` +
        `or action="insert_markdown_at_cursor" to insert at the user's cursor (both take ` +
        `{ markdown: string }). NEVER use create_page_from_markdown or any other ` +
        `notes/notebook-creation skill on this page — creating a new notebook page is the ` +
        `wrong feature for a document; doing so leaves the user's open document unchanged ` +
        `and creates a stray note in their notebook.`,
      data: {
        documentId,
        title: documentTitle,
        // Body preview is plain text — the model doesn't need PM JSON for
        // chat-with-document. Phase 10's node-view refactor will let us
        // safely include resolved binding values inline here too.
        bodyPreview: trimmedPreview,
        bodyPreviewTruncated: raw != null && raw.length > BODY_PREVIEW_LIMIT,
        bindings: bindings.map((b) => ({
          id: b.id,
          kind: b.kind,
          label: b.label ?? null,
          // The resolved value can be large (AQL table rows); cap each
          // binding's serialized form so a single fat table doesn't blow
          // the 64KB snapshot ceiling. Front-of-list strategy keeps
          // simple values intact and only truncates heavy ones.
          lastResolvedValueJsonb:
            b.lastResolvedValueJsonb && b.lastResolvedValueJsonb.length > 2000
              ? `${b.lastResolvedValueJsonb.slice(0, 2000)}…`
              : b.lastResolvedValueJsonb
        }))
      }
    };
  }, [pageKey, documentId, documentTitle, bindings]);

  // Per-action handler bridged to the editor's ProseMirror view via the
  // ref so the provider entry stays referentially stable. We declare the
  // action catalog on the provider so PageContextRegistry includes it in
  // `data.actions` automatically — same plumbing the form-fill defaults
  // ride on.
  const handlePageAction = useCallback(
    async (req: PageActionRequest): Promise<PageActionResult> => {
      const dispatch = onActionRef.current;
      if (!dispatch) {
        return {
          ok: false,
          error: "unsupported_action",
          message: "The document editor isn't mounted yet; try again in a moment."
        };
      }
      return dispatch(req);
    },
    []
  );

  const entry = useMemo<PageContextProviderEntry>(
    () => ({
      pageKey,
      getSnapshot,
      actions: DOCUMENT_PAGE_ACTIONS,
      onPageAction: handlePageAction
    }),
    [pageKey, getSnapshot, handlePageAction]
  );

  useRegisterPageContext(entry);
}

// Cheap rolling hash so the snapshot's `version` field bumps when any
// observable input changes. Not cryptographic — just a stable integer
// the registry / server can use to invalidate caches.
function hashFingerprint(
  title: string,
  preview: string | null,
  bindings: DocumentBindingDto[]
): number {
  let h = 5381;
  const mix = (s: string) => {
    for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
  };
  mix(title);
  if (preview) mix(preview);
  for (const b of bindings) {
    mix(b.id);
    mix(b.kind);
    if (b.lastResolvedValueJsonb) mix(b.lastResolvedValueJsonb);
  }
  return h >>> 0;
}
