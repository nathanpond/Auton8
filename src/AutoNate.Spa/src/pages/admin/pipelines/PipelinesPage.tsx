import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
import { notifications } from "@mantine/notifications";
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

const QUERY_KEY = ["pipelines", "list"] as const;

export default function PipelinesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: QUERY_KEY,
    queryFn: ({ signal }) => listPipelines(signal)
  });

  const createMutation = useMutation({
    mutationFn: createPipeline,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setCreateOpen(false);
      setName("");
      setDescription("");
      setSubmitError(null);
      notifications.show({ message: "Pipeline created.", color: "green" });
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
      notifications.show({ message: "Pipeline deleted.", color: "green" });
    }
  });

  const runMutation = useMutation({
    mutationFn: runPipeline,
    onSuccess: () => {
      notifications.show({
        message: "Pipeline run queued. Check the run history.",
        color: "green"
      });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Run failed.");
      notifications.show({ message, color: "red" });
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
      graph: { nodes: [], edges: [] }
    });
  }

  const columns: DataTableColumn<Pipeline>[] = [
    {
      accessor: "name",
      title: "Name",
      render: (row) => (
        <Link to={`/admin/config/pipelines/${row.id}`}>
          <Text fw={500}>{row.name}</Text>
        </Link>
      )
    },
    {
      accessor: "description",
      title: "Description",
      render: (row) => row.description ?? <Text c="dimmed">—</Text>
    },
    {
      accessor: "scheduleCron",
      title: "Schedule",
      render: (row) =>
        row.scheduleCron ? <Badge variant="light">{row.scheduleCron}</Badge> : <Text c="dimmed">manual</Text>
    },
    {
      accessor: "lastRunAtUtc",
      title: "Last run",
      render: (row) =>
        row.lastRunAtUtc ? new Date(row.lastRunAtUtc).toLocaleString() : <Text c="dimmed">Never</Text>
    },
    {
      accessor: "actions",
      title: "",
      width: 130,
      render: (row) => (
        <Group gap={4} wrap="nowrap">
          <Tooltip label="Run now">
            <ActionIcon
              variant="subtle"
              aria-label={`Run ${row.name}`}
              onClick={() => runMutation.mutate(row.id)}
              loading={runMutation.isPending && runMutation.variables === row.id}
            >
              <i className="fa fa-play" />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Delete pipeline">
            <ActionIcon
              color="red"
              variant="subtle"
              aria-label={`Delete ${row.name}`}
              onClick={() => {
                if (window.confirm(`Delete pipeline "${row.name}"?`)) {
                  deleteMutation.mutate(row.id);
                }
              }}
            >
              <i className="fa fa-trash" />
            </ActionIcon>
          </Tooltip>
        </Group>
      )
    }
  ];

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

      {error ? (
        <Alert color="red" title="Failed to load pipelines">
          {error instanceof Error ? error.message : "Unknown error"}
        </Alert>
      ) : null}

      <Box>
        <DataTable
          records={data ?? []}
          columns={columns}
          fetching={isLoading}
          idAccessor="id"
          noRecordsText="No pipelines yet."
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
