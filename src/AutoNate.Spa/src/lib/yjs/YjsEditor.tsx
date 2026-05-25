import {
  BlockNoteViewEditor,
  FloatingComposerController,
  FloatingThreadController
} from "@blocknote/react";
import { BlockNoteView } from "@blocknote/mantine";
import { NotesThreadsSidebar } from "./NotesThreadsSidebar";
import { UserStorePrewarmer } from "./UserStorePrewarmer";
import { useMe } from "@/hooks/useMe";
import { useUsers } from "@/hooks/useUsers";
import type { YjsDocumentHandle } from "./useYjsDocument";
import type { YjsRole } from "./ticket";
import { useBlockNoteWithYjs } from "./useBlockNoteWithYjs";
import { useResolveUsersRef } from "./useResolveUsers";
import { userCursorColor } from "./userColor";
import { useEffect } from "react";
import { EmbedDepthContext } from "@/lib/blocknote/PageEditorContext";
import { NoteSlashController } from "@/lib/blocknote/NoteSlashController";
import {
  clearPageEditorSignal,
  setPageEditorSignal
} from "@/lib/blocknote/pageEditorSignal";
import {
  registerPageBodyEditor,
  unregisterPageBodyEditor
} from "@/lib/blocknote/pageBodyEditorRegistry";

interface Props {
  handle: YjsDocumentHandle;
  // True when the role permits writing. Combine with role for the
  // comments-auth branch. View-mode UIs flow this through directly to
  // BlockNoteView.
  editable: boolean;
  showSidebar: boolean;
  // Server-decided role. Passed through to useBlockNoteWithYjs so the
  // ThreadStoreAuth branches between full-write and read-only.
  role: YjsRole;
}

// Shared editor body. Owns the BlockNoteView layout and the comments
// extension wiring. Both VisualTextEditor and PageOverview render this
// once their Yjs handle is ready — keeping the duplicate body code DRY.
//
// Mounted only when both the Yjs handle AND the users directory are
// ready. The directory gate matters because BlockNote's UserStore
// caches the first `resolveUsers` result per id, and we don't want
// "Unknown user" cached during the directory-loading window.
export function YjsEditor({ handle, editable, showSidebar, role }: Props) {
  const me = useMe();
  const usersQuery = useUsers();
  const { resolve } = useResolveUsersRef();

  if (usersQuery.isLoading || !me.data) return null;

  const user = me.data.authenticated
    ? {
        userId: me.data.userId,
        displayName:
          [me.data.firstName, me.data.lastName].filter(Boolean).join(" ") ||
          me.data.username
      }
    : { userId: "anonymous", displayName: "Anonymous" };

  return (
    <YjsEditorInner
      // Re-key on role so the editor remounts when the role flips. The
      // CommentsExtension's ThreadStoreAuth is captured at editor-create
      // time; without a remount, switching role would leave the old auth
      // in place. Initial mount: viewer (useYjsDocument default) →
      // ticket-fetch → editor. Subsequent role changes are rare.
      key={role}
      handle={handle}
      editable={editable}
      showSidebar={showSidebar}
      role={role}
      currentUserId={user.userId}
      currentUserName={user.displayName}
      resolveUsers={resolve}
    />
  );
}

// Inner component so the editor only instantiates once per (handle, user)
// combo. useCreateBlockNote doesn't re-key on option changes — toggling
// showSidebar at this level just re-renders, not re-creates.
function YjsEditorInner({
  handle,
  editable,
  showSidebar,
  role,
  currentUserId,
  currentUserName,
  resolveUsers
}: Props & {
  currentUserId: string;
  currentUserName: string;
  resolveUsers: ReturnType<typeof useResolveUsersRef>["resolve"];
}) {
  const documentName = handle.provider.configuration.name;
  const editor = useBlockNoteWithYjs({
    doc: handle.doc,
    provider: handle.provider,
    currentUser: {
      id: currentUserId,
      displayName: currentUserName,
      color: userCursorColor(currentUserId)
    },
    resolveUsers,
    role,
    // The provider's `name` is the doc identifier we connected with
    // (`page:<guid>` or `note:<guid>`) — pass it through so comment
    // audit events land on the right resource server-side. The hook
    // also uses the `page:` prefix to gate the noteEmbed schema.
    documentName
  });

  // Parse the page id from the doc identifier for page editors. Only
  // these get the `/note` slash command and the noteEmbed render context.
  const pageId = documentName.startsWith("page:")
    ? documentName.slice("page:".length)
    : null;

  // Register the editor's pageId + editable state in a per-editor signal
  // store. The noteEmbed block render reads from this signal using the
  // BlockNote editor instance as the key. We don't rely on React context
  // because Tiptap's ReactNodeViewRenderer renders custom blocks in a
  // way that isn't guaranteed to inherit parent contexts (depending on
  // the Tiptap version's portal strategy). The signal sidesteps this:
  // the editor instance is reliably handed to the block render via
  // props, so the lookup always works.
  useEffect(() => {
    if (!pageId) return;
    setPageEditorSignal(editor, { pageId, editable });
    registerPageBodyEditor(pageId, editor);
    return () => {
      clearPageEditorSignal(editor);
      unregisterPageBodyEditor(pageId, editor);
    };
  }, [editor, pageId, editable]);

  // We take manual control of:
  //   - layout (`renderEditor={false}` + explicit <BlockNoteViewEditor />)
  //     so the threads sidebar sits beside the editor instead of below it
  //   - the comments UI (`comments={false}` to disable BlockNote's default
  //     auto-mounted thread/composer popovers) so we can choose which
  //     surfaces appear based on `showSidebar`.
  //
  // Behavior:
  //   - <FloatingComposerController /> is always mounted so the
  //     "Add comment" formatting-toolbar button can open the composer.
  //   - <FloatingThreadController /> is only mounted when the sidebar is
  //     CLOSED. When the sidebar is open, clicking a thread marker in
  //     the editor highlights the associated text (via the comments
  //     extension's selection decorations) but does NOT pop the inline
  //     thread — the sidebar already shows it.
  // Page editors mount the `/note` slash controller + the contexts the
  // noteEmbed block reads. Both providers are no-ops for note editors
  // (pageId is null), so we still wrap them universally to keep one
  // render path. EmbedDepthContext starts at 0 here; nested embeds bump
  // it inside their own render.
  const body = (
    <BlockNoteView
      editor={editor}
      editable={editable}
      theme="light"
      renderEditor={false}
      comments={false}
      // BlockNoteDefaultUI (auto-mounted by <BlockNoteViewEditor/>) registers
      // its own `/`-trigger SuggestionMenuController. Without this opt-out,
      // both it AND <NoteSlashController/> below render overlapping popovers
      // at the same position — visually one menu, but mouse clicks land on
      // the wrong layer and the selected item never inserts.
      slashMenu={false}
    >
      <UserStorePrewarmer />
      <div style={{ display: "flex", alignItems: "stretch", width: "100%" }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <BlockNoteViewEditor />
          {pageId && <NoteSlashController pageId={pageId} />}
          <FloatingComposerController />
          {!showSidebar && <FloatingThreadController />}
        </div>
        {showSidebar && (
          <aside
            style={{
              width: 400,
              flexShrink: 0,
              borderLeft: "1px solid #ced4da",
              background: "#fff",
              marginLeft: 16,
              // BlockNote's thread cards sometimes overflow horizontally
              // (long links / long usernames). Clip rather than scroll;
              // text inside already wraps at word boundaries.
              overflowX: "hidden"
            }}
          >
            <NotesThreadsSidebar />
          </aside>
        )}
      </div>
    </BlockNoteView>
  );

  return <EmbedDepthContext.Provider value={0}>{body}</EmbedDepthContext.Provider>;
}
