import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import {
  Anchor,
  Box,
  Button,
  Divider,
  Group,
  Indicator,
  Menu,
  ScrollArea,
  Stack,
  Text,
  UnstyledButton
} from "@mantine/core";
import { Link } from "react-router-dom";
import {
  applyHeaderHover,
  clearHeaderHover,
  headerIconButtonStyle
} from "@/shell/headerStyles";
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
    <Menu position="bottom-end" width={320} shadow="md" closeOnItemClick={false}>
      <Menu.Target>
        <UnstyledButton
          aria-label="Notifications"
          title="Notifications"
          style={headerIconButtonStyle}
          onMouseEnter={applyHeaderHover}
          onMouseLeave={clearHeaderHover}
        >
          <Indicator
            label={badgeText ?? ""}
            disabled={badgeText === null}
            color="red"
            size={16}
            offset={2}
            inline
          >
            <i className="fa fa-bell" />
          </Indicator>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Group justify="space-between" px="sm" py="xs">
          <Text fw={600}>Notifications</Text>
          {unreadCount > 0 && (
            <Button
              variant="subtle"
              size="compact-xs"
              onClick={() => markAll.mutate()}
              loading={markAll.isPending}
            >
              Mark all read
            </Button>
          )}
        </Group>
        <Divider />
        <ScrollArea.Autosize mah={384}>
          {items.length === 0 ? (
            <Box px="md" py="lg">
              <Text size="sm" c="dimmed" ta="center">
                No notifications.
              </Text>
            </Box>
          ) : (
            <Stack gap={0}>
              {items.map((item) => (
                <NotificationDropdownRow
                  key={item.id}
                  notification={item}
                  onClick={() => handleItemClick(item)}
                />
              ))}
            </Stack>
          )}
        </ScrollArea.Autosize>
        <Divider />
        <Anchor
          component={Link}
          to="/notifications"
          ta="center"
          py="xs"
          display="block"
          size="sm"
          underline="never"
        >
          View All Notifications
        </Anchor>
      </Menu.Dropdown>
    </Menu>
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
    <Box
      component="button"
      type="button"
      onClick={onClick}
      px="sm"
      py="xs"
      style={{
        display: "block",
        width: "100%",
        textAlign: "left",
        background: "transparent",
        border: 0,
        cursor: "pointer",
        borderBottom: "1px solid var(--mantine-color-default-border)"
      }}
    >
      <Group justify="space-between" gap="xs" wrap="nowrap">
        <Text size="sm" fw={notification.isRead ? 400 : 600} style={{ flex: 1 }}>
          {notification.title}
        </Text>
        <Text size="xs" c="dimmed" style={{ whiteSpace: "nowrap" }}>
          {ago}
        </Text>
      </Group>
      <Text size="xs" c="dimmed" lineClamp={1}>
        {notification.body}
      </Text>
    </Box>
  );
}

// Compact relative time for the dropdown — full timestamps go on the
// /notifications page where there's room for them.
function formatRelative(iso: string): string {
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
