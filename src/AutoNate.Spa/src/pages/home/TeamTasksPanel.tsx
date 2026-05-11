import { useCallback, useMemo } from "react";
import { Link } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Group, Paper, Stack, Text, Title } from "@mantine/core";
import { DataTable } from "@/components/data-table/DataTable";
import { listTeamAssignedTasks } from "@/api/executions";
import { useBusConnection } from "@/hooks/useBusConnection";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { FlowableTaskSummary } from "@/types/flowable";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import UserBadge from "@/pages/records/UserBadge";

const COLUMN_WIDTHS = ["26%", "12%", "16%", "14%", "10%", "14%", "8%"];
const QUERY_KEY = ["home", "team-tasks"] as const;

export default function TeamTasksPanel() {
  const qc = useQueryClient();
  const { data: statusAppearance = [] } = useStatusAppearance();

  // Re-fetch on workflow-execution bus events. A reassignment can move a task
  // in or out of the team queue, and the actor's own queue, so both keys are
  // invalidated together.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      const topic = msg.topic ?? "";
      if (topic.startsWith("workflow.execution")) {
        qc.invalidateQueries({ queryKey: QUERY_KEY });
        qc.invalidateQueries({ queryKey: ["home", "my-tasks"] });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  // listTeamAssignedTasks returns the full set (server doesn't paginate it),
  // so client mode is the right fit. The wrapper will sort + page + search
  // in-memory.
  const loadAll = useCallback(() => listTeamAssignedTasks(), []);

  const columns = useMemo<ColumnDef<FlowableTaskSummary>[]>(
    () => [
      {
        id: "name",
        accessorFn: (t: FlowableTaskSummary) =>
          t.processInstanceName ??
          t.processDefinitionName ??
          t.processDefinitionId ??
          t.name ??
          t.id,
        header: "Name",
        cell: ({ row }) => {
          const task = row.original;
          const workflowName =
            task.processInstanceName ??
            task.processDefinitionName ??
            task.processDefinitionId ??
            task.name ??
            task.id;
          return task.processInstanceId ? (
            <Link to={`/executions/${task.processInstanceId}`} style={{ textDecoration: "none" }}>
              {workflowName}
            </Link>
          ) : (
            <Text>{workflowName}</Text>
          );
        }
      },
      {
        id: "status",
        accessorFn: (t: FlowableTaskSummary) =>
          t.name?.trim() ? t.name : (t.taskDefinitionKey ?? ""),
        header: "Status",
        cell: ({ row }) => {
          const task = row.original;
          const activeNode = task.name?.trim() ? task.name : task.taskDefinitionKey ?? null;
          if (!activeNode) return <Dim />;
          return <StatusBadge status={activeNode} entries={statusAppearance} />;
        }
      },
      {
        id: "type",
        accessorFn: (t: FlowableTaskSummary) =>
          t.processDefinitionName ?? t.processDefinitionId ?? "Workflow",
        header: "Type",
        cell: ({ row }) => {
          const task = row.original;
          return (
            <Group gap="xs" wrap="nowrap">
              <i className="fa fa-diagram-project" />
              <span>{task.processDefinitionName ?? task.processDefinitionId ?? "Workflow"}</span>
            </Group>
          );
        }
      },
      {
        id: "assignee",
        accessorKey: "assignee",
        header: "Assignee",
        cell: ({ row }) => <UserBadge userId={row.original.assignee} />
      },
      {
        id: "dueDate",
        accessorKey: "dueDate",
        header: "Due Date",
        cell: ({ row }) =>
          row.original.dueDate ? <span>{formatDateTime(row.original.dueDate)}</span> : <Dim />
      },
      {
        id: "createdAtUtc",
        accessorKey: "createdAtUtc",
        header: "Last Updated",
        cell: ({ row }) => formatWhen(row.original.createdAtUtc)
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        cell: ({ row }) =>
          row.original.processInstanceId ? (
            <Button
              component={Link}
              to={`/executions/${row.original.processInstanceId}`}
              variant="outline"
              size="xs"
            >
              View
            </Button>
          ) : (
            <Dim />
          )
      }
    ],
    [statusAppearance]
  );

  return (
    <Paper withBorder radius="md" p="md">
      <Stack gap="sm">
        <Group gap="xs">
          <i className="fa fa-users" />
          <Title order={4}>Team Tasks</Title>
        </Group>
        <DataTable<FlowableTaskSummary>
          queryKey={QUERY_KEY}
          mode="client"
          loadAll={loadAll}
          columns={columns}
          columnWidths={COLUMN_WIDTHS}
          rowKey={(t) => t.id}
          searchPlaceholder="Search team tasks…"
          emptyMessage="No tasks are assigned to anyone you supervise."
          initialSort={[{ id: "createdAtUtc", desc: true }]}
        />
      </Stack>
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

function formatWhen(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}
