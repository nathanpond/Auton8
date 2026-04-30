import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  NotificationListResponse,
  NotificationModel,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead
} from "@/api/notifications";
import { BusMessageEnvelope, useBusConnection } from "./useBusConnection";
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

// Subscribes to the notification.* topic prefix on the bus and refreshes the
// react-query caches whenever an event arrives for the current user.
// Mounted once at the shell level so live updates flow regardless of which
// page the user is on.
export function useNotificationLiveUpdates(enabled = true) {
  const { data: me } = useMe();
  const qc = useQueryClient();
  const userId = me && me.authenticated ? me.userId : null;

  useBusConnection({
    enabled: enabled && Boolean(userId),
    topicPrefix: "notification.",
    onMessage: (envelope: BusMessageEnvelope) => {
      try {
        const event = JSON.parse(envelope.payload) as { userId?: string };
        if (event.userId && userId && event.userId === userId) {
          qc.invalidateQueries({ queryKey: ["notifications"] });
        }
      } catch {
        // BusWatcher pretty-prints payloads on the way through; if it isn't
        // JSON we have nothing to filter on. Drop silently.
      }
    }
  });

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
