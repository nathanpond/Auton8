import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  ActionIcon,
  Alert,
  Anchor,
  Box,
  Button,
  Grid,
  Group,
  Input,
  Modal,
  Stack,
  Switch,
  TextInput,
  Textarea,
  Tooltip
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useCreateRecordType } from "@/hooks/useRecordTypes";
import { listRecordTypes } from "@/api/recordTypes";
import { CreateRecordTypeRequest, RecordType } from "@/types/records";
import IconPicker from "@/components/IconPicker";
import ColorPicker from "@/components/ColorPicker";
import { DataTable } from "@/components/data-table/DataTable";
import { ArchivedBadge } from "@/components/ArchivedBadge";

const COLUMN_WIDTHS = ["10%", "26%", "30%", "18%", "10%", "6%"];

export default function RecordTypeList() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const columns = useMemo<DataTableColumn<RecordType>[]>(
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
          <>
            <Anchor component={Link} to={`/record-types/${row.original.id}`} fw={600}>
              {row.original.name}
            </Anchor>
            {row.original.isArchived && <ArchivedBadge />}
          </>
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
        cell: ({ row }) => {
          const iso = row.original.updatedAtUtc;
          const d = new Date(iso);
          if (Number.isNaN(d.getTime())) return iso;
          return (
            <Tooltip label={d.toLocaleString()} withArrow>
              <span>{d.toLocaleDateString()}</span>
            </Tooltip>
          );
        }
      },
      {
        id: "fieldCount",
        accessorKey: "fieldCount",
        header: "# of Fields",
        cell: ({ row }) => row.original.fieldCount
      },
      {
        id: "records",
        header: "",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Tooltip label="View records" withArrow>
            <ActionIcon
              component={Link}
              to={`/records/${row.original.shortCode}`}
              variant="subtle"
              color="gray"
              aria-label={`View ${row.original.shortCode} records`}
              onClick={(e) => e.stopPropagation()}
            >
              <i className="fa fa-table-list" />
            </ActionIcon>
          </Tooltip>
        )
      }
    ],
    []
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
        toolbarBeforeSearch={
          <Tooltip label="New record type" withArrow>
            <ActionIcon
              size="lg"
              variant="filled"
              aria-label="New record type"
              onClick={() => setModalOpen(true)}
            >
              <i className="fa fa-plus" />
            </ActionIcon>
          </Tooltip>
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

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
