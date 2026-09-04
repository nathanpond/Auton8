import { toast } from "@/components/notifications/toast";
import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import { Link } from "react-router-dom";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  Pipeline,
  createPipeline,
  deletePipeline,
  listPipelines,
  runPipeline
} from "@/api/pipelines";

import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import CronExpressionBuilder from "@/components/CronExpressionBuilder";

const QUERY_KEY = ["pipelines", "list"] as const;
const COLUMN_WIDTHS = ["1fr", "2fr", "140px", "180px", "130px"];

export default function PipelinesPage() {
  useDocumentTitle("Pipelines");
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [scheduleCron, setScheduleCron] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);

  const createMutation = useMutation({
    mutationFn: createPipeline,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setCreateOpen(false);
      setName("");
      setDescription("");
      setScheduleCron("");
      setSubmitError(null);
      toast.success("Pipeline created.");
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deletePipeline,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      toast.success("Pipeline deleted.");
    }
  });

  const runMutation = useMutation({
    mutationFn: runPipeline,
    onSuccess: () => {
      toast.success("Pipeline run queued. Check the run history.");
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Run failed.");
      toast.error(message);
    }
  });

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    createMutation.mutate({
      name: name.trim(),
      description: description.trim() || null,
      // Empty string is the backend's "clear" signal; on create that's the
      // same as null. Trim so a user typing "   " doesn't get a stored cron
      // string of whitespace.
      scheduleCron: scheduleCron.trim() || null,
      graph: { nodes: [], edges: [] }
    });
  }

  const columns = useMemo<DataTableColumn<Pipeline>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Link to={`/pipelines/${row.original.id}`}>
            <Text fw={500}>{row.original.name}</Text>
          </Link>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) => row.original.description ?? <Text c="dimmed">—</Text>
      },
      {
        id: "scheduleCron",
        accessorKey: "scheduleCron",
        header: "Schedule",
        cell: ({ row }) =>
          row.original.scheduleCron ? <Badge variant="light">{row.original.scheduleCron}</Badge> : <Text c="dimmed">manual</Text>
      },
      {
        id: "lastRunAtUtc",
        accessorKey: "lastRunAtUtc",
        header: "Last run",
        cell: ({ row }) =>
          row.original.lastRunAtUtc ? new Date(row.original.lastRunAtUtc).toLocaleString() : <Text c="dimmed">Never</Text>
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Group gap={4} wrap="nowrap">
            <Tooltip label="Run now">
              <ActionIcon
                variant="subtle"
                aria-label={`Run ${row.original.name}`}
                onClick={() => runMutation.mutate(row.original.id)}
                loading={runMutation.isPending && runMutation.variables === row.original.id}
              >
                <i className="fa fa-play" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete pipeline">
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={`Delete ${row.original.name}`}
                onClick={() => {
                  if (window.confirm(`Delete pipeline "${row.original.name}"?`)) {
                    deleteMutation.mutate(row.original.id);
                  }
                }}
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </Tooltip>
          </Group>
        )
      }
    ],
    [deleteMutation, runMutation]
  );

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>Analytics Pipelines</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={() => setCreateOpen(true)}>
          New pipeline
        </Button>
      </Group>

      <Text c="dimmed">
        Compose dataset sources, transformers, analyzers, and dataset sinks into a DAG. Manual runs
        enqueue immediately; scheduled runs follow the configured cron. Click a name to open the
        React Flow editor.
      </Text>

      <Box>
        <DataTable<Pipeline>
          mode="client"
          loadAll={() => listPipelines()}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(row) => row.id}
          columnWidths={COLUMN_WIDTHS}
          emptyMessage="No pipelines yet."
          loadingMessage="Loading pipelines…"
        />
      </Box>

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title="New pipeline" centered>
        <form onSubmit={onSubmit}>
          <Stack gap="sm">
            <TextInput
              label="Name"
              required
              value={name}
              onChange={(e) => setName(e.currentTarget.value)}
              data-autofocus
            />
            <TextInput
              label="Description"
              value={description}
              onChange={(e) => setDescription(e.currentTarget.value)}
            />
            <CronExpressionBuilder
              label="Schedule"
              description="Optional. Pick a preset or choose Custom to type a cron. v1 only triggers schedules of the form `*/N * * * *`."
              value={scheduleCron}
              onChange={setScheduleCron}
            />
            {/* In-page, not a toast (#91): this sits inside an open form and the
            user has to fix the input it describes. A toast would vanish
            mid-correction — it is a validation summary by another name. */}
            {submitError ? <Alert color="red">{submitError}</Alert> : null}
            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={() => setCreateOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" loading={createMutation.isPending}>
                Create
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}
