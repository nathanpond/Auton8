import { useEffect, useRef } from "react";
import type { User } from "@blocknote/core";
import { useUsers } from "@/hooks/useUsers";
import { userDisplayName } from "@/hooks/useUserDirectory";
import { LocalUser } from "@/types/flowable";
import { avatarUrl } from "./avatarUrl";

export type ResolveUsersFn = (userIds: string[]) => Promise<User[]>;

// Stable `resolveUsers` for BlockNote's CommentsExtension. BlockNote's
// UserStore captures the callback once at extension creation and caches
// results per-userId on first call, so we can't pass a freshly-bound
// closure on each render. Instead we hand out a permanent function that
// reads from a ref; the ref is kept current via useEffect against
// useUsers()'s React-Query data.
//
// The calling component (YjsEditor) gates editor mount on
// `useUsers().isLoading === false` — without that gate, the first lookup
// would land before the directory loads, return "Unknown user", and the
// UserStore cache would freeze that placeholder.
export function useResolveUsersRef(): { resolve: ResolveUsersFn } {
  const { data: users = [] } = useUsers();
  const dirRef = useRef<Map<string, LocalUser>>(new Map());

  useEffect(() => {
    const next = new Map<string, LocalUser>();
    for (const u of users) {
      next.set(u.userId.toLowerCase(), u);
    }
    dirRef.current = next;
  }, [users]);

  // Captured ONCE — BlockNote's UserStore stashes this reference. Reads
  // dirRef.current at call time, so updates to useUsers data are visible
  // to any user lookup that BlockNote hasn't yet cached.
  const resolveRef = useRef<ResolveUsersFn>(async (userIds) => {
    return userIds.map((id) => toUser(id, dirRef.current));
  });

  return { resolve: resolveRef.current };
}

function toUser(userId: string, directory: Map<string, LocalUser>): User {
  const local = directory.get(userId.toLowerCase());
  const displayName = local
    ? userDisplayName(local) ?? local.username
    : "Unknown user";
  return {
    id: userId,
    username: displayName,
    avatarUrl: avatarUrl(userId, displayName)
  };
}
