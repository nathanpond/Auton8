import { toast } from "@/components/notifications/toast";
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
  Loader,
  Modal,
  NativeSelect,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  ConnectorTestResult,
  DataConnector,
  DataConnectorPreviewResult,
  createDataConnector,
  deleteDataConnector,
  listDataConnectorKinds,
  listDataConnectors,
  previewDataConnector,
  testDataConnector,
  updateDataConnector
} from "@/api/dataconnectors";

const QUERY_KEY = ["dataconnectors", "list"] as const;
const KINDS_KEY = ["dataconnectors", "kinds"] as const;
const COLUMN_WIDTHS = ["1fr", "120px", "2fr", "200px", "130px"];

export default function DataConnectorsPage() {
  const queryClient = useQueryClient();
  const [modalOpen, setModalOpen] = useState(false);
  // editingId === null = create mode; otherwise we're editing that row.
  // Same modal renders both flows (matches CodeTransformersPage shape) so
  // the configJson textarea + kind dropdown logic stays in one place.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState("rest");
  const [configJson, setConfigJson] = useState('{"url": "", "authMode": "none"}');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ id: string; result: ConnectorTestResult } | null>(null);

  // Preview modal — separate dialog so it can render the row table without
  // fighting the create/edit modal for screen space.
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewConnector, setPreviewConnector] = useState<DataConnector | null>(null);
  const [previewResult, setPreviewResult] = useState<DataConnectorPreviewResult | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const { data: kinds } = useQuery({
    queryKey: KINDS_KEY,
    queryFn: ({ signal }) => listDataConnectorKinds(signal)
  });

  useEffect(() => {
    // The kind-driven config skeleton only applies in create mode; an
    // existing row's configJson stays untouched so we don't clobber the
    // user's saved credentials the moment the edit modal opens.
    if (editingId !== null) return;
    if (kind === "rest") setConfigJson('{"url": "", "authMode": "none"}');
    else if (kind === "smb") setConfigJson('{"share": "", "path": "/", "username": "", "password": ""}');
    else setConfigJson("{}");
  }, [kind, editingId]);

  function resetForm() {
    setEditingId(null);
    setName("");
    setDescription("");
    setKind("rest");
    setConfigJson('{"url": "", "authMode": "none"}');
    setSubmitError(null);
  }

  function openCreate() {
    resetForm();
    setModalOpen(true);
  }

  function openEdit(row: DataConnector) {
    setEditingId(row.id);
    setName(row.name);
    setDescription(row.description ?? "");
    setKind(row.kind);
    setConfigJson(row.configJson);
    setSubmitError(null);
    setModalOpen(true);
  }

  const createMutation = useMutation({
    mutationFn: createDataConnector,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setModalOpen(false);
      resetForm();
      toast.success("Data connector created.");
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const editMutation = useMutation({
    mutationFn: (vars: { id: string }) =>
      updateDataConnector(vars.id, {
        name: name.trim(),
        description: description.trim() || null,
        configJson
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setModalOpen(false);
      resetForm();
      toast.success("Data connector updated.");
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Update failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteDataConnector,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      toast.success("Data connector deleted.");
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Delete failed.";
      toast.error(message);
    }
  });

  const testMutation = useMutation({
    mutationFn: testDataConnector,
    onSuccess: (result, id) => setTestResult({ id, result })
  });

  const previewMutation = useMutation({
    mutationFn: (id: string) => previewDataConnector(id, 5),
    onSuccess: (result) => {
      setPreviewResult(result);
      setPreviewError(null);
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Preview failed.");
      setPreviewError(message);
      setPreviewResult(null);
    }
  });

  function openPreview(row: DataConnector) {
    setPreviewConnector(row);
    setPreviewResult(null);
    setPreviewError(null);
    setPreviewOpen(true);
    previewMutation.mutate(row.id);
  }

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
    if (editingId) {
      editMutation.mutate({ id: editingId });
    } else {
      createMutation.mutate({
        name: name.trim(),
        description: description.trim() || null,
        kind,
        configJson
      });
    }
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
            <Tooltip label="Preview data (first 5 rows)">
              <ActionIcon
                variant="subtle"
                aria-label={`Preview ${row.original.name}`}
                onClick={() => openPreview(row.original)}
              >
                <i className="fa fa-eye" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Edit connector">
              <ActionIcon
                variant="subtle"
                aria-label={`Edit ${row.original.name}`}
                onClick={() => openEdit(row.original)}
              >
                <i className="fa fa-pen-to-square" />
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
    // Stable references stand in for openEdit/openPreview because those
    // functions only close over state setters (stable across renders).
    [deleteMutation, testMutation]
  );

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>Data Connectors</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={openCreate}>
          New connector
        </Button>
      </Group>

      <Text c="dimmed">
        Outbound integrations that pull data into Auton8 from external systems on a schedule.
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
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingId ? "Edit data connector" : "New data connector"}
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
              // Kind is fixed once the row exists — the runtime handler
              // is locked to that string and changing it would orphan any
              // refresh state already collected.
              disabled={editingId !== null}
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
              <Button variant="default" onClick={() => setModalOpen(false)}>
                Cancel
              </Button>
              <Button
                type="submit"
                loading={createMutation.isPending || editMutation.isPending}
              >
                {editingId ? "Save" : "Create"}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      <Modal
        opened={previewOpen}
        onClose={() => setPreviewOpen(false)}
        title={
          previewConnector ? `Preview — ${previewConnector.name}` : "Preview data"
        }
        size="xl"
        centered
      >
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Fetches the first 5 rows from <Code>{previewConnector?.kind}</Code> connector without
            updating its <Code>lastFetchedAtUtc</Code> / cursor — safe to invoke repeatedly while
            iterating on config.
          </Text>
          {previewMutation.isPending ? (
            <Group justify="center" py="lg">
              <Loader size="sm" />
              <Text size="sm">Calling connector…</Text>
            </Group>
          ) : null}
          {previewError ? (
            <Alert color="red" title="Preview failed">
              {previewError}
            </Alert>
          ) : null}
          {previewResult && !previewResult.success ? (
            <Alert color="red" title="Connector returned an error">
              <Code block>{previewResult.errorMessage ?? "(no message)"}</Code>
            </Alert>
          ) : null}
          {previewResult && previewResult.success ? (
            previewResult.rows.length === 0 ? (
              <Alert color="yellow">
                Connector returned 0 rows. Check the config — most often a wrong URL / path / auth
                or a <Code>rowsPath</Code> mismatch for REST connectors.
              </Alert>
            ) : (
              <Box style={{ maxHeight: 360, overflow: "auto" }}>
                <Table striped withColumnBorders>
                  <Table.Thead>
                    <Table.Tr>
                      {previewResult.columns.map((c) => (
                        <Table.Th key={c}>{c}</Table.Th>
                      ))}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {previewResult.rows.map((row, i) => (
                      <Table.Tr key={i}>
                        {previewResult.columns.map((c) => (
                          <Table.Td key={c}>
                            {formatPreviewCell(row[c])}
                          </Table.Td>
                        ))}
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Box>
            )
          ) : null}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setPreviewOpen(false)}>
              Close
            </Button>
            {previewConnector ? (
              <Button
                leftSection={<i className="fa fa-rotate" />}
                loading={previewMutation.isPending}
                onClick={() => previewMutation.mutate(previewConnector.id)}
              >
                Re-run preview
              </Button>
            ) : null}
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}

// Render unknown JSON cell values readably. Strings/numbers/booleans pass
// through; objects and arrays are stringified so the row stays one line.
function formatPreviewCell(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}
