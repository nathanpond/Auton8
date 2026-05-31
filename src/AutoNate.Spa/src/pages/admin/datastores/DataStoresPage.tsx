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
  NativeSelect,
  Stack,
  Text,
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
  DataStore,
  DataStoreKind,
  createDataStore,
  deleteDataStore,
  kindLabel,
  listDataStores
} from "@/api/datastores";

const QUERY_KEY = ["datastores", "list"] as const;
const COLUMN_WIDTHS = ["1fr", "120px", "2fr", "180px", "60px"];

export default function DataStoresPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState<DataStoreKind>("FileType");
  const [submitError, setSubmitError] = useState<string | null>(null);

  const createMutation = useMutation({
    mutationFn: createDataStore,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setCreateOpen(false);
      setName("");
      setDescription("");
      setKind("FileType");
      setSubmitError(null);
      notifications.show({ message: "Data store created.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteDataStore,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Data store deleted.", color: "green" });
    },
    onError: (err: unknown) => {
      const message = err instanceof Error ? err.message : "Delete failed.";
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
      kind
    });
  }

  const columns = useMemo<DataTableColumn<DataStore>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => <Text fw={500}>{row.original.name}</Text>
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
        )
      }
    ],
    [deleteMutation]
  );

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center">
        <Title order={1}>Data Stores</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={() => setCreateOpen(true)}>
          New data store
        </Button>
      </Group>

      <Text c="dimmed">
        Files and SQL tables stored inside AutoNate. Files-type stores hold any uploads behind a folder
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

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title="New data store" centered>
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
