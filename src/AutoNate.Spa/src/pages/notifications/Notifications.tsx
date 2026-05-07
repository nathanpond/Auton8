import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
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

  const columns = useMemo<ColumnDef<NotificationModel>[]>(
    () => [
      {
        id: "unread-dot",
        header: "",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) =>
          row.original.isRead ? null : (
            <span
              className="d-inline-block bg-primary rounded-circle"
              style={{ width: "0.5rem", height: "0.5rem" }}
              title="Unread"
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
            <div className="small text-muted">{row.original.body}</div>
          </>
        )
      },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Type",
        cell: ({ row }) => (
          <span className="small text-muted">{labelForKind(row.original.kind)}</span>
        )
      },
      {
        id: "createdAtUtc",
        accessorKey: "createdAtUtc",
        header: "Received",
        cell: ({ row }) => (
          <span className="small text-muted">
            {new Date(row.original.createdAtUtc).toLocaleString()}
          </span>
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
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Notifications</h1>
          <p className="page-head-copy">
            Everything that's been routed to you — record assignments and workflow tasks waiting on
            you.
          </p>
        </div>
      </div>

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
          <button
            type="button"
            className="btn btn-outline-secondary btn-sm"
            onClick={() => markAll.mutate()}
            disabled={markAll.isPending}
          >
            Mark all read
          </button>
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
