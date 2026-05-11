import { useCallback, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Code, Group, Paper, Stack, Text, Title } from "@mantine/core";
import { DataTable } from "@/components/data-table/DataTable";
import { listAssignedToMe } from "@/api/records";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import {
  listMyAssignedTasks,
  getTaskFormConfig,
  TaskFormConfig
} from "@/api/executions";
import { taskFormConfigQueryKey, useCompleteTask } from "@/hooks/useExecutions";
import { useBusConnection } from "@/hooks/useBusConnection";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { RecordModel, RecordType } from "@/types/records";
import { FlowableTaskSummary } from "@/types/flowable";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { findIcon, preferredStyle, stripFaPrefix } from "@/lib/faIcons";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import SimpleCompleteTaskModal from "@/components/workflow/SimpleCompleteTaskModal";
import GatewayChoiceModal from "@/components/workflow/GatewayChoiceModal";
import TaskFormModal from "@/components/workflow/TaskFormModal";

// Cap the assigned-records preload — beyond this the auto-mode probe switches
// the table to server mode and fetches per page instead. The workflow-task
// API doesn't paginate so we always pull the full task set.
const RECORD_PRELOAD = 1000;
const COLUMN_WIDTHS = ["24%", "10%", "16%", "20%", "8%", "14%", "8%"];
const QUERY_KEY = ["home", "my-tasks"] as const;

type TaskRow =
  | {
      kind: "record";
      id: string;
      sortKey: number;
      record: RecordModel;
      type: RecordType | null;
    }
  | {
      kind: "workflow";
      id: string;
      sortKey: number;
      task: FlowableTaskSummary;
    };

export default function MyTasksPanel() {
  const qc = useQueryClient();
  const { data: types = [] } = useRecordTypes(true);
  const { data: statusAppearance = [] } = useStatusAppearance();
  const completeTask = useCompleteTask();
  const navigate = useNavigate();
  const [openingTaskId, setOpeningTaskId] = useState<string | null>(null);
  const [activeTaskConfig, setActiveTaskConfig] = useState<TaskFormConfig | null>(null);
  const [openError, setOpenError] = useState<string | null>(null);

  const typesById = useMemo(() => {
    const map = new Map<string, RecordType>();
    for (const t of types) map.set(t.id, t);
    return map;
  }, [types]);

  // Refetch on any record or workflow-execution bus event. The server-side
  // /assigned-to-me endpoints already filter by the current user, so we don't
  // need to inspect payloads to decide whether to act — assignments and
  // reassignments both flow through these topics. Team Tasks is invalidated
  // too since reassignments may move work in or out of a supervisee's queue.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      const topic = msg.topic ?? "";
      if (topic.startsWith("record.") || topic.startsWith("workflow.execution")) {
        qc.invalidateQueries({ queryKey: QUERY_KEY });
        qc.invalidateQueries({ queryKey: ["home", "team-tasks"] });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  // Fan out to both endpoints in parallel and merge into the discriminated
  // TaskRow union. Sorted by activity time so the most-recent items rise.
  const loadAll = useCallback(async (): Promise<TaskRow[]> => {
    const [recordsPage, workflowTasks] = await Promise.all([
      listAssignedToMe({
        page: 0,
        pageSize: RECORD_PRELOAD,
        sort: "updated_desc",
        includeArchived: false
      }),
      listMyAssignedTasks()
    ]);
    const recordRows: TaskRow[] = recordsPage.items.map((rec) => ({
      kind: "record",
      id: `record:${rec.id}`,
      sortKey: parseTime(rec.updatedAtUtc),
      record: rec,
      type: typesById.get(rec.recordTypeId) ?? null
    }));
    const workflowRows: TaskRow[] = workflowTasks.map((task) => ({
      kind: "workflow",
      id: `workflow:${task.id}`,
      sortKey: parseTime(task.createdAtUtc),
      task
    }));
    return [...recordRows, ...workflowRows].sort((a, b) => b.sortKey - a.sortKey);
  }, [typesById]);

  // Clicking Open dispatches on the task's userForm config (set in
  // Workflow Studio). Simple → confirm modal in place, Modal → form modal
  // in place, Page → navigate to the dedicated task-form route.
  const onOpenTask = async (taskId: string) => {
    setOpeningTaskId(taskId);
    setOpenError(null);
    try {
      const config = await qc.fetchQuery({
        queryKey: taskFormConfigQueryKey(taskId),
        queryFn: ({ signal }) => getTaskFormConfig(taskId, signal)
      });
      if (!config) {
        setOpenError("Task not found or already completed.");
        return;
      }
      if (config.mode === "page") {
        navigate(`/workflow-tasks/${encodeURIComponent(taskId)}/form`);
        return;
      }
      setActiveTaskConfig(config);
    } catch (err) {
      setOpenError(describeError(err));
    } finally {
      setOpeningTaskId(null);
    }
  };

  const closeActiveTask = () => setActiveTaskConfig(null);

  const completeFromModal = useCallback(
    async (taskId: string, variables?: Record<string, unknown>) => {
      await completeTask.mutateAsync({ taskId, variables });
    },
    [completeTask]
  );

  const columns = useMemo<ColumnDef<TaskRow>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row: TaskRow) =>
          row.kind === "record"
            ? `${row.record.key} ${row.record.name}`
            : (row.task.processInstanceName ??
              row.task.processDefinitionName ??
              row.task.processDefinitionId ??
              row.task.name ??
              row.task.id),
        header: "Name",
        cell: ({ row }) =>
          row.original.kind === "record"
            ? (
                <Link to={`/record/${row.original.record.key}`} style={{ textDecoration: "none" }}>
                  <Group gap="xs" wrap="nowrap">
                    <Code>{row.original.record.key}</Code>
                    <Text>{row.original.record.name}</Text>
                  </Group>
                </Link>
              )
            : (() => {
                const task = row.original.task;
                const name =
                  task.processInstanceName ??
                  task.processDefinitionName ??
                  task.processDefinitionId ??
                  task.name ??
                  task.id;
                return task.processInstanceId ? (
                  <Link
                    to={`/executions/${task.processInstanceId}`}
                    style={{ textDecoration: "none" }}
                  >
                    {name}
                  </Link>
                ) : (
                  <Text>{name}</Text>
                );
              })()
      },
      {
        id: "status",
        accessorFn: (row: TaskRow) =>
          row.kind === "record"
            ? (row.record.status ?? "")
            : (row.task.name?.trim() ? row.task.name : (row.task.taskDefinitionKey ?? "")),
        header: "Status",
        cell: ({ row }) =>
          row.original.kind === "record"
            ? renderRecordStatus(row.original.record.status, statusAppearance)
            : renderWorkflowStatus(row.original.task, statusAppearance)
      },
      {
        id: "type",
        accessorFn: (row: TaskRow) =>
          row.kind === "record"
            ? (row.type?.name ?? "Unknown")
            : (row.task.processDefinitionName ?? row.task.processDefinitionId ?? "Workflow"),
        header: "Type",
        cell: ({ row }) =>
          row.original.kind === "record"
            ? renderRecordType(row.original.type)
            : renderWorkflowType(row.original.task)
      },
      {
        id: "description",
        accessorFn: (row: TaskRow) =>
          row.kind === "record" ? (readDescription(row.record.values) ?? "") : "",
        header: "Description",
        cell: ({ row }) =>
          row.original.kind === "record" ? renderRecordDescription(row.original.record) : <Dim />
      },
      {
        id: "dueDate",
        accessorFn: (row: TaskRow) =>
          row.kind === "record" ? (row.record.dueDate ?? "") : (row.task.dueDate ?? ""),
        header: "Due Date",
        cell: ({ row }) =>
          row.original.kind === "record"
            ? renderRecordDueDate(row.original.record.dueDate)
            : renderWorkflowDueDate(row.original.task.dueDate)
      },
      {
        id: "lastUpdated",
        accessorFn: (row: TaskRow) => row.sortKey,
        header: "Last Updated",
        cell: ({ row }) =>
          row.original.kind === "record"
            ? formatWhen(row.original.record.updatedAtUtc)
            : formatWhen(row.original.task.createdAtUtc)
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        cell: ({ row }) => {
          const r = row.original;
          if (r.kind !== "workflow") return null;
          return (
            <Button
              size="xs"
              onClick={() => onOpenTask(r.task.id)}
              loading={openingTaskId === r.task.id}
            >
              Open
            </Button>
          );
        }
      }
    ],
    [statusAppearance, openingTaskId]
  );

  return (
    <Paper withBorder radius="md" p="md">
      <Stack gap="sm">
        <Group gap="xs">
          <i className="fa fa-user-check" />
          <Title order={4}>My Tasks</Title>
        </Group>
        {openError && (
          <Alert color="red" variant="light">
            {openError}
          </Alert>
        )}
        <DataTable<TaskRow>
          queryKey={QUERY_KEY}
          mode="client"
          loadAll={loadAll}
          columns={columns}
          columnWidths={COLUMN_WIDTHS}
          rowKey={(r) => r.id}
          searchPlaceholder="Search my tasks…"
          emptyMessage="Nothing is assigned to you right now."
          initialSort={[{ id: "lastUpdated", desc: true }]}
        />
      </Stack>

      {activeTaskConfig?.mode === "simple" &&
        (activeTaskConfig.gatewayChoices && activeTaskConfig.gatewayChoices.length > 0 ? (
          <GatewayChoiceModal
            config={activeTaskConfig}
            onClose={closeActiveTask}
            onComplete={(taskId, variables) => completeFromModal(taskId, variables)}
          />
        ) : (
          <SimpleCompleteTaskModal
            config={activeTaskConfig}
            onClose={closeActiveTask}
            onComplete={(taskId) => completeFromModal(taskId)}
          />
        ))}
      {activeTaskConfig?.mode === "modal" && (
        <TaskFormModal
          config={activeTaskConfig}
          onClose={closeActiveTask}
          onComplete={(taskId, variables) => completeFromModal(taskId, variables)}
        />
      )}
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

function renderRecordStatus(
  status: string | null | undefined,
  entries: StatusAppearanceEntry[]
) {
  if (!status) return <Dim />;
  return <StatusBadge status={status} entries={entries} />;
}

function renderWorkflowStatus(task: FlowableTaskSummary, entries: StatusAppearanceEntry[]) {
  const activeNode = task.name?.trim() ? task.name : task.taskDefinitionKey ?? null;
  if (!activeNode) return <Dim />;
  return <StatusBadge status={activeNode} entries={entries} />;
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

function renderRecordType(type: RecordType | null) {
  if (!type) {
    return (
      <Text size="sm" c="dimmed">
        Unknown
      </Text>
    );
  }
  return (
    <Group gap="xs" wrap="nowrap">
      {type.icon ? (
        <i
          className={resolveIconClass(type.icon)}
          style={type.color ? { color: type.color } : undefined}
          aria-hidden
        />
      ) : null}
      <span>{type.name}</span>
    </Group>
  );
}

function renderWorkflowType(task: FlowableTaskSummary) {
  return (
    <Group gap="xs" wrap="nowrap">
      <i className="fa fa-diagram-project" />
      <span>{task.processDefinitionName ?? task.processDefinitionId ?? "Workflow"}</span>
    </Group>
  );
}

function renderRecordDescription(record: RecordModel) {
  const description = readDescription(record.values);
  if (!description) return <Dim />;
  return <Text size="sm">{description}</Text>;
}

function renderRecordDueDate(dueDate: string | null | undefined) {
  if (!dueDate) return <Dim />;
  return <span>{formatDate(dueDate)}</span>;
}

function renderWorkflowDueDate(dueDate: string | null | undefined) {
  if (!dueDate) return <Dim />;
  return <span>{formatDateTime(dueDate)}</span>;
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}

function resolveIconClass(icon: string): string {
  const found = findIcon(icon);
  if (found) return `${preferredStyle(found)} fa-${found.name}`;
  const name = stripFaPrefix(icon);
  return `fa-solid fa-${name}`;
}

function readDescription(values: Record<string, unknown>): string | null {
  const raw = values?.description ?? values?.Description;
  if (typeof raw !== "string") return null;
  const trimmed = raw.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function parseTime(iso: string | null | undefined): number {
  if (!iso) return 0;
  const t = new Date(iso).getTime();
  return Number.isNaN(t) ? 0 : t;
}

function formatWhen(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}
