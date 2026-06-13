import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
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
  refreshDataset,
  updateDataset
} from "@/api/datasets";
import {
  kindLabel as dataStoreKindLabel,
  listDataStoreTables,
  listDataStores
} from "@/api/datastores";
import { listDataConnectors } from "@/api/dataconnectors";
import CronExpressionBuilder from "@/components/CronExpressionBuilder";
import { useDatasetsPagePageContext } from "./useDatasetsPagePageContext";

const QUERY_KEY = ["datasets", "list"] as const;
const COLUMN_WIDTHS = ["1fr", "100px", "1fr", "1fr", "180px", "130px"];

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
  const DEFAULT_COLUMNS_JSON = useMemo(
    () =>
      JSON.stringify(
        [
          { name: "Id", postgresType: "text" },
          { name: "Name", postgresType: "text" }
        ] satisfies DatasetColumn[],
        null,
        2
      ),
    []
  );
  const [columnsJson, setColumnsJson] = useState(DEFAULT_COLUMNS_JSON);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // Source picker queries. The lists are tiny so we always pull both up
  // front (and cache them via React Query) — the toggle between
  // `datastore` / `dataconnector` flips which Select renders. Tables are
  // only fetched when a SQL DataStore is selected.
  const dataStoresQuery = useQuery({
    queryKey: ["datastores", "list"],
    queryFn: ({ signal }) => listDataStores(signal),
    enabled: createOpen
  });
  const dataConnectorsQuery = useQuery({
    queryKey: ["dataconnectors", "list"],
    queryFn: ({ signal }) => listDataConnectors(signal),
    enabled: createOpen
  });

  const selectedDataStore = useMemo(
    () =>
      sourceKind === "datastore"
        ? dataStoresQuery.data?.find((d) => d.id === sourceId) ?? null
        : null,
    [sourceKind, sourceId, dataStoresQuery.data]
  );
  const isSqlDataStore =
    selectedDataStore !== null && dataStoreKindLabel(selectedDataStore.kind) === "SqlType";

  // Only hit /tables when the user has actually picked a SQL DataStore.
  // FileType stores and DataConnectors don't have tables to enumerate.
  const tablesQuery = useQuery({
    queryKey: ["datastores", "tables", sourceId],
    queryFn: ({ signal }) => listDataStoreTables(sourceId, signal),
    enabled: createOpen && sourceKind === "datastore" && !!sourceId && isSqlDataStore
  });

  // Switching sourceKind invalidates the previously picked sourceId/table:
  // a DataStore UUID isn't a DataConnector UUID (and vice versa). Clearing
  // here prevents the form posting a `datastore` kind with a connector id.
  useEffect(() => {
    setSourceId("");
    setSourceTableName("");
  }, [sourceKind]);

  // Switching source store within `datastore` kind: clear the table pick,
  // since the previous store's table names are meaningless for the new one.
  useEffect(() => {
    setSourceTableName("");
  }, [sourceId]);

  // "Import columns from selected table" replaces the textarea contents
  // with the table's stored schema. The user can still edit before saving.
  function importColumnsFromSelectedTable() {
    if (!tablesQuery.data || !sourceTableName) return;
    const table = tablesQuery.data.find((t) => t.tableName === sourceTableName);
    if (!table) {
      setSubmitError(`Table "${sourceTableName}" not found in selected DataStore.`);
      return;
    }
    setColumnsJson(JSON.stringify(table.columns, null, 2));
    setSubmitError(null);
  }

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
    }
  });

  const refreshMutation = useMutation({
    mutationFn: refreshDataset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Dataset refreshed.", color: "green" });
    }
  });

  // Audit fix #13 — Edit modal. Separate from the create modal because
  // the backend's UpdateDatasetRequest accepts only Name / Description /
  // RefreshCron (mode / source / columns are locked once the cache
  // table / virtual view exists). Reusing the create modal would mean
  // hiding five fields in edit mode; a small dedicated modal is
  // clearer.
  const [editOpen, setEditOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editRefreshCron, setEditRefreshCron] = useState("");
  const [editIsCached, setEditIsCached] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  function openEdit(row: Dataset) {
    setEditingId(row.id);
    setEditName(row.name);
    setEditDescription(row.description ?? "");
    setEditRefreshCron(row.refreshCron ?? "");
    setEditIsCached(modeLabel(row.mode) === "Cached");
    setEditError(null);
    setEditOpen(true);
  }

  const editMutation = useMutation({
    mutationFn: (vars: { id: string }) =>
      updateDataset(vars.id, {
        name: editName.trim(),
        description: editDescription.trim() || null,
        // Backend semantics: null = leave unchanged, empty string =
        // clear (sets RefreshCron to null). Send the trimmed value
        // verbatim so the user can clear an existing cron by emptying
        // the field — important because Virtual datasets shouldn't
        // carry a cron at all.
        refreshCron: editIsCached ? editRefreshCron.trim() : ""
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setEditOpen(false);
      notifications.show({ message: "Dataset updated.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Update failed.");
      setEditError(message);
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
    setColumnsJson(DEFAULT_COLUMNS_JSON);
    setSubmitError(null);
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    submitCreate();
  }

  const submitCreate = useCallback(() => {
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    let parsedColumns: DatasetColumn[];
    try {
      parsedColumns = JSON.parse(columnsJson);
    } catch (err) {
      setSubmitError(
        "Columns JSON is invalid: " + (err instanceof Error ? err.message : String(err))
      );
      return;
    }
    if (!Array.isArray(parsedColumns) || parsedColumns.length === 0) {
      setSubmitError("Columns must be a non-empty array.");
      return;
    }
    for (const col of parsedColumns) {
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
      columns: parsedColumns,
      sourceKind,
      sourceId: sourceId.trim(),
      sourceTableName: sourceTableName.trim() || null,
      refreshCron: mode === "Cached" ? refreshCron.trim() || null : null
    });
  }, [name, description, mode, sourceKind, sourceId, sourceTableName, refreshCron, columnsJson, createMutation]);

  // Parallel useQuery so the page-context provider has a live datasets list
  // to expose to the chatbot. react-query dedupes the request with the
  // DataTable's internal query so this is free at runtime.
  const datasetsQuery = useQuery({ queryKey: QUERY_KEY, queryFn: () => listDatasets() });
  const datasetsList = datasetsQuery.data ?? [];

  const setCreateField = useCallback(
    (field: "name" | "description" | "mode" | "sourceKind" | "sourceId" | "sourceTableName" | "refreshCron" | "columnsJson", value: string) => {
      if (field === "name") setName(value);
      else if (field === "description") setDescription(value);
      else if (field === "mode") setMode(value as DatasetMode);
      else if (field === "sourceKind") setSourceKind(value);
      else if (field === "sourceId") setSourceId(value);
      else if (field === "sourceTableName") setSourceTableName(value);
      else if (field === "refreshCron") setRefreshCron(value);
      else if (field === "columnsJson") setColumnsJson(value);
    },
    []
  );

  const setEditField = useCallback(
    (field: "name" | "description" | "refreshCron", value: string) => {
      if (field === "name") setEditName(value);
      else if (field === "description") setEditDescription(value);
      else if (field === "refreshCron") setEditRefreshCron(value);
    },
    []
  );

  const submitEdit = useCallback(() => {
    if (!editName.trim()) {
      setEditError("Name is required.");
      return;
    }
    if (!editingId) return;
    editMutation.mutate({ id: editingId });
  }, [editName, editingId, editMutation]);

  const closeModals = useCallback(() => {
    setCreateOpen(false);
    setEditOpen(false);
  }, []);

  const openCreateModal = useCallback(() => {
    resetForm();
    setCreateOpen(true);
  }, []);

  const deleteMutationRef = useRef(deleteMutation);
  deleteMutationRef.current = deleteMutation;
  const refreshMutationRef = useRef(refreshMutation);
  refreshMutationRef.current = refreshMutation;
  const deleteDatasetCb = useCallback(
    (id: string) => deleteMutationRef.current.mutateAsync(id),
    []
  );
  const refreshDatasetCb = useCallback(
    (id: string) => refreshMutationRef.current.mutateAsync(id),
    []
  );

  useDatasetsPagePageContext({
    datasets: datasetsList,
    loading: datasetsQuery.isLoading,
    createModal: {
      open: createOpen,
      name,
      description,
      mode,
      sourceKind,
      sourceId,
      sourceTableName,
      refreshCron,
      columnsJson,
      submitError
    },
    editModal: {
      open: editOpen,
      editingId,
      editName,
      editDescription,
      editRefreshCron,
      editIsCached,
      editError
    },
    sources: {
      datastores: (dataStoresQuery.data ?? []).map((s) => ({ id: s.id, name: s.name, kind: s.kind })),
      tables: (tablesQuery.data ?? []).map((t) => ({ tableName: t.tableName, rowCount: t.rowCount })),
      connectors: (dataConnectorsQuery.data ?? []).map((c) => ({ id: c.id, name: c.name, kind: c.kind }))
    },
    openCreateModal,
    openEditModal: (id) => {
      const row = datasetsList.find((d) => d.id === id);
      if (row) openEdit(row);
    },
    closeModals,
    setCreateField,
    setEditField,
    submitCreate,
    submitEdit,
    deleteDataset: deleteDatasetCb,
    refreshDataset: refreshDatasetCb
  });

  const columns = useMemo<DataTableColumn<Dataset>[]>(
    () => [
      { id: "name", accessorKey: "name", header: "Name", cell: ({ row }) => row.original.name },
      {
        id: "mode",
        accessorFn: (row) => modeLabel(row.mode),
        header: "Mode",
        cell: ({ row }) => (
          <Badge color={modeLabel(row.original.mode) === "Cached" ? "teal" : "gray"}>
            {modeLabel(row.original.mode)}
          </Badge>
        )
      },
      {
        id: "sourceKind",
        accessorKey: "sourceKind",
        header: "Source",
        cell: ({ row }) => (
          <Text size="sm">
            <Code>{row.original.sourceKind}</Code>
            {row.original.sourceTableName ? ` · ${row.original.sourceTableName}` : ""}
          </Text>
        )
      },
      {
        id: "refreshCron",
        accessorKey: "refreshCron",
        header: "Refresh",
        cell: ({ row }) => row.original.refreshCron ?? <Text c="dimmed">manual / virtual</Text>
      },
      {
        id: "lastRefreshedAtUtc",
        accessorKey: "lastRefreshedAtUtc",
        header: "Last refresh",
        cell: ({ row }) =>
          row.original.lastRefreshedAtUtc
            ? new Date(row.original.lastRefreshedAtUtc).toLocaleString()
            : <Text c="dimmed">Never</Text>
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Group gap={4} wrap="nowrap">
            {modeLabel(row.original.mode) === "Cached" ? (
              <Tooltip label="Refresh now">
                <ActionIcon
                  variant="subtle"
                  aria-label={`Refresh ${row.original.name}`}
                  onClick={() => refreshMutation.mutate(row.original.id)}
                  loading={refreshMutation.isPending && refreshMutation.variables === row.original.id}
                >
                  <i className="fa fa-rotate" />
                </ActionIcon>
              </Tooltip>
            ) : null}
            <Tooltip label="Edit dataset">
              <ActionIcon
                variant="subtle"
                aria-label={`Edit ${row.original.name}`}
                onClick={() => openEdit(row.original)}
              >
                <i className="fa fa-pen-to-square" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete dataset">
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={`Delete ${row.original.name}`}
                onClick={() => {
                  if (window.confirm(`Delete dataset "${row.original.name}"?`)) {
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
    [deleteMutation, refreshMutation]
  );

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

      <Box>
        <DataTable<Dataset>
          mode="client"
          loadAll={() => listDatasets()}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(row) => row.id}
          columnWidths={COLUMN_WIDTHS}
          emptyMessage="No datasets yet."
          loadingMessage="Loading datasets…"
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
              description='Used as the AQL handle: FROM Dataset("name").'
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
            {sourceKind === "datastore" ? (
              <NativeSelect
                label="Source DataStore"
                description="Lists the DataStores you can read. Pick a SQL store to enable the table picker and 'Import columns' below."
                required
                data={[
                  { value: "", label: "Select a DataStore…" },
                  ...(dataStoresQuery.data ?? []).map((s) => ({
                    value: s.id,
                    label: `${s.name} (${dataStoreKindLabel(s.kind)})`
                  }))
                ]}
                value={sourceId}
                onChange={(e) => setSourceId(e.currentTarget.value)}
              />
            ) : (
              <NativeSelect
                label="Source DataConnector"
                description="Datasets over connectors must be Cached mode (the connector is polled on a refresh cron, never live)."
                required
                data={[
                  { value: "", label: "Select a DataConnector…" },
                  ...(dataConnectorsQuery.data ?? []).map((c) => ({
                    value: c.id,
                    label: `${c.name} (${c.kind})`
                  }))
                ]}
                value={sourceId}
                onChange={(e) => setSourceId(e.currentTarget.value)}
              />
            )}
            {sourceKind === "datastore" && isSqlDataStore ? (
              <NativeSelect
                label="Source table"
                description={
                  tablesQuery.isLoading
                    ? "Loading tables…"
                    : (tablesQuery.data?.length ?? 0) === 0
                    ? "This SQL DataStore has no ingested tables yet. Upload a CSV first."
                    : "Required for SQL DataStore sources. Used as the FROM target in generated queries."
                }
                data={[
                  { value: "", label: "Select a table…" },
                  ...(tablesQuery.data ?? []).map((t) => ({
                    value: t.tableName,
                    label: `${t.tableName} (${t.rowCount.toLocaleString()} row${t.rowCount === 1 ? "" : "s"})`
                  }))
                ]}
                value={sourceTableName}
                onChange={(e) => setSourceTableName(e.currentTarget.value)}
              />
            ) : null}
            {mode === "Cached" ? (
              <CronExpressionBuilder
                label="Refresh cron"
                description="How often Cached datasets re-materialize. Pick a preset or choose Custom."
                value={refreshCron}
                onChange={setRefreshCron}
              />
            ) : null}
            <Group justify="space-between" align="flex-end" gap="sm">
              <Text size="sm" fw={500}>
                Column schema (JSON)
              </Text>
              <Button
                variant="default"
                size="compact-sm"
                leftSection={<i className="fa fa-download" />}
                disabled={!sourceTableName || !tablesQuery.data?.length}
                onClick={importColumnsFromSelectedTable}
              >
                Import columns from selected table
              </Button>
            </Group>
            <Textarea
              aria-label="Column schema (JSON)"
              description={`Allowed postgresType values: ${POSTGRES_TYPES.join(", ")}. Edit by hand, or click "Import columns" to pull the schema from the picked table above.`}
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

      <Modal
        opened={editOpen}
        onClose={() => setEditOpen(false)}
        title="Edit dataset"
        centered
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!editName.trim()) {
              setEditError("Name is required.");
              return;
            }
            if (!editingId) return;
            editMutation.mutate({ id: editingId });
          }}
        >
          <Stack gap="sm">
            <TextInput
              label="Name"
              required
              value={editName}
              onChange={(e) => setEditName(e.currentTarget.value)}
              data-autofocus
            />
            <TextInput
              label="Description"
              value={editDescription}
              onChange={(e) => setEditDescription(e.currentTarget.value)}
            />
            {editIsCached ? (
              <CronExpressionBuilder
                label="Refresh cron"
                description="Pick a preset or choose Custom. Empty (Manual only) clears the schedule on save."
                value={editRefreshCron}
                onChange={setEditRefreshCron}
              />
            ) : (
              <Text size="xs" c="dimmed">
                Virtual datasets have no refresh cron. Switch mode by recreating the dataset.
              </Text>
            )}
            {editError ? <Alert color="red">{editError}</Alert> : null}
            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={() => setEditOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" loading={editMutation.isPending}>
                Save
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}
