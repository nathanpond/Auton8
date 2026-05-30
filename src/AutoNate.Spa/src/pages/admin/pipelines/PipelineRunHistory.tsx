import { useState } from "react";
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

  if (!id) return null;

  const columns: DataTableColumn<PipelineRun>[] = [
    {
      accessor: "status",
      title: "Status",
      render: (row) => <Badge color={STATUS_COLOR[row.status]}>{row.status}</Badge>
    },
    {
      accessor: "queuedAtUtc",
      title: "Queued",
      render: (row) => new Date(row.queuedAtUtc).toLocaleString()
    },
    {
      accessor: "startedAtUtc",
      title: "Started",
      render: (row) =>
        row.startedAtUtc ? new Date(row.startedAtUtc).toLocaleString() : <Text c="dimmed">—</Text>
    },
    {
      accessor: "completedAtUtc",
      title: "Completed",
      render: (row) =>
        row.completedAtUtc ? new Date(row.completedAtUtc).toLocaleString() : <Text c="dimmed">—</Text>
    },
    {
      accessor: "triggerKind",
      title: "Trigger",
      render: (row) => <Badge variant="light">{row.triggerKind}</Badge>
    },
    {
      accessor: "errorMessage",
      title: "Error",
      render: (row) =>
        row.errorMessage ? (
          <Text size="xs" c="red" lineClamp={2}>
            {row.errorMessage}
          </Text>
        ) : (
          <Text c="dimmed">—</Text>
        )
    }
  ];

  const stepColumns: DataTableColumn<PipelineRunStep>[] = [
    { accessor: "nodeKey", title: "Node" },
    { accessor: "nodeKind", title: "Kind" },
    {
      accessor: "status",
      title: "Status",
      render: (row) => <Badge color={STATUS_COLOR[row.status]}>{row.status}</Badge>
    },
    {
      accessor: "rowCount",
      title: "Rows",
      render: (row) => row.rowCount ?? <Text c="dimmed">—</Text>
    },
    {
      accessor: "errorMessage",
      title: "Error",
      render: (row) =>
        row.errorMessage ? (
          <Text size="xs" c="red" lineClamp={2}>
            {row.errorMessage}
          </Text>
        ) : (
          <Text c="dimmed">—</Text>
        )
    }
  ];

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>
          Run history — {pipelineQuery.data?.name ?? "Pipeline"}
        </Title>
        <Group>
          <Button variant="default" onClick={() => navigate(`/admin/config/pipelines/${id}`)}>
            Open editor
          </Button>
          <Button variant="default" onClick={() => navigate("/admin/config/pipelines")}>
            Back to list
          </Button>
        </Group>
      </Group>

      {runsQuery.error ? (
        <Alert color="red">Failed to load runs.</Alert>
      ) : null}

      <Box>
        <DataTable
          records={runsQuery.data ?? []}
          columns={columns}
          fetching={runsQuery.isLoading}
          idAccessor="id"
          noRecordsText="No runs yet."
          onRowClick={({ record }) => setSelectedRunId(record.id)}
          rowStyle={(row) =>
            row.id === selectedRunId
              ? { background: "var(--mantine-color-blue-light)" }
              : undefined
          }
        />
      </Box>

      {selectedRunId ? (
        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Title order={3}>Steps</Title>
            {runDetailQuery.data ? (
              <DataTable
                records={runDetailQuery.data.steps}
                columns={stepColumns}
                fetching={runDetailQuery.isLoading}
                idAccessor="id"
                noRecordsText="No steps recorded."
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
