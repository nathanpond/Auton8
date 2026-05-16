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
  const threadStore = new YjsThreadStore(
    args.currentUser.id,
    args.doc.getMap("threads"),
    threadStoreAuth
  );

  return useCreateBlockNote({
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
