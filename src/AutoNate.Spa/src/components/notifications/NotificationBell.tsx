import { useMemo } from "react";
import { Link, useNavigate } from "react-router-dom";
import { NotificationModel } from "@/api/notifications";
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useRecentNotifications
} from "@/hooks/useNotifications";

export default function NotificationBell() {
  const { data } = useRecentNotifications();
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const navigate = useNavigate();

  const items = data?.items ?? [];
  const unreadCount = data?.unreadCount ?? 0;

  // Cap badge at 99+ — bell icon space is tight and the exact count past 99
  // isn't actionable.
  const badgeText = useMemo(() => {
    if (unreadCount <= 0) return null;
    if (unreadCount > 99) return "99+";
    return String(unreadCount);
  }, [unreadCount]);

  const handleItemClick = (n: NotificationModel) => {
    if (!n.isRead) {
      markRead.mutate(n.id);
    }
    if (n.linkPath) {
      navigate(n.linkPath);
    }
  };

  return (
    <div className="menu-item dropdown">
      <a
        href="#"
        className="menu-link menu-link-tight"
        data-bs-toggle="dropdown"
        aria-expanded="false"
        title="Notifications"
        onClick={(e) => e.preventDefault()}
      >
        <div className="menu-icon notification-bell-icon">
          <i className="fa fa-bell"></i>
          {badgeText !== null && (
            <span className="notification-bell-badge">{badgeText}</span>
          )}
        </div>
      </a>
      <div
        className="dropdown-menu dropdown-menu-end me-1 p-0"
        style={{ minWidth: "20rem", maxWidth: "24rem" }}
      >
        <div className="d-flex justify-content-between align-items-center px-3 py-2 border-bottom">
          <strong>Notifications</strong>
          {unreadCount > 0 && (
            <button
              type="button"
              className="btn btn-link btn-sm p-0"
              onClick={() => markAll.mutate()}
              disabled={markAll.isPending}
            >
              Mark all read
            </button>
          )}
        </div>
        <div style={{ maxHeight: "24rem", overflowY: "auto" }}>
          {items.length === 0 && (
            <div className="px-3 py-4 text-center text-muted small">No notifications.</div>
          )}
          {items.map((item) => (
            <NotificationDropdownRow
              key={item.id}
              notification={item}
              onClick={() => handleItemClick(item)}
            />
          ))}
        </div>
        <div className="border-top">
          <Link
            to="/notifications"
            className="dropdown-item text-center small py-2"
          >
            View All Notifications
          </Link>
        </div>
      </div>
    </div>
  );
}

function NotificationDropdownRow({
  notification,
  onClick
}: {
  notification: NotificationModel;
  onClick: () => void;
}) {
  const ago = useMemo(() => formatRelative(notification.createdAtUtc), [notification.createdAtUtc]);
  return (
    <a
      href={notification.linkPath ?? "#"}
      className={`dropdown-item d-flex flex-column gap-1 py-2 px-3 ${notification.isRead ? "" : "fw-semibold"}`}
      onClick={(e) => {
        e.preventDefault();
        onClick();
      }}
      style={{ whiteSpace: "normal" }}
    >
      <div className="d-flex justify-content-between align-items-start gap-2">
        <span className="small">{notification.title}</span>
        <span className="text-muted" style={{ fontSize: "0.7rem" }}>
          {ago}
        </span>
      </div>
      <span className="small text-muted text-truncate">
        {notification.body}
      </span>
    </a>
  );
}

// Compact relative time for the dropdown — full timestamps go on the
// /notifications page where there's room for them.
export function formatRelative(iso: string): string {
  const ts = Date.parse(iso);
  if (Number.isNaN(ts)) return "";
  const diffMs = Date.now() - ts;
  const minutes = Math.round(diffMs / 60_000);
  if (minutes < 1) return "now";
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days}d`;
  const weeks = Math.round(days / 7);
  if (weeks < 5) return `${weeks}w`;
  return new Date(ts).toLocaleDateString();
}

