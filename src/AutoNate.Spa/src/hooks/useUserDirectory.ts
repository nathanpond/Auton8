import { useMemo } from "react";
import { useUsers } from "./useUsers";
import { LocalUser } from "@/types/flowable";

/**
 * Cached lookup from {@link LocalUser.userId} (Guid) to the user record. The
 * underlying useUsers query is cached by React Query, so widgets that resolve
 * many user ids on a single page only ever pay one network round-trip.
 */
export function useUserDirectory() {
  const { data: users = [], isLoading } = useUsers();

  const byId = useMemo(() => {
    const map = new Map<string, LocalUser>();
    for (const u of users) {
      map.set(u.userId.toLowerCase(), u);
    }
    return map;
  }, [users]);

  return {
    isLoading,
    /** Resolve a user Guid to its LocalUser, or null if unknown. */
    get(userId: string | null | undefined): LocalUser | null {
      if (!userId) return null;
      return byId.get(userId.toLowerCase()) ?? null;
    }
  };
}

/** Render-friendly label for a LocalUser. Prefers full name, then username. */
export function userDisplayName(user: LocalUser | null): string | null {
  if (!user) return null;
  const fullName = `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim();
  return fullName.length > 0 ? fullName : user.username;
}

/**
 * "Full Name (username)" — used in places (e.g. the workflow execution log)
 * where we want both the human name and the login id visible on the same
 * line. Falls back to whichever of name/username is set, then to the raw
 * fallbackId, then to "unknown".
 */
export function userFullDisplay(user: LocalUser | null, fallbackId?: string | null): string {
  if (!user) return fallbackId ?? "unknown";
  const fullName = `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim();
  if (fullName.length > 0 && user.username && fullName !== user.username) {
    return `${fullName} (${user.username})`;
  }
  if (fullName.length > 0) return fullName;
  return user.username ?? fallbackId ?? "unknown";
}
