import { toast } from "@/components/notifications/toast";
import { FormEvent, useCallback, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  NativeSelect,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  DataStore,
  DataStoreKind,
  createDataStore,
  deleteDataStore,
  kindLabel,
  listDataStores,
  updateDataStore
} from "@/api/datastores";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { useDataStoresPagePageContext } from "./useDataStoresPagePageContext";

const QUERY_KEY = ["datastores", "list"] as const;
const COLUMN_WIDTHS = ["1fr", "120px", "2fr", "180px", "60px"];

export default function DataStoresPage() {
  useDocumentTitle("Data Stores");
  const queryClient = useQueryClient();
  // Same Modal renders create + edit (matches DataConnectorsPage / Code
  // TransformersPage). editingId === null means create mode; non-null
  // pre-fills the form from that row and the submit branches to PUT.
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState<DataStoreKind>("FileType");
  const [submitError, setSubmitError] = useState<string | null>(null);

  function resetForm() {
    setEditingId(null);
    setName("");
    setDescription("");
    setKind("FileType");
    setSubmitError(null);
  }

  function openCreate() {
    resetForm();
    setModalOpen(true);
  }

  function openEdit(row: DataStore) {
    setEditingId(row.id);
    setName(row.name);
    setDescription(row.description ?? "");
    // Kind is informational in edit mode (the dropdown is disabled
    // below). Setting it from the row keeps the displayed value
    // accurate without changing the in-flight kind.
    setKind(kindLabel(row.kind));
    setSubmitError(null);
    setModalOpen(true);
  }

  const createMutation = useMutation({
    mutationFn: createDataStore,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setModalOpen(false);
      resetForm();
      toast.success("Data store created.");
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
      updateDataStore(vars.id, {
        name: name.trim(),
        description: description.trim() || null
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      // Detail page caches a single row keyed by id — bust that too so
      // navigating back into the store reflects the new name.
      queryClient.invalidateQueries({ queryKey: ["datastores", "detail"] });
      setModalOpen(false);
      resetForm();
      toast.success("Data store updated.");
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Update failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteDataStore,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      toast.success("Data store deleted.");
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Delete failed.";
      toast.error(message);
    }
  });

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    submit();
  }

  // Internal helper so submit() can be called both from the form's onSubmit
  // and from the page-context provider's submit_modal action without
  // synthesizing a fake event.
  const submit = useCallback(() => {
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    if (editingId) {
      editMutation.mutate({ id: editingId });
    } else {
      createMutation.mutate({
        name: name.trim(),
        description: description.trim() || null,
        kind
      });
    }
  }, [name, description, kind, editingId, createMutation, editMutation]);

  // Mirror of the DataTable's underlying query so the page-context hook can
  // expose the live list to the chatbot. react-query dedupes the request
  // so this is free at runtime.
  const storesQuery = useQuery({ queryKey: QUERY_KEY, queryFn: () => listDataStores() });
  const stores = storesQuery.data ?? [];

  const setModalField = useCallback((field: "name" | "description" | "kind", value: string) => {
    if (field === "name") setName(value);
    else if (field === "description") setDescription(value);
    else if (field === "kind") setKind(value as DataStoreKind);
  }, []);

  const closeModal = useCallback(() => setModalOpen(false), []);

  // Avoid recreating the deleteStore callback every render (the page-context
  // hook would treat it as a fresh value and bump the snapshot version).
  const deleteMutationRef = useRef(deleteMutation);
  deleteMutationRef.current = deleteMutation;
  const deleteStore = useCallback(
    (id: string) => deleteMutationRef.current.mutateAsync(id),
    []
  );

  useDataStoresPagePageContext({
    stores,
    loading: storesQuery.isLoading,
    modal: {
      open: modalOpen,
      editingId,
      name,
      description,
      kind,
      submitError
    },
    openCreate,
    openEdit: (id) => {
      const row = stores.find((s) => s.id === id);
      if (row) openEdit(row);
    },
    closeModal,
    setModalField,
    submitModal: submit,
    deleteStore
  });

  const columns = useMemo<DataTableColumn<DataStore>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Link to={`/datastores/${row.original.id}`}>
            <Text fw={500}>{row.original.name}</Text>
          </Link>
        )
      },
      {
        id: "kind",
        accessorFn: (row) => kindLabel(row.kind),
        header: "Kind",
        cell: ({ row }) => (
          <Badge color={kindLabel(row.original.kind) === "SqlType" ? "blue" : "gray"}>
            {kindLabel(row.original.kind)}
          </Badge>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) => row.original.description ?? <Text c="dimmed">—</Text>
      },
      {
        id: "createdAtUtc",
        accessorKey: "createdAtUtc",
        header: "Created",
        cell: ({ row }) => new Date(row.original.createdAtUtc).toLocaleString()
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Group gap={4} wrap="nowrap">
            <Tooltip label="Edit data store">
              <ActionIcon
                variant="subtle"
                aria-label={`Edit ${row.original.name}`}
                onClick={() => openEdit(row.original)}
              >
                <i className="fa fa-pen-to-square" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete data store">
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={`Delete ${row.original.name}`}
                onClick={() => {
                  if (window.confirm(`Delete data store "${row.original.name}"?`)) {
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
    [deleteMutation]
  );

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>Data Stores</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={openCreate}>
          New data store
        </Button>
      </Group>

      <Text c="dimmed">
        Files and SQL tables stored inside Auton8. Files-type stores hold any uploads behind a folder
        tree; SQL-type stores back per-datastore schemas in the <code>autonate_datastores</code> cluster
        DB and accept CSV ingest.
      </Text>

      <Box>
        <DataTable<DataStore>
          mode="client"
          loadAll={() => listDataStores()}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(row) => row.id}
          columnWidths={COLUMN_WIDTHS}
          emptyMessage="No data stores yet."
          loadingMessage="Loading data stores…"
        />
      </Box>

      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingId ? "Edit data store" : "New data store"}
        centered
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
              data={[
                { value: "FileType", label: "Files — folder tree of uploaded files" },
                { value: "SqlType", label: "SQL — CSV-ingestible tables in autonate_datastores" }
              ]}
              value={kind}
              onChange={(e) => setKind(e.currentTarget.value as DataStoreKind)}
              // Kind is fixed once the store is provisioned — a SQL store
              // owns a per-id schema and role in autonate_datastores;
              // changing the kind would orphan that infrastructure.
              disabled={editingId !== null}
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
    </Stack>
  );
}
