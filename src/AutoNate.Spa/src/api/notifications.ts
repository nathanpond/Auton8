import { api } from "./client";

export type NotificationModel = {
  id: string;
  userId: string;
  kind: string;
  title: string;
  body: string;
  relatedEntityKind: string | null;
  relatedEntityId: string | null;
  linkPath: string | null;
  isRead: boolean;
  createdAtUtc: string;
  readAtUtc: string | null;
};

export type NotificationListResponse = {
  items: NotificationModel[];
  unreadCount: number;
};

const base = "/api/notifications";

export async function listNotifications(
  options: { limit?: number } = {},
  signal?: AbortSignal
): Promise<NotificationListResponse> {
  const params: Record<string, number> = {};
  if (typeof options.limit === "number") params.limit = options.limit;
  const { data } = await api.get<NotificationListResponse>(base, { params, signal });
  return data;
}

export async function getUnreadCount(signal?: AbortSignal): Promise<number> {
  const { data } = await api.get<{ unreadCount: number }>(`${base}/unread-count`, { signal });
  return data.unreadCount;
}

export async function markNotificationRead(id: string): Promise<NotificationModel> {
  const { data } = await api.post<NotificationModel>(`${base}/${id}/read`);
  return data;
}

export async function markAllNotificationsRead(): Promise<number> {
  const { data } = await api.post<{ updated: number }>(`${base}/mark-all-read`);
  return data.updated;
}

export type ListNotificationsPageRequest = {
  page: number;
  pageSize: number;
  search?: string;
  sort?: string;
  sortDir?: "asc" | "desc";
  unreadOnly?: boolean;
};

export type ListNotificationsPageResult = {
  items: NotificationModel[];
  totalCount: number;
  unreadCount: number;
};

export async function listNotificationsPage(
  req: ListNotificationsPageRequest,
  signal?: AbortSignal
): Promise<ListNotificationsPageResult> {
  const params: Record<string, string | number | boolean> = {
    page: req.page,
    pageSize: req.pageSize
  };
  if (req.search) params.q = req.search;
  if (req.sort) params.sort = req.sort;
  if (req.sortDir) params.sortDir = req.sortDir;
  if (req.unreadOnly) params.unreadOnly = true;
  const { data } = await api.get<ListNotificationsPageResult>(`${base}/page`, {
    params,
    signal
  });
  return data;
}
