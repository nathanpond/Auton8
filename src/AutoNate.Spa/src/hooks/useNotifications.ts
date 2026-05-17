import { useEffect, useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  NotificationListResponse,
  NotificationModel,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead
} from "@/api/notifications";
import { useBusSubscription } from "./useBusSubscription";
import { useMe } from "./useMe";

const RECENT_LIMIT = 10;

export const notificationsRecentKey = ["notifications", "recent"] as const;
export const notificationsAllKey = ["notifications", "all"] as const;

export function useRecentNotifications(enabled = true) {
  return useQuery<NotificationListResponse>({
    queryKey: notificationsRecentKey,
    queryFn: ({ signal }) => listNotifications({ limit: RECENT_LIMIT }, signal),
    enabled,
    staleTime: 30_000
  });
}

export function useAllNotifications(enabled = true) {
  return useQuery<NotificationListResponse>({
    queryKey: notificationsAllKey,
    queryFn: ({ signal }) => listNotifications({}, signal),
    enabled
  });
}

export function useMarkNotificationRead() {
  const qc = useQueryClient();
  return useMutation<NotificationModel, Error, string>({
    mutationFn: (id) => markNotificationRead(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["notifications"] });
    }
  });
}

export function useMarkAllNotificationsRead() {
  const qc = useQueryClient();
  return useMutation<number, Error, void>({
    mutationFn: () => markAllNotificationsRead(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["notifications"] });
    }
  });
}

// Subscribes to the per-user notification channel on the scoped /ws/bus-watcher
// stream and refreshes the react-query caches whenever an event arrives.
// Mounted once at the shell level so live updates flow regardless of which
// page the user is on. Server-side gates ensure only this user's events are
// ever delivered — no client-side userId filtering is needed.
export function useNotificationLiveUpdates(enabled = true) {
  const { data: me } = useMe();
  const qc = useQueryClient();
  const userId = me && me.authenticated ? me.userId : null;

  const channels = useMemo(
    () => (userId ? [`notification:user:${userId}`] : []),
    [userId],
  );

  useBusSubscription(
    channels,
    (event) => {
      if (event.type !== "event") return;
      qc.invalidateQueries({ queryKey: ["notifications"] });
    },
    { enabled: enabled && Boolean(userId) },
  );

  // Keep the recent feed warm in case the websocket misses a frame after a
  // reconnect — every 30s tanstack-query refetch above also picks up gaps.
  useEffect(() => {
    if (!enabled || !userId) return;
    const handle = window.setInterval(() => {
      qc.invalidateQueries({ queryKey: notificationsRecentKey });
    }, 60_000);
    return () => window.clearInterval(handle);
  }, [enabled, userId, qc]);
}
