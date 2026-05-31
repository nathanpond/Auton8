import { FormEvent, useEffect, useMemo, useState } from "react";
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
  ConnectorTestResult,
  DataConnector,
  createDataConnector,
  deleteDataConnector,
  listDataConnectorKinds,
  listDataConnectors,
  testDataConnector
} from "@/api/dataconnectors";

const QUERY_KEY = ["dataconnectors", "list"] as const;
const KINDS_KEY = ["dataconnectors", "kinds"] as const;
const COLUMN_WIDTHS = ["1fr", "120px", "2fr", "200px", "130px"];

export default function DataConnectorsPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState("rest");
  const [configJson, setConfigJson] = useState('{"url": "", "authMode": "none"}');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ id: string; result: ConnectorTestResult } | null>(null);

  const { data: kinds } = useQuery({
    queryKey: KINDS_KEY,
    queryFn: ({ signal }) => listDataConnectorKinds(signal)
  });

  useEffect(() => {
    if (kind === "rest") setConfigJson('{"url": "", "authMode": "none"}');
    else if (kind === "smb") setConfigJson('{"share": "", "path": "/", "username": "", "password": ""}');
    else setConfigJson("{}");
  }, [kind]);

  const createMutation = useMutation({
    mutationFn: createDataConnector,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setCreateOpen(false);
      setName("");
      setDescription("");
      setSubmitError(null);
      notifications.show({ message: "Data connector created.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteDataConnector,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Data connector deleted.", color: "green" });
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Delete failed.";
      notifications.show({ message, color: "red" });
    }
  });

  const testMutation = useMutation({
    mutationFn: testDataConnector,
    onSuccess: (result, id) => setTestResult({ id, result })
  });

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    try {
      JSON.parse(configJson);
    } catch (err) {
      setSubmitError(
        "Config JSON is not valid: " + (err instanceof Error ? err.message : String(err))
      );
      return;
    }
    createMutation.mutate({
      name: name.trim(),
      description: description.trim() || null,
      kind,
      configJson
    });
  }

  const columns = useMemo<DataTableColumn<DataConnector>[]>(
    () => [
      { id: "name", accessorKey: "name", header: "Name", cell: ({ row }) => row.original.name },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Kind",
        cell: ({ row }) => <Badge variant="light">{row.original.kind}</Badge>
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) => row.original.description ?? <Text c="dimmed">—</Text>
      },
      {
        id: "lastFetchedAtUtc",
        accessorKey: "lastFetchedAtUtc",
        header: "Last fetched",
        cell: ({ row }) =>
          row.original.lastFetchedAtUtc
            ? new Date(row.original.lastFetchedAtUtc).toLocaleString()
            : <Text c="dimmed">Never</Text>
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Group gap={4} wrap="nowrap">
            <Tooltip label="Test connection">
              <ActionIcon
                variant="subtle"
                aria-label={`Test ${row.original.name}`}
                onClick={() => testMutation.mutate(row.original.id)}
                loading={testMutation.isPending && testMutation.variables === row.original.id}
              >
                <i className="fa fa-plug" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete connector">
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={`Delete ${row.original.name}`}
                onClick={() => {
                  if (window.confirm(`Delete data connector "${row.original.name}"?`)) {
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
    [deleteMutation, testMutation]
  );

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>Data Connectors</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={() => setCreateOpen(true)}>
          New connector
        </Button>
      </Group>

      <Text c="dimmed">
        Outbound integrations that pull data into AutoNate from external systems on a schedule.
        Built-in kinds: <Code>rest</Code> (REST API with bearer / basic / api-key auth) and{" "}
        <Code>smb</Code> (Samba network share — wire integration ships separately).
      </Text>

      {testResult ? (
        <Alert
          color={testResult.result.success ? "green" : "red"}
          title={testResult.result.success ? "Test succeeded" : "Test failed"}
          withCloseButton
          onClose={() => setTestResult(null)}
        >
          {testResult.result.message}
        </Alert>
      ) : null}

      <Box>
        <DataTable<DataConnector>
          mode="client"
          loadAll={() => listDataConnectors()}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(row) => row.id}
          columnWidths={COLUMN_WIDTHS}
          emptyMessage="No data connectors yet."
          loadingMessage="Loading data connectors…"
        />
      </Box>

      <Modal
        opened={createOpen}
        onClose={() => setCreateOpen(false)}
        title="New data connector"
        centered
        size="lg"
      >
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
            <NativeSelect
              label="Kind"
              data={(kinds ?? ["rest", "smb"]).map((k) => ({ value: k, label: k }))}
              value={kind}
              onChange={(e) => setKind(e.currentTarget.value)}
            />
            <Textarea
              label="Config JSON"
              description="Kind-specific configuration. REST: { url, authMode, token?, username?, password?, apiKeyHeader?, apiKey?, rowsPath? }"
              autosize
              minRows={5}
              value={configJson}
              onChange={(e) => setConfigJson(e.currentTarget.value)}
              styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)", fontSize: 13 } }}
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
