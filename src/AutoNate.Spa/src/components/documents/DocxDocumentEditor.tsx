import { useMemo, useRef, type ReactNode } from "react";
import { DocxEditor, createEmptyDocument } from "@eigenpal/docx-editor-react";
import { ySyncPlugin, yCursorPlugin } from "y-prosemirror";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { useMe } from "@/hooks/useMe";
import {
  useCreateDocumentComment,
  useDeleteDocumentComment,
  useDocumentComments,
  useReplyToDocumentComment,
  useResolveDocumentComment
} from "@/hooks/useDocumentComments";
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

export default function DocxDocumentEditor({
  documentId,
  documentTitle,
  onRenameDocument,
  titleBarRight
}: Props) {
  const yjsName = useMemo(() => `documents:${documentId}`, [documentId]);
  const { handle, role } = useYjsDocument(yjsName);
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
  const mode: "editing" | "suggesting" | "viewing" =
    role === "editor" ? "editing" : "viewing";
  const readOnly = role === "viewer";

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
    if (!handle) return [];
    const fragment = handle.doc.getXmlFragment("default");
    const plugins = [ySyncPlugin(fragment)];
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
  }, [handle, role]);

  // While the Yjs connection is establishing, render a placeholder so
  // the editor surface isn't blank — docx-editor's own placeholder
  // prop covers this once it mounts, but the very first paint can
  // race ahead of `handle` being set.
  if (!handle) {
    return (
      <div style={{ padding: 32, color: "var(--mantine-color-dimmed)" }}>
        Connecting to document…
      </div>
    );
  }

  return (
    <DocxEditor
      // Force a remount when role transitions. docx-editor latches the
      // readOnly prop at mount and ignores subsequent changes — without
      // this key the editor would stay locked at the pessimistic
      // initial "viewer" role even after the ticket fetch promotes the
      // user to "editor". Yjs state survives the remount because the
      // Y.Doc + provider live in useYjsDocument one level up.
      key={role}
      // Schema seed only (externalContent skips the content load).
      document={SCHEMA_SEED_DOCUMENT}
      externalContent
      externalPlugins={externalPlugins}
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
      // Surface the docx-editor's full Word-style toolbar.
      showToolbar
      showRuler
      showZoomControl
      // Title bar wiring: the docx-editor renders the doc name + an
      // optional right slot we use for the "Back to project" link.
      // Renames flow through our REST documents endpoint via the
      // parent's callback; the doc name itself is NOT a Yjs property.
      documentName={documentTitle}
      documentNameEditable={role === "editor" && Boolean(onRenameDocument)}
      onDocumentNameChange={onRenameDocument}
      renderTitleBarRight={titleBarRight ? () => titleBarRight : undefined}
      // Match Mantine surface tokens so the editor blends with the rest
      // of the app shell. The library inherits CSS vars for finer
      // control; this `style` just sets the outer container fill.
      style={{ height: "100%", background: "var(--mantine-color-gray-0)" }}
    />
  );
}
