import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Paper,
  Stack,
  Text,
  Title,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  PipelineRun,
  PipelineRunStep,
  PipelineRunStepLog,
  cancelPipelineRun,
  getPipeline,
  getPipelineRun,
  listPipelineRuns,
  retryPipelineRun
} from "@/api/pipelines";

const STATUS_COLOR: Record<PipelineRun["status"], string> = {
  Queued: "gray",
  Running: "blue",
  Succeeded: "green",
  Failed: "red",
  Cancelled: "yellow"
};

const RUN_COLUMN_WIDTHS = ["140px", "180px", "180px", "180px", "120px", "1fr", "100px"];
const STEP_COLUMN_WIDTHS = ["1fr", "140px", "140px", "100px", "2fr", "80px"];

const ACTIVE_RUN_STATUSES: PipelineRun["status"][] = ["Queued", "Running"];
const RETRYABLE_RUN_STATUSES: PipelineRun["status"][] = ["Failed", "Cancelled"];

// Audit fix archived-11 — color-code known log levels; unknown levels fall
// back to dimmed text so plugin runners can emit whatever vocabulary
// they want without breaking the table render.
const LOG_LEVEL_COLOR: Record<string, string> = {
  info: "blue",
  warn: "yellow",
  error: "red"
};

export default function PipelineRunHistory() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  // Audit fix archived-11 — clicking a step row in the table expands its log
  // panel below. Null = no expansion. Same pattern as the step ID
  // selector for runs above.
  const [selectedStepId, setSelectedStepId] = useState<string | null>(null);

  const pipelineQuery = useQuery({
    queryKey: ["pipeline", id],
    queryFn: ({ signal }) => getPipeline(id!, signal),
    enabled: !!id
  });

  // Audit fix archived-10 — cancel + retry. Cancel flips Queued/Running to
  // Cancelled (backend); the orchestrator's between-node check (in
  // PipelineOrchestrator) bails on the next iteration. Retry enqueues
  // a fresh run with the original graph snapshot so a retry exercises
  // the same DAG even if the saved graph has since changed. Both
  // invalidate the runs query so the table reflects new state and the
  // 2s auto-poll catches the transition.
  const cancelMutation = useMutation({
    mutationFn: (runId: string) => cancelPipelineRun(id!, runId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pipeline-runs", id] });
      queryClient.invalidateQueries({ queryKey: ["pipeline-run-detail", id] });
      notifications.show({ message: "Run cancellation requested.", color: "yellow" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Cancel failed.");
      notifications.show({ message, color: "red" });
    }
  });

  const retryMutation = useMutation({
    mutationFn: (runId: string) => retryPipelineRun(id!, runId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pipeline-runs", id] });
      notifications.show({ message: "Pipeline run re-queued.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Retry failed.");
      notifications.show({ message, color: "red" });
    }
  });

  const runsQuery = useQuery({
    queryKey: ["pipeline-runs", id],
    queryFn: ({ signal }) => listPipelineRuns(id!, signal),
    enabled: !!id,
    refetchInterval: (q) => {
      const rows = q.state.data;
      const stillBusy = rows?.some((r) => r.status === "Queued" || r.status === "Running");
      return stillBusy ? 2000 : false;
    }
  });

  const runDetailQuery = useQuery({
    queryKey: ["pipeline-run-detail", id, selectedRunId],
    queryFn: ({ signal }) => getPipelineRun(id!, selectedRunId!, signal),
    enabled: !!id && !!selectedRunId,
    refetchInterval: (q) => {
      const detail = q.state.data;
      const stillBusy = detail?.run.status === "Queued" || detail?.run.status === "Running";
      return stillBusy ? 2000 : false;
    }
  });

  const runColumns = useMemo<DataTableColumn<PipelineRun>[]>(
    () => [
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => (
          <Badge color={STATUS_COLOR[row.original.status]}>{row.original.status}</Badge>
        )
      },
      {
        id: "queuedAtUtc",
        accessorKey: "queuedAtUtc",
        header: "Queued",
        cell: ({ row }) => new Date(row.original.queuedAtUtc).toLocaleString()
      },
      {
        id: "startedAtUtc",
        accessorKey: "startedAtUtc",
        header: "Started",
        cell: ({ row }) =>
          row.original.startedAtUtc ? new Date(row.original.startedAtUtc).toLocaleString() : <Text c="dimmed">—</Text>
      },
      {
        id: "completedAtUtc",
        accessorKey: "completedAtUtc",
        header: "Completed",
        cell: ({ row }) =>
          row.original.completedAtUtc ? new Date(row.original.completedAtUtc).toLocaleString() : <Text c="dimmed">—</Text>
      },
      {
        id: "triggerKind",
        accessorKey: "triggerKind",
        header: "Trigger",
        cell: ({ row }) => <Badge variant="light">{row.original.triggerKind}</Badge>
      },
      {
        id: "errorMessage",
        accessorKey: "errorMessage",
        header: "Error",
        cell: ({ row }) =>
          row.original.errorMessage ? (
            <Text size="xs" c="red" lineClamp={2}>
              {row.original.errorMessage}
            </Text>
          ) : (
            <Text c="dimmed">—</Text>
          )
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => {
          const run = row.original;
          const isActive = ACTIVE_RUN_STATUSES.includes(run.status);
          const isRetryable = RETRYABLE_RUN_STATUSES.includes(run.status);
          return (
            <Group gap={4} wrap="nowrap" onClick={(e) => e.stopPropagation()}>
              {isActive ? (
                <Tooltip label="Cancel run">
                  <ActionIcon
                    color="yellow"
                    variant="subtle"
                    aria-label={`Cancel run ${run.id}`}
                    loading={cancelMutation.isPending && cancelMutation.variables === run.id}
                    onClick={() => {
                      if (window.confirm("Cancel this run? Any node currently executing will finish first.")) {
                        cancelMutation.mutate(run.id);
                      }
                    }}
                  >
                    <i className="fa fa-stop" />
                  </ActionIcon>
                </Tooltip>
              ) : null}
              {isRetryable ? (
                <Tooltip label="Retry run (re-queue with original graph)">
                  <ActionIcon
                    color="green"
                    variant="subtle"
                    aria-label={`Retry run ${run.id}`}
                    loading={retryMutation.isPending && retryMutation.variables === run.id}
                    onClick={() => retryMutation.mutate(run.id)}
                  >
                    <i className="fa fa-rotate-right" />
                  </ActionIcon>
                </Tooltip>
              ) : null}
            </Group>
          );
        }
      }
    ],
    [cancelMutation, retryMutation]
  );

  const stepColumns = useMemo<DataTableColumn<PipelineRunStep>[]>(
    () => [
      { id: "nodeKey", accessorKey: "nodeKey", header: "Node", cell: ({ row }) => row.original.nodeKey },
      { id: "nodeKind", accessorKey: "nodeKind", header: "Kind", cell: ({ row }) => row.original.nodeKind },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => (
          <Badge color={STATUS_COLOR[row.original.status]}>{row.original.status}</Badge>
        )
      },
      {
        id: "rowCount",
        accessorKey: "rowCount",
        header: "Rows",
        cell: ({ row }) => row.original.rowCount ?? <Text c="dimmed">—</Text>
      },
      {
        id: "errorMessage",
        accessorKey: "errorMessage",
        header: "Error",
        cell: ({ row }) =>
          row.original.errorMessage ? (
            <Text size="xs" c="red" lineClamp={2}>
              {row.original.errorMessage}
            </Text>
          ) : (
            <Text c="dimmed">—</Text>
          )
      },
      {
        id: "logs",
        accessorFn: (row) => row.logs?.length ?? 0,
        header: "Logs",
        enableSorting: false,
        cell: ({ row }) => {
          const count = row.original.logs?.length ?? 0;
          if (count === 0) return <Text c="dimmed">—</Text>;
          const isOpen = selectedStepId === row.original.id;
          return (
            <Badge
              variant={isOpen ? "filled" : "light"}
              color={isOpen ? "blue" : "gray"}
              style={{ cursor: "pointer" }}
            >
              {count}
            </Badge>
          );
        }
      }
    ],
    [selectedStepId]
  );

  const selectedStep = useMemo(() => {
    if (!selectedStepId) return null;
    return runDetailQuery.data?.steps.find((s) => s.id === selectedStepId) ?? null;
  }, [selectedStepId, runDetailQuery.data]);

  if (!id) return null;

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>
          Run history — {pipelineQuery.data?.name ?? "Pipeline"}
        </Title>
        <Group>
          <Button variant="default" onClick={() => navigate(`/pipelines/${id}`)}>
            Open editor
          </Button>
          <Button variant="default" onClick={() => navigate("/pipelines")}>
            Back to list
          </Button>
        </Group>
      </Group>

      {runsQuery.error ? <Alert color="red">Failed to load runs.</Alert> : null}

      <Box>
        <DataTable<PipelineRun>
          mode="client"
          loadAll={async () => runsQuery.data ?? listPipelineRuns(id)}
          queryKey={["pipeline-runs", id]}
          columns={runColumns}
          rowKey={(row) => row.id}
          columnWidths={RUN_COLUMN_WIDTHS}
          emptyMessage="No runs yet."
          loadingMessage="Loading runs…"
          onRowClick={(row) => setSelectedRunId(row.id)}
          // Stable handle for E2E, which previously clicked "the first row in
          // the body" because no semantic one existed (archived-92).
          getRowTestId={(row) => `pipeline-run-row-${row.id}`}
          getRowAriaLabel={(row) => `Run ${row.id}`}
        />
      </Box>

      {selectedRunId ? (
        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Steps</Title>
            {runDetailQuery.data ? (
              <DataTable<PipelineRunStep>
                mode="client"
                loadAll={async () => runDetailQuery.data?.steps ?? []}
                queryKey={["pipeline-run-detail", id, selectedRunId, "steps"]}
                columns={stepColumns}
                rowKey={(row) => row.id}
                columnWidths={STEP_COLUMN_WIDTHS}
                emptyMessage="No steps recorded."
                loadingMessage="Loading step detail…"
                getRowTestId={(row) => `pipeline-run-step-${row.id}`}
                getRowAriaLabel={(row) => `Step ${row.nodeKey}`}
                onRowClick={(row) =>
                  setSelectedStepId((current) => (current === row.id ? null : row.id))
                }
              />
            ) : (
              <Text c="dimmed">Loading step detail…</Text>
            )}

            {selectedStep ? (
              <Paper p="sm" withBorder aria-label="Step logs">
                <Stack gap="xs">
                  <Group justify="space-between">
                    <Title order={5}>
                      Logs — {selectedStep.nodeKey} ({selectedStep.nodeKind})
                    </Title>
                    <Badge variant="light">
                      {selectedStep.logs?.length ?? 0} entries
                    </Badge>
                  </Group>
                  {(selectedStep.logs ?? []).length === 0 ? (
                    <Text size="sm" c="dimmed">
                      No log entries captured for this step.
                    </Text>
                  ) : (
                    <Stack gap={4}>
                      {selectedStep.logs.map((entry, i) => (
                        <StepLogEntry key={i} entry={entry} />
                      ))}
                    </Stack>
                  )}
                </Stack>
              </Paper>
            ) : null}
          </Stack>
        </Paper>
      ) : null}
    </Stack>
  );
}

// Single log row. The timestamp + level badge sit on a line of their
// own so a long message wraps cleanly without pushing the metadata
// out of view. Pre-wrap preserves the orchestrator's stack-trace
// formatting on Failed entries.
function StepLogEntry({ entry }: { entry: PipelineRunStepLog }) {
  const color = LOG_LEVEL_COLOR[entry.level.toLowerCase()] ?? "gray";
  const ts = new Date(entry.timestampUtc).toLocaleTimeString();
  return (
    <Box>
      <Group gap={6} mb={2}>
        <Text size="xs" c="dimmed" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
          {ts}
        </Text>
        <Badge size="xs" color={color} variant="light">
          {entry.level}
        </Badge>
      </Group>
      <Text
        size="xs"
        style={{
          fontFamily: "var(--mantine-font-family-monospace)",
          whiteSpace: "pre-wrap",
          margin: 0
        }}
      >
        {entry.message}
      </Text>
    </Box>
  );
}
