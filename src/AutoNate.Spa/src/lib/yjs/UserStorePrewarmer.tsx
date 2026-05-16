import { useEffect } from "react";
import { CommentsExtension } from "@blocknote/core/comments";
import { useExtension } from "@blocknote/react";
import { useUsers } from "@/hooks/useUsers";

// Pre-loads every known user into BlockNote's CommentsExtension UserStore
// so synchronous lookups (e.g. `Comments.tsx`'s read of the
// `thread.resolvedBy` user) find their entry on the very first render
// after a thread is resolved.
//
// Without this, BlockNote's Comments component throws
//   "User <id> resolved thread <id>, but their data could not be found."
// because its `useUsers([resolvedBy])` hook reads getUser() synchronously
// in the same render that *starts* the async loadUsers fetch — getUser
// returns undefined → throw.
//
// Mounted inside <BlockNoteView> so it has CommentsExtension context.
// Renders nothing.
export function UserStorePrewarmer() {
  const comments = useExtension(CommentsExtension);
  const { data: users } = useUsers();

  useEffect(() => {
    if (!users || users.length === 0) return;
    void comments.userStore.loadUsers(users.map((u) => u.userId));
  }, [comments, users]);

  return null;
}
