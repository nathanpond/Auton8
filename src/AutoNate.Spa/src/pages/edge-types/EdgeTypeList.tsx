import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Grid,
  Group,
  Modal,
  Select,
  Stack,
  Switch,
  Text,
  TextInput
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useCreateEdgeType, useRestoreEdgeType } from "@/hooks/useRecordEdges";
import { listEdgeTypes } from "@/api/recordEdges";
import { CreateEdgeTypeRequest, EdgeType } from "@/types/records";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["12%", "40%", "14%", "18%", "16%"];

export default function EdgeTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const restore = useRestoreEdgeType();
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const onRestore = async (t: EdgeType) => {
    try {
      await restore.mutateAsync(t.id);
      setFlash({ kind: "success", message: `Restored ${t.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const columns = useMemo<DataTableColumn<EdgeType>[]>(
    () => [
      {
        id: "shortCode",
        accessorKey: "shortCode",
        header: "Code",
        cell: ({ row }) => <code>{row.original.shortCode}</code>
      },
      {
        id: "name",
        accessorKey: "name",
        header: "Forward / Inverse",
        cell: ({ row }) => (
          <>
            <Anchor component={Link} to={`/record-edge-types/${row.original.id}`} fw={600}>
              {row.original.name}
            </Anchor>
            {row.original.inverseName && (
              <Text component="span" c="dimmed" ml={8}>
                / {row.original.inverseName}
              </Text>
            )}
          </>
        )
      },
      {
        id: "direction",
        accessorFn: (t) => (t.isDirected ? "Directed" : "Undirected"),
        header: "Direction"
      },
      {
        id: "cardinality",
        accessorKey: "cardinality",
        header: "Cardinality"
      },
      {
        id: "status",
        accessorFn: (t) => (t.isArchived ? "Archived" : "Active"),
        header: "Status",
        cell: ({ row }) =>
          row.original.isArchived ? (
            <Group gap="xs">
              <Badge color="gray" variant="filled">
                Archived
              </Badge>
              <Anchor
                component="button"
                type="button"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation();
                  void onRestore(row.original);
                }}
                disabled={restore.isPending}
              >
                Restore
              </Anchor>
            </Group>
          ) : (
            <Badge color="green" variant="filled">
              Active
            </Badge>
          )
      }
    ],
    [restore.isPending]
  );

  return (
    <>
      <PageHeader
        title="Edge Types"
        description={
          <>
            Edge types describe how records can link together (e.g. <code>Account</code>{" "}
            <em>has contact</em> <code>Contact</code>). Each edge type can carry its own
            configurable data fields.
          </>
        }
      />

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="sm"
        >
          {flash.message}
        </Alert>
      )}

      <DataTable<EdgeType>
        mode="client"
        loadAll={() => listEdgeTypes(includeArchived)}
        queryKey={["edge-types", { includeArchived }]}
        columns={columns}
        rowKey={(t) => t.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search edge types…"
        emptyMessage="No edge types yet. Create one to let records link to each other."
        loadingMessage="Loading edge types…"
        getRowClassName={(t) => (t.isArchived ? "row-archived" : undefined)}
        onRowClick={(t) => navigate(`/record-edge-types/${t.id}`)}
        getRowAriaLabel={(t) => `Open ${t.shortCode}`}
        globalFilterFn={(t, search) => {
          const needle = search.toLowerCase();
          return `${t.shortCode} ${t.name} ${t.inverseName ?? ""}`.toLowerCase().includes(needle);
        }}
        toolbarLeft={
          <Switch
            id="include-archived-edges"
            ml="xs"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.currentTarget.checked)}
            label="Show archived"
          />
        }
        toolbarRight={
          <Button leftSection={<i className="fa fa-plus" />} onClick={() => setModalOpen(true)}>
            New edge type
          </Button>
        }
      />

      {modalOpen && (
        <CreateModal
          onClose={() => setModalOpen(false)}
          onSuccess={(t) => {
            setFlash({ kind: "success", message: `Created edge type ${t.shortCode}.` });
            setModalOpen(false);
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
    </>
  );
}

function CreateModal({
  onClose,
  onSuccess,
  onError
}: {
  onClose: () => void;
  onSuccess: (t: EdgeType) => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateEdgeTypeRequest>({
    shortCode: "",
    name: "",
    inverseName: null,
    isDirected: true,
    allowSelfReference: false,
    cardinality: "many_to_many",
    fromRecordTypeIds: null,
    toRecordTypeIds: null
  });
  const create = useCreateEdgeType();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        ...values,
        shortCode: values.shortCode.trim().toUpperCase(),
        name: values.name.trim(),
        inverseName: values.inverseName?.trim() || null
      });
      onSuccess(created);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title="New Edge Type" size="lg">
      <Box component="form" onSubmit={submit}>
        <Stack gap="md">
          <Grid>
            <Grid.Col span={{ base: 12, md: 3 }}>
              <TextInput
                label="Short code"
                maxLength={8}
                placeholder="HAS"
                value={values.shortCode}
                onChange={(e) => setValues({ ...values, shortCode: e.currentTarget.value })}
                required
                styles={{ input: { textTransform: "uppercase" } }}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 9 }}>
              <TextInput
                label="Forward name"
                placeholder="has contact"
                value={values.name}
                onChange={(e) => setValues({ ...values, name: e.currentTarget.value })}
                required
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 9 }}>
              <TextInput
                label="Inverse name (optional)"
                placeholder="is contact of"
                value={values.inverseName ?? ""}
                onChange={(e) => setValues({ ...values, inverseName: e.currentTarget.value })}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 3 }}>
              <Select
                label="Cardinality"
                value={values.cardinality}
                onChange={(v) =>
                  v && setValues({ ...values, cardinality: v as CreateEdgeTypeRequest["cardinality"] })
                }
                data={[
                  { value: "many_to_many", label: "many_to_many" },
                  { value: "one_to_one", label: "one_to_one" },
                  { value: "one_to_many", label: "one_to_many" },
                  { value: "many_to_one", label: "many_to_one" }
                ]}
                allowDeselect={false}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <Switch
                id="edgetype-directed"
                checked={values.isDirected}
                onChange={(e) => setValues({ ...values, isDirected: e.currentTarget.checked })}
                label="Directed (with optional inverse name)"
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <Switch
                id="edgetype-self-ref"
                checked={values.allowSelfReference}
                onChange={(e) => setValues({ ...values, allowSelfReference: e.currentTarget.checked })}
                label="Allow self-references"
              />
            </Grid.Col>
          </Grid>
        </Stack>
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={create.isPending}>
            Create
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
