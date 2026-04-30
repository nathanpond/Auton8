import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { NotificationModel } from "@/api/notifications";
import {
  useAllNotifications,
  useMarkAllNotificationsRead,
  useMarkNotificationRead
} from "@/hooks/useNotifications";

export default function Notifications() {
  const { data, isLoading, isError, error } = useAllNotifications();
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const navigate = useNavigate();

  const items = data?.items ?? [];
  const unreadCount = data?.unreadCount ?? 0;

  const handleRowClick = (n: NotificationModel) => {
    if (!n.isRead) markRead.mutate(n.id);
    if (n.linkPath) navigate(n.linkPath);
  };

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Notifications</h1>
          <p className="page-head-copy">
            Everything that's been routed to you — record assignments and workflow tasks waiting on
            you.
          </p>
        </div>
        {unreadCount > 0 && (
          <div className="page-head-actions">
            <button
              type="button"
              className="btn btn-outline-secondary"
              onClick={() => markAll.mutate()}
              disabled={markAll.isPending}
            >
              Mark all read
            </button>
          </div>
        )}
      </div>

      <div className="card">
        <div className="card-body p-0">
          {isLoading && <div className="p-4 text-muted">Loading…</div>}
          {isError && (
            <div className="p-4 text-danger">
              Failed to load notifications: {(error as Error)?.message ?? "unknown error"}
            </div>
          )}
          {!isLoading && !isError && items.length === 0 && (
            <div className="p-4 text-muted">No notifications yet.</div>
          )}
          {!isLoading && !isError && items.length > 0 && (
            <div className="table-responsive">
              <table className="table table-hover align-middle mb-0">
                <thead>
                  <tr>
                    <th style={{ width: "1.5rem" }}></th>
                    <th>Notification</th>
                    <th style={{ width: "12rem" }}>Type</th>
                    <th style={{ width: "12rem" }}>Received</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((n) => (
                    <NotificationRow
                      key={n.id}
                      notification={n}
                      onClick={() => handleRowClick(n)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </>
  );
}

function NotificationRow({
  notification,
  onClick
}: {
  notification: NotificationModel;
  onClick: () => void;
}) {
  const received = useMemo(
    () => new Date(notification.createdAtUtc).toLocaleString(),
    [notification.createdAtUtc]
  );
  const typeLabel = labelForKind(notification.kind);
  const isClickable = Boolean(notification.linkPath);
  return (
    <tr
      onClick={onClick}
      style={{ cursor: isClickable ? "pointer" : "default" }}
      className={notification.isRead ? "" : "fw-semibold"}
    >
      <td className="text-center">
        {!notification.isRead && (
          <span
            className="d-inline-block bg-primary rounded-circle"
            style={{ width: "0.5rem", height: "0.5rem" }}
            title="Unread"
          />
        )}
      </td>
      <td>
        <div>{notification.title}</div>
        <div className="small text-muted">{notification.body}</div>
      </td>
      <td className="small text-muted">{typeLabel}</td>
      <td className="small text-muted">{received}</td>
    </tr>
  );
}

function labelForKind(kind: string): string {
  switch (kind) {
    case "record.assigned":
      return "Record assignment";
    case "workflow.task.assigned":
      return "Workflow task";
    default:
      return kind;
  }
}
