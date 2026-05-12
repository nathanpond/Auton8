import { useCallback, useMemo } from "react";
import { Link } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import { useQueryClient } from "@tanstack/react-query";
import { Badge, Code, Group, Paper, Text, Title } from "@mantine/core";
import { DataTable } from "@/components/data-table/DataTable";
import { listWatchedRecords, WatchedRecord } from "@/api/records";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { useBusConnection } from "@/hooks/useBusConnection";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import UserBadge from "@/pages/records/UserBadge";

// Cap the client-mode preload — beyond this the auto-mode probe switches
// the table to server mode and fetches per page instead.
const CLIENT_PRELOAD = 1000;
const COLUMN_WIDTHS = ["28%", "30%", "12%", "18%", "12%"];
const QUERY_KEY = ["home", "watched-records"] as const;

export default function WatchedRecordsPanel() {
  const qc = useQueryClient();
  const { data: statusAppearance = [] } = useStatusAppearance();

  // Refetch when records change so a watched record's status / due date /
  // name updates surface here without a manual reload.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      if ((msg.topic ?? "").startsWith("record.")) {
        qc.invalidateQueries({ queryKey: QUERY_KEY });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  const loadAll = useCallback(async () => {
    const page = await listWatchedRecords({ page: 0, pageSize: CLIENT_PRELOAD });
    return page.items;
  }, []);

  const loadPage = useCallback(
    async (req: { page: number; pageSize: number }) => {
      const page = await listWatchedRecords({ page: req.page, pageSize: req.pageSize });
      return { items: page.items, totalCount: page.totalCount };
    },
    []
  );

  const columns = useMemo<DataTableColumn<WatchedRecord>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Link to={`/record/${row.original.key}`} style={{ textDecoration: "none" }}>
            <Group gap="xs" wrap="nowrap">
              <Code>{row.original.key}</Code>
              <Text span>{row.original.name}</Text>
              {row.original.isArchived && (
                <Badge color="gray" variant="filled" size="sm">
                  Archived
                </Badge>
              )}
            </Group>
          </Link>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) =>
          row.original.description ? (
            <Text size="sm">{row.original.description}</Text>
          ) : (
            <Dim />
          )
      },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) =>
          row.original.status ? (
            <StatusBadge status={row.original.status} entries={statusAppearance} />
          ) : (
            <Dim />
          )
      },
      {
        id: "assignees",
        accessorFn: (r: WatchedRecord) => r.assigneeIds.join(", "),
        header: "Assigned To",
        cell: ({ row }) =>
          row.original.assigneeIds.length > 0 ? (
            <Group gap={4} wrap="wrap">
              {row.original.assigneeIds.map((id, i) => (
                <span key={id}>
                  <UserBadge userId={id} />
                  {i < row.original.assigneeIds.length - 1 ? "," : ""}
                </span>
              ))}
            </Group>
          ) : (
            <Text c="dimmed">Unassigned</Text>
          )
      },
      {
        id: "dueDate",
        accessorKey: "dueDate",
        header: "Due Date",
        cell: ({ row }) =>
          row.original.dueDate ? <span>{formatDate(row.original.dueDate)}</span> : <Dim />
      }
    ],
    [statusAppearance]
  );

  return (
    <Paper withBorder radius="md" p="md">
      <DataTable<WatchedRecord>
        queryKey={QUERY_KEY}
        mode="auto"
        loadAll={loadAll}
        loadPage={loadPage}
        columns={columns}
        columnWidths={COLUMN_WIDTHS}
        rowKey={(r) => r.id}
        searchPlaceholder="Search watched records…"
        emptyMessage='You aren&apos;t watching any records yet. Open a record and click "Watch" to add it here.'
        initialSort={[{ id: "name", desc: false }]}
        toolbarLeft={
          <Group gap="xs">
            <i className="fa fa-eye" />
            <Title order={4}>Watched Records</Title>
          </Group>
        }
      />
    </Paper>
  );
}

function Dim() {
  return (
    <Text c="dimmed" span>
      —
    </Text>
  );
}

function StatusBadge({
  status,
  entries
}: {
  status: string;
  entries: StatusAppearanceEntry[];
}) {
  const bg = resolveStatusBadgeColor(status, entries);
  const fg = badgeTextColor(bg);
  return (
    <Badge radius="xl" style={{ backgroundColor: bg, color: fg, border: 0 }}>
      {status}
    </Badge>
  );
}

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}
