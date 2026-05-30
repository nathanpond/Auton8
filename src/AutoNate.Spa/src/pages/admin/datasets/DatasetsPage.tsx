import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Group,
  Modal,
  NativeSelect,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  Dataset,
  DatasetColumn,
  DatasetMode,
  createDataset,
  deleteDataset,
  listDatasets,
  modeLabel,
  refreshDataset
} from "@/api/datasets";

const QUERY_KEY = ["datasets", "list"] as const;

const POSTGRES_TYPES = ["text", "bigint", "double precision", "boolean", "timestamptz"];

export default function DatasetsPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [mode, setMode] = useState<DatasetMode>("Virtual");
  const [sourceKind, setSourceKind] = useState("datastore");
  const [sourceId, setSourceId] = useState("");
  const [sourceTableName, setSourceTableName] = useState("");
  const [refreshCron, setRefreshCron] = useState("*/5 * * * *");
  const [columnsJson, setColumnsJson] = useState(
    JSON.stringify(
      [
        { name: "Id", postgresType: "text" },
        { name: "Name", postgresType: "text" }
      ] satisfies DatasetColumn[],
      null,
      2
    )
  );
  const [submitError, setSubmitError] = useState<string | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: QUERY_KEY,
    queryFn: ({ signal }) => listDatasets(signal)
  });

  const createMutation = useMutation({
    mutationFn: createDataset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setCreateOpen(false);
      resetForm();
      notifications.show({ message: "Dataset created.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteDataset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Dataset deleted.", color: "green" });
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Delete failed.";
      notifications.show({ message, color: "red" });
    }
  });

  const refreshMutation = useMutation({
    mutationFn: refreshDataset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Dataset refreshed.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Refresh failed.");
      notifications.show({ message, color: "red" });
    }
  });

  function resetForm() {
    setName("");
    setDescription("");
    setMode("Virtual");
    setSourceKind("datastore");
    setSourceId("");
    setSourceTableName("");
    setRefreshCron("*/5 * * * *");
    setSubmitError(null);
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    let columns: DatasetColumn[];
    try {
      columns = JSON.parse(columnsJson);
    } catch (err) {
      setSubmitError(
        "Columns JSON is invalid: " + (err instanceof Error ? err.message : String(err))
      );
      return;
    }
    if (!Array.isArray(columns) || columns.length === 0) {
      setSubmitError("Columns must be a non-empty array.");
      return;
    }
    for (const col of columns) {
      if (!col?.name || !POSTGRES_TYPES.includes(col.postgresType)) {
        setSubmitError(
          "Each column must have a name and one of: " + POSTGRES_TYPES.join(", ")
        );
        return;
      }
    }
    createMutation.mutate({
      name: name.trim(),
      description: description.trim() || null,
      mode,
      columns,
      sourceKind,
      sourceId: sourceId.trim(),
      sourceTableName: sourceTableName.trim() || null,
      refreshCron: mode === "Cached" ? refreshCron.trim() || null : null
    });
  }

  const columns: DataTableColumn<Dataset>[] = [
    { accessor: "name", title: "Name" },
    {
      accessor: "mode",
      title: "Mode",
      render: (row) => (
        <Badge color={modeLabel(row.mode) === "Cached" ? "teal" : "gray"}>
          {modeLabel(row.mode)}
        </Badge>
      )
    },
    {
      accessor: "sourceKind",
      title: "Source",
      render: (row) => (
        <Text size="sm">
          <Code>{row.sourceKind}</Code>
          {row.sourceTableName ? ` · ${row.sourceTableName}` : ""}
        </Text>
      )
    },
    {
      accessor: "refreshCron",
      title: "Refresh",
      render: (row) => row.refreshCron ?? <Text c="dimmed">manual / virtual</Text>
    },
    {
      accessor: "lastRefreshedAtUtc",
      title: "Last refresh",
      render: (row) =>
        row.lastRefreshedAtUtc ? new Date(row.lastRefreshedAtUtc).toLocaleString() : <Text c="dimmed">Never</Text>
    },
    {
      accessor: "actions",
      title: "",
      width: 130,
      render: (row) => (
        <Group gap={4} wrap="nowrap">
          {modeLabel(row.mode) === "Cached" ? (
            <Tooltip label="Refresh now">
              <ActionIcon
                variant="subtle"
                aria-label={`Refresh ${row.name}`}
                onClick={() => refreshMutation.mutate(row.id)}
                loading={refreshMutation.isPending && refreshMutation.variables === row.id}
              >
                <i className="fa fa-rotate" />
              </ActionIcon>
            </Tooltip>
          ) : null}
          <Tooltip label="Delete dataset">
            <ActionIcon
              color="red"
              variant="subtle"
              aria-label={`Delete ${row.name}`}
              onClick={() => {
                if (window.confirm(`Delete dataset "${row.name}"?`)) {
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
        <Title order={1}>Datasets</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={() => setCreateOpen(true)}>
          New dataset
        </Button>
      </Group>

      <Text c="dimmed">
        Queryable surfaces backed by a DataStore or DataConnector. Reference them in AQL as{" "}
        <Code>{`FROM Dataset("name") WHERE ...`}</Code>. Virtual datasets execute against the source on
        each query; Cached datasets materialize into <Code>autonate_datastores.cache_&lt;id&gt;</Code>{" "}
        on a refresh cron.
      </Text>

      {error ? (
        <Alert color="red" title="Failed to load datasets">
          {error instanceof Error ? error.message : "Unknown error"}
        </Alert>
      ) : null}

      <Box>
        <DataTable
          records={data ?? []}
          columns={columns}
          fetching={isLoading}
          idAccessor="id"
          noRecordsText="No datasets yet."
        />
      </Box>

      <Modal
        opened={createOpen}
        onClose={() => setCreateOpen(false)}
        title="New dataset"
        centered
        size="lg"
      >
        <form onSubmit={onSubmit}>
          <Stack gap="sm">
            <TextInput
              label="Name"
              description="Used as the AQL handle: FROM Dataset(&quot;name&quot;)."
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
            <NativeSelect
              label="Mode"
              data={[
                { value: "Virtual", label: "Virtual — passthrough on each query" },
                { value: "Cached", label: "Cached — materialized on a refresh cron" }
              ]}
              value={mode}
              onChange={(e) => setMode(e.currentTarget.value as DatasetMode)}
            />
            <NativeSelect
              label="Source kind"
              data={[
                { value: "datastore", label: "DataStore (SQL table or File metadata)" },
                { value: "dataconnector", label: "DataConnector (Cached only)" }
              ]}
              value={sourceKind}
              onChange={(e) => setSourceKind(e.currentTarget.value)}
            />
            <TextInput
              label="Source ID"
              description="The DataStore or DataConnector UUID this dataset draws from."
              required
              value={sourceId}
              onChange={(e) => setSourceId(e.currentTarget.value)}
            />
            <TextInput
              label="Source table"
              description="Required for SQL DataStore sources. Leave blank for File datastore or connector sources."
              value={sourceTableName}
              onChange={(e) => setSourceTableName(e.currentTarget.value)}
            />
            {mode === "Cached" ? (
              <TextInput
                label="Refresh cron"
                description="5-field cron. v1 recognizes the */N minutes form (e.g. '*/5 * * * *')."
                value={refreshCron}
                onChange={(e) => setRefreshCron(e.currentTarget.value)}
              />
            ) : null}
            <Textarea
              label="Column schema (JSON)"
              description={`Allowed postgresType values: ${POSTGRES_TYPES.join(", ")}.`}
              autosize
              minRows={6}
              value={columnsJson}
              onChange={(e) => setColumnsJson(e.currentTarget.value)}
              styles={{
                input: { fontFamily: "var(--mantine-font-family-monospace)", fontSize: 13 }
              }}
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
