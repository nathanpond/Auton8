import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import { Button, Text } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  NotificationModel,
  listNotificationsPage
} from "@/api/notifications";
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead
} from "@/hooks/useNotifications";
import {
  DataTable,
  DataTableFilterOption,
  DataTablePageRequest
} from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["4%", "60%", "16%", "20%"];

const FILTERS: DataTableFilterOption<NotificationModel>[] = [
  { id: "unread", label: "Unread", predicate: (n) => !n.isRead }
];

export default function Notifications() {
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const navigate = useNavigate();

  const handleRowClick = (n: NotificationModel) => {
    if (!n.isRead) markRead.mutate(n.id);
    if (n.linkPath) navigate(n.linkPath);
  };

  const columns = useMemo<DataTableColumn<NotificationModel>[]>(
    () => [
      {
        id: "unread-dot",
        header: "",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) =>
          row.original.isRead ? null : (
            <span
              title="Unread"
              style={{
                display: "inline-block",
                width: "0.5rem",
                height: "0.5rem",
                borderRadius: "50%",
                background: "var(--mantine-color-blue-filled)"
              }}
            />
          )
      },
      {
        id: "title",
        accessorKey: "title",
        header: "Notification",
        cell: ({ row }) => (
          <>
            <div>{row.original.title}</div>
            <Text size="sm" c="dimmed">
              {row.original.body}
            </Text>
          </>
        )
      },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Type",
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {labelForKind(row.original.kind)}
          </Text>
        )
      },
      {
        id: "createdAtUtc",
        accessorKey: "createdAtUtc",
        header: "Received",
        cell: ({ row }) => (
          <Text size="sm" c="dimmed" component="span">
            {new Date(row.original.createdAtUtc).toLocaleString()}
          </Text>
        )
      }
    ],
    []
  );

  const loadPage = async (req: DataTablePageRequest) => {
    const result = await listNotificationsPage({
      page: req.page,
      pageSize: req.pageSize,
      search: req.search || undefined,
      sort: req.sort?.id,
      sortDir: req.sort ? (req.sort.desc ? "desc" : "asc") : undefined,
      unreadOnly: req.filter === "unread"
    });
    return { items: result.items, totalCount: result.totalCount };
  };

  return (
    <>
      <PageHeader
        title="Notifications"
        description="Everything that's been routed to you — record assignments and workflow tasks waiting on you."
      />

      <DataTable<NotificationModel>
        mode="server"
        loadPage={loadPage}
        queryKey={["notifications", "all"]}
        columns={columns}
        rowKey={(n) => n.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "createdAtUtc", desc: true }]}
        searchPlaceholder="Search notifications…"
        emptyMessage="No notifications yet."
        loadingMessage="Loading notifications…"
        filters={FILTERS}
        onRowClick={handleRowClick}
        getRowAriaLabel={(n) => `Open ${n.title}`}
        getRowClassName={(n) => (n.isRead ? undefined : "notification-unread")}
        toolbarRight={
          <Button
            size="xs"
            variant="default"
            onClick={() => markAll.mutate()}
            loading={markAll.isPending}
          >
            Mark all read
          </Button>
        }
      />
    </>
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
