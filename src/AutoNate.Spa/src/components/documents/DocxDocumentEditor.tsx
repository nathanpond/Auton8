import { useMemo, type ReactNode } from "react";
import { DocxEditor, createEmptyDocument } from "@eigenpal/docx-editor-react";
import { ySyncPlugin, yCursorPlugin } from "y-prosemirror";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { useMe } from "@/hooks/useMe";
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

  // Build the y-prosemirror plugin list once the Y.Doc + awareness are
  // available. The fragment name "default" matches the sidecar
  // materializer; changing it would silently break body_jsonb snapshots.
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
  }, [handle]);

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
      // Schema seed only (externalContent skips the content load).
      document={SCHEMA_SEED_DOCUMENT}
      externalContent
      externalPlugins={externalPlugins}
      author={authorName}
      // Server-decided role drives read-only. The editor's own mode
      // toggle (editing / suggesting / viewing) is still user-driven
      // for editor-role users.
      readOnly={role !== "editor"}
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
