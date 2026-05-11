import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Grid,
  Group,
  Input,
  Modal,
  Stack,
  Switch,
  TextInput,
  Textarea
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useCreateRecordType, useRestoreRecordType } from "@/hooks/useRecordTypes";
import { listRecordTypes } from "@/api/recordTypes";
import { CreateRecordTypeRequest, RecordType } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["10%", "22%", "26%", "16%", "14%", "12%"];

export default function RecordTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const restore = useRestoreRecordType();

  const onRestore = async (type: RecordType) => {
    try {
      await restore.mutateAsync(type.id);
      setFlash({ kind: "success", message: `Restored ${type.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const columns = useMemo<ColumnDef<RecordType>[]>(
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
        header: "Name",
        cell: ({ row }) => (
          <Anchor component={Link} to={`/record-types/${row.original.id}`} fw={600}>
            {row.original.name}
          </Anchor>
        )
      },
      {
        id: "description",
        accessorKey: "description",
        header: "Description",
        cell: ({ row }) => row.original.description ?? ""
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => formatWhen(row.original.updatedAtUtc)
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
      },
      {
        id: "records",
        header: "Records",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Button
            component={Link}
            to={`/records/${row.original.shortCode}`}
            size="xs"
            variant="default"
            onClick={(e) => e.stopPropagation()}
          >
            Records
          </Button>
        )
      }
    ],
    [restore.isPending]
  );

  return (
    <>
      <PageHeader
        title="Record Types"
        description="Define the records your app manages. Each record type has a short code (used as the key prefix) and a set of fields that every record of that type will have."
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

      <DataTable<RecordType>
        mode="client"
        loadAll={() => listRecordTypes(includeArchived)}
        queryKey={["record-types", { includeArchived }]}
        columns={columns}
        rowKey={(t) => t.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search record types…"
        emptyMessage='No record types yet. Click "New record type" to create one.'
        loadingMessage="Loading record types…"
        getRowClassName={(t) => (t.isArchived ? "row-archived" : undefined)}
        onRowClick={(t) => navigate(`/record-types/${t.id}`)}
        getRowAriaLabel={(t) => `Open ${t.shortCode}`}
        globalFilterFn={(t, search) => {
          const needle = search.toLowerCase();
          return `${t.shortCode} ${t.name} ${t.description ?? ""}`.toLowerCase().includes(needle);
        }}
        toolbarLeft={
          <Switch
            id="include-archived-record-types"
            ml="xs"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.currentTarget.checked)}
            label="Show archived"
          />
        }
        toolbarRight={
          <Button leftSection={<i className="fa fa-plus" />} onClick={() => setModalOpen(true)}>
            New record type
          </Button>
        }
      />

      {modalOpen && (
        <CreateModal
          onClose={() => setModalOpen(false)}
          onSuccess={(t) => {
            setFlash({ kind: "success", message: `Created record type ${t.shortCode}.` });
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
  onSuccess: (t: RecordType) => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateRecordTypeRequest>({
    shortCode: "",
    name: "",
    description: null,
    icon: null,
    color: null
  });
  const create = useCreateRecordType();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        shortCode: values.shortCode.trim().toUpperCase(),
        name: values.name.trim(),
        description: values.description?.trim() || null,
        icon: values.icon?.trim() || null,
        color: values.color?.trim() || null
      });
      onSuccess(created);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title="New Record Type">
      <Box component="form" onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            label="Short code"
            maxLength={8}
            placeholder="ACC"
            value={values.shortCode}
            onChange={(e) => setValues({ ...values, shortCode: e.currentTarget.value })}
            required
            styles={{ input: { textTransform: "uppercase" } }}
            description={
              <>
                2-8 characters, used as the record-key prefix (e.g. <code>ACC-142</code>).
              </>
            }
          />
          <TextInput
            label="Name"
            value={values.name}
            onChange={(e) => setValues({ ...values, name: e.currentTarget.value })}
            required
          />
          <Textarea
            label="Description"
            rows={3}
            value={values.description ?? ""}
            onChange={(e) => setValues({ ...values, description: e.currentTarget.value })}
          />
          <Grid>
            <Grid.Col span={6}>
              <Input.Wrapper label="Icon (FontAwesome)">
                <IconPicker
                  value={values.icon ?? ""}
                  onChange={(v) => setValues({ ...values, icon: v })}
                />
              </Input.Wrapper>
            </Grid.Col>
            <Grid.Col span={6}>
              <Input.Wrapper label="Color">
                <ColorPicker
                  value={values.color ?? ""}
                  onChange={(v) => setValues({ ...values, color: v })}
                />
              </Input.Wrapper>
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

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
