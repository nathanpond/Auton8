import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Modal,
  Stack,
  Switch,
  Text,
  TextInput
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { CreateFormRequest, FormSummary, listForms } from "@/api/forms";
import { useCreateForm, useDeleteForm } from "@/hooks/useForms";
import { DataTable } from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["14%", "30%", "16%", "13%", "17%", "10%"];

export default function FormsList() {
  const [modalOpen, setModalOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(
    null
  );
  const deleteForm = useDeleteForm();

  const onDelete = async (form: FormSummary) => {
    if (!window.confirm(`Delete form "${form.name}" (${form.shortCode})?`)) return;
    try {
      await deleteForm.mutateAsync(form.id);
      setFlash({ kind: "success", message: `Deleted ${form.shortCode}.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const columns = useMemo<DataTableColumn<FormSummary>[]>(
    () => [
      {
        id: "shortCode",
        accessorKey: "shortCode",
        header: "Short code",
        cell: ({ row }) => <code>{row.original.shortCode}</code>
      },
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <Anchor component={Link} to={`/admin/config/forms/${row.original.id}`} fw={600}>
            {row.original.name}
          </Anchor>
        )
      },
      {
        id: "status",
        accessorFn: (f) =>
          f.publishedVersionNumber === null
            ? "Draft"
            : f.isDraft
              ? "Has changes"
              : f.siteAvailable
                ? "Live"
                : "Published",
        header: "Status",
        cell: ({ row }) => <StatusBadges form={row.original} />
      },
      {
        id: "versions",
        accessorFn: (f) => f.draftVersionNumber,
        header: "Versions",
        cell: ({ row }) => (
          <>
            <Text size="sm" c="dimmed" component="span">
              Draft v{row.original.draftVersionNumber}
            </Text>
            {row.original.publishedVersionNumber !== null && (
              <>
                <br />
                <Text size="sm" c="dimmed" component="span">
                  Pub v{row.original.publishedVersionNumber}
                </Text>
              </>
            )}
          </>
        )
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => formatWhen(row.original.updatedAtUtc)
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Box>
            <ActionIcon
              variant="outline"
              color="red"
              size="sm"
              title="Delete form"
              aria-label={`Delete ${row.original.shortCode}`}
              disabled={deleteForm.isPending}
              onClick={(e) => {
                e.stopPropagation();
                void onDelete(row.original);
              }}
            >
              <i className="fa fa-trash" />
            </ActionIcon>
          </Box>
        )
      }
    ],
    [deleteForm.isPending]
  );

  return (
    <>
      <PageHeader
        title="Forms"
        description={
          <>
            Author JSX forms that can be bound to records, workflow tasks, or any other data
            source. Each save snapshots a version; publishing makes the form live at{" "}
            <code>/form/&lt;shortcode&gt;</code> when Site-available is on.
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

      <DataTable<FormSummary>
        mode="client"
        loadAll={() => listForms()}
        queryKey={["forms"]}
        columns={columns}
        rowKey={(f) => f.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "shortCode", desc: false }]}
        searchPlaceholder="Search forms…"
        emptyMessage="No forms yet. Click 'New form' to create one."
        loadingMessage="Loading forms…"
        globalFilterFn={(f, search) => {
          const needle = search.toLowerCase();
          return `${f.shortCode} ${f.name}`.toLowerCase().includes(needle);
        }}
        toolbarRight={
          <Button leftSection={<i className="fa fa-plus" />} onClick={() => setModalOpen(true)}>
            New form
          </Button>
        }
      />

      {modalOpen && (
        <CreateModal
          onClose={() => setModalOpen(false)}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
    </>
  );
}

function StatusBadges({ form }: { form: FormSummary }) {
  if (form.publishedVersionNumber === null) {
    return (
      <Badge color="gray" variant="filled">
        Draft
      </Badge>
    );
  }
  if (form.isDraft) {
    return (
      <Group gap="xs">
        <Badge color="green" variant="filled">
          Published
        </Badge>
        <Badge color="yellow" variant="filled">
          Has changes
        </Badge>
      </Group>
    );
  }
  return form.siteAvailable ? (
    <Badge color="green" variant="filled">
      Live
    </Badge>
  ) : (
    <Badge color="green" variant="filled">
      Published
    </Badge>
  );
}

function CreateModal({
  onClose,
  onError
}: {
  onClose: () => void;
  onError: (m: string) => void;
}) {
  const [values, setValues] = useState<CreateFormRequest>({
    name: "",
    shortCode: "",
    siteAvailable: false
  });
  const create = useCreateForm();
  const navigate = useNavigate();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        shortCode: values.shortCode.trim().toLowerCase(),
        siteAvailable: values.siteAvailable
      });
      navigate(`/admin/config/forms/${created.id}`);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title="New Form">
      <Box component="form" onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            label="Name"
            value={values.name}
            onChange={(e) => setValues({ ...values, name: e.currentTarget.value })}
            required
            autoFocus
          />
          <TextInput
            label="Short code"
            placeholder="contact-form"
            value={values.shortCode}
            onChange={(e) => setValues({ ...values, shortCode: e.currentTarget.value })}
            required
            styles={{ input: { textTransform: "lowercase" } }}
            description={
              <>
                Used in <code>/form/&lt;short-code&gt;</code> and{" "}
                <code>/formdev/&lt;short-code&gt;</code>.
              </>
            }
          />
          <Switch
            id="create-site-available"
            checked={values.siteAvailable}
            onChange={(e) => setValues({ ...values, siteAvailable: e.currentTarget.checked })}
            label="Site-available (can be loaded at /form/<short-code> once published)"
          />
        </Stack>
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={create.isPending}>
            Create &amp; edit
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
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}
