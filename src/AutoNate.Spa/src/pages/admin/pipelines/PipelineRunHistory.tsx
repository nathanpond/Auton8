import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Paper,
  Stack,
  Text,
  Title
} from "@mantine/core";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  PipelineRun,
  PipelineRunStep,
  getPipeline,
  getPipelineRun,
  listPipelineRuns
} from "@/api/pipelines";

const STATUS_COLOR: Record<PipelineRun["status"], string> = {
  Queued: "gray",
  Running: "blue",
  Succeeded: "green",
  Failed: "red",
  Cancelled: "yellow"
};

const RUN_COLUMN_WIDTHS = ["140px", "180px", "180px", "180px", "120px", "2fr"];
const STEP_COLUMN_WIDTHS = ["1fr", "140px", "140px", "100px", "2fr"];

export default function PipelineRunHistory() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);

  const pipelineQuery = useQuery({
    queryKey: ["pipeline", id],
    queryFn: ({ signal }) => getPipeline(id!, signal),
    enabled: !!id
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
      }
    ],
    []
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
      }
    ],
    []
  );

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
              />
            ) : (
              <Text c="dimmed">Loading step detail…</Text>
            )}
          </Stack>
        </Paper>
      ) : null}
    </Stack>
  );
}
