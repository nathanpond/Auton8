import * as Y from "yjs";
import { HocuspocusProvider } from "@hocuspocus/provider";
import { useCreateBlockNote } from "@blocknote/react";
import {
  CommentsExtension,
  DefaultThreadStoreAuth,
  YjsThreadStore
} from "@blocknote/core/comments";
import type { ResolveUsersFn } from "./useResolveUsers";
import { ReadOnlyThreadStoreAuth } from "./ReadOnlyThreadStoreAuth";
import type { YjsRole } from "./ticket";
import { wrapThreadStoreWithAuditing } from "./commentAudit";
import { pageBlockNoteSchema } from "@/lib/blocknote/pageSchema";

export interface YjsCurrentUser {
  // Backend user Guid. Used as the principal for the YjsThreadStore's
  // DefaultThreadStoreAuth — owns the "I created this thread, I can
  // resolve it" check.
  id: string;
  displayName: string;
  // Hex color string for the user's cursor + presence label.
  color: string;
}

// Builds a BlockNote editor wired to a Yjs document fragment with
// comments enabled. MUST be called from a component that's only
// mounted once the YjsDocumentHandle from useYjsDocument is non-null
// AND useUsers() has resolved — otherwise CommentsExtension would
// cache "Unknown user" placeholders in its UserStore.
export function useBlockNoteWithYjs(args: {
  doc: Y.Doc;
  provider: HocuspocusProvider;
  currentUser: YjsCurrentUser;
  resolveUsers: ResolveUsersFn;
  // "editor" or "viewer". Server enforces via Hocuspocus's readOnly flag;
  // we use it client-side to swap the thread-store auth so write
  // affordances (Add comment, reply, react, resolve) are hidden for
  // viewers.
  role: YjsRole;
  // Doc name (`page:<guid>` or `note:<guid>`). Passed to the audit
  // wrapper so comment events land on the bus with the right pageId.
  documentName: string;
}) {
  // Threads live in the same Y.Doc as the body, in a Y.Map named
  // "threads" (BlockNote's recommended key). Sync rides the same
  // HocuspocusProvider connection — no separate transport.
  //
  // Auth role mapping:
  //   role === "editor"  → DefaultThreadStoreAuth(userId, "editor"):
  //                        full CRUD on threads + comments + reactions.
  //   role === "viewer"  → ReadOnlyThreadStoreAuth: all `can*` methods
  //                        return false → comment composer hidden,
  //                        reply form hidden, resolve / delete / react
  //                        actions hidden.
  const threadStoreAuth =
    args.role === "editor"
      ? new DefaultThreadStoreAuth(args.currentUser.id, "editor")
      : new ReadOnlyThreadStoreAuth();
  const rawThreadStore = new YjsThreadStore(
    args.currentUser.id,
    args.doc.getMap("threads"),
    threadStoreAuth
  );
  // Wrap with an auditing Proxy so successful comment writes also POST
  // to /api/yjs/comment-event, landing per-action audit events on the
  // bus alongside the existing PageUpdated webhook event.
  const threadStore = wrapThreadStoreWithAuditing(rawThreadStore, args.documentName);

  // Page editors get the extended schema that registers the `noteEmbed`
  // block (used by the `/note` slash command). Richtext-note editors keep
  // the default schema — same-page note embedding is meaningful only on a
  // page, and a default-schema editor opening a page would silently drop
  // any noteEmbed nodes. Derive from documentName so this can't drift
  // from the doc identity (no separate prop for callers to forget).
  const isPage = args.documentName.startsWith("page:");

  return useCreateBlockNote({
    ...(isPage ? { schema: pageBlockNoteSchema } : {}),
    collaboration: {
      // BlockNote types provider as `{ awareness?: Awareness | undefined }`
      // and HocuspocusProvider types it as `Awareness | null` (the null
      // state is theoretical — at runtime the constructor populates it
      // eagerly). Bridge with `?? undefined` to keep the types honest.
      provider: { awareness: args.provider.awareness ?? undefined },
      // BlockNote's recommended fragment key — matches the official docs.
      // All collaborators must use the same key for sync to work.
      fragment: args.doc.getXmlFragment("document-store"),
      user: { name: args.currentUser.displayName, color: args.currentUser.color },
      showCursorLabels: "activity"
    },
    extensions: [
      CommentsExtension({ threadStore, resolveUsers: args.resolveUsers })
    ],
    placeholders: { default: "Type to start writing…" }
  });
}
