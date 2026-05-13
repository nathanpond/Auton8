import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Card,
  Code,
  Group,
  Modal,
  NativeSelect,
  Stack,
  Switch,
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
  ExternalConnection,
  ExternalConnectionMetadata,
  TestConnectionResult,
  createExternalConnection,
  deleteExternalConnection,
  listExternalConnections,
  testExternalConnection,
  updateExternalConnection
} from "@/api/externalConnections";

// One field a connector kind needs the admin to fill in (in addition to the
// always-present name/description/secret/enabled). Adding a new connector
// means adding a row to KINDS below — the modal renders these fields
// generically, so no other code touches the form.
type ConnectionFieldDef = {
  key: string;            // metadata.json key
  label: string;
  placeholder?: string;
  defaultValue?: string;
  hint?: string;          // small grey text under the input
};

type ConnectionKindDef = {
  value: string;
  label: string;
  // Helper text shown under the secret field. Defaults to a generic
  // "Stored encrypted; never echoed back." line.
  secretHint?: string;
  fields: ConnectionFieldDef[];
};

const KINDS: ConnectionKindDef[] = [
  {
    value: "LlmProvider:Anthropic",
    label: "Anthropic (Claude)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.anthropic.com",
        defaultValue: "https://api.anthropic.com"
      }
    ]
  },
  {
    value: "LlmProvider:OpenAI",
    label: "OpenAI (GPT)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.openai.com",
        defaultValue: "https://api.openai.com"
      }
    ]
  },
  {
    value: "WebSearchProvider:Tavily",
    label: "Tavily (web search)",
    fields: [
      {
        key: "baseUrl",
        label: "Base URL",
        placeholder: "https://api.tavily.com",
        defaultValue: "https://api.tavily.com",
        hint: "Override only if proxying through a custom endpoint."
      }
    ]
  }
];

const COLUMN_WIDTHS = ["38%", "28%", "12%", "22%"];
const QUERY_KEY = ["external-connections", "list"] as const;

type FormState = {
  id: string | null;
  kind: string;
  name: string;
  description: string;
  isEnabled: boolean;
  secret: string;
  // Field values keyed by ConnectionFieldDef.key. Empty strings are dropped
  // before submit so the backend metadata stays sparse.
  metadata: Record<string, string>;
};

function findKind(value: string): ConnectionKindDef | undefined {
  return KINDS.find((k) => k.value === value);
}

function defaultMetadataFor(kind: ConnectionKindDef): Record<string, string> {
  const out: Record<string, string> = {};
  for (const f of kind.fields) {
    if (f.defaultValue !== undefined) out[f.key] = f.defaultValue;
  }
  return out;
}

function makeEmptyForm(kindValue: string = KINDS[0].value): FormState {
  const kind = findKind(kindValue) ?? KINDS[0];
  return {
    id: null,
    kind: kind.value,
    name: "",
    description: "",
    isEnabled: true,
    secret: "",
    metadata: defaultMetadataFor(kind)
  };
}

export function ExternalConnectionsPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<FormState | null>(null);
  const [testResults, setTestResults] = useState<Record<string, TestConnectionResult>>({});
  const [pendingDelete, setPendingDelete] = useState<ExternalConnection | null>(null);

  const listQuery = useQuery({
    queryKey: QUERY_KEY,
    queryFn: ({ signal }) => listExternalConnections(undefined, signal)
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: QUERY_KEY });

  const createMutation = useMutation({
    mutationFn: createExternalConnection,
    onSuccess: () => { invalidate(); setEditing(null); }
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...rest }: { id: string } & Parameters<typeof updateExternalConnection>[1]) =>
      updateExternalConnection(id, rest),
    onSuccess: () => { invalidate(); setEditing(null); }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteExternalConnection,
    onSuccess: () => {
      invalidate();
      setPendingDelete(null);
    }
  });

  const testMutation = useMutation({
    mutationFn: testExternalConnection,
    onSuccess: (result, id) => {
      setTestResults((prev) => ({ ...prev, [id]: result }));
    }
  });

  const startNew = () => setEditing(makeEmptyForm());

  const startEdit = (row: ExternalConnection) => {
    const kind = findKind(row.kind);
    // Pre-populate every field this kind declares from the persisted
    // metadata; unknown / missing values become empty strings.
    const metadata: Record<string, string> = {};
    if (kind) {
      for (const f of kind.fields) {
        const v = row.metadata?.[f.key];
        metadata[f.key] = typeof v === "string" ? v : "";
      }
    }
    setEditing({
      id: row.id,
      kind: row.kind,
      name: row.name,
      description: row.description ?? "",
      isEnabled: row.isEnabled,
      secret: "",
      metadata
    });
  };

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!editing) return;

    // Drop empty strings so a blank field doesn't store metadata noise like
    // baseUrl: "". The backend just sees the keys the kind needs.
    const metadata: ExternalConnectionMetadata = {};
    for (const [k, v] of Object.entries(editing.metadata)) {
      if (v !== "") metadata[k] = v;
    }

    if (editing.id) {
      updateMutation.mutate({
        id: editing.id,
        name: editing.name,
        description: editing.description || null,
        isEnabled: editing.isEnabled,
        metadata,
        // Omit when blank so existing secret stays untouched.
        secret: editing.secret.length > 0 ? editing.secret : undefined
      });
    } else {
      createMutation.mutate({
        kind: editing.kind,
        name: editing.name,
        description: editing.description || null,
        isEnabled: editing.isEnabled,
        metadata,
        secret: editing.secret || undefined
      });
    }
  };

  const loadAll = useMemo(
    () => async (): Promise<ExternalConnection[]> => listExternalConnections(),
    []
  );

  const columns = useMemo<DataTableColumn<ExternalConnection>[]>(
    () => [
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        cell: ({ row }) => (
          <>
            <Text fw={600}>{row.original.name}</Text>
            {row.original.description && (
              <Text size="sm" c="dimmed">
                {row.original.description}
              </Text>
            )}
          </>
        )
      },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Kind",
        cell: ({ row }) => <Code>{row.original.kind}</Code>
      },
      {
        id: "isEnabled",
        accessorFn: (r) => (r.isEnabled ? "Enabled" : "Disabled"),
        header: "Enabled",
        cell: ({ row }) =>
          row.original.isEnabled ? (
            <Badge color="blue" variant="filled">
              Enabled
            </Badge>
          ) : (
            <Badge color="gray" variant="filled">
              Disabled
            </Badge>
          )
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => {
          const result = testResults[row.original.id];
          return (
            <>
              <Group gap={4} wrap="nowrap" justify="flex-end">
                <Tooltip label="Edit" withArrow>
                  <ActionIcon
                    variant="subtle"
                    color="blue"
                    aria-label={`Edit ${row.original.name}`}
                    onClick={() => startEdit(row.original)}
                  >
                    <i className="fa fa-pen" />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label="Test" withArrow>
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    aria-label={`Test ${row.original.name}`}
                    onClick={() => testMutation.mutate(row.original.id)}
                    loading={
                      testMutation.isPending && testMutation.variables === row.original.id
                    }
                  >
                    <i className="fa fa-plug" />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label="Delete" withArrow>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    aria-label={`Delete ${row.original.name}`}
                    onClick={() => setPendingDelete(row.original)}
                  >
                    <i className="fa fa-trash" />
                  </ActionIcon>
                </Tooltip>
              </Group>
              {result && (
                <Text size="sm" c={result.ok ? "green" : "red"} mt={4} ta="right">
                  {result.ok
                    ? `OK (${result.latencyMs}ms${result.modelEcho ? `, ${result.modelEcho}` : ""})`
                    : `Error: ${result.error}`}
                </Text>
              )}
            </>
          );
        }
      }
    ],
    [testResults, testMutation]
  );

  return (
    <Card withBorder shadow="sm">
      <Group justify="space-between" align="center" mb="md">
        <Title order={5} m={0}>
          External Connections
        </Title>
      </Group>

      {listQuery.isError && (
        <Alert color="red" variant="light" mb="md">
          Failed to load connections.
        </Alert>
      )}

      <DataTable<ExternalConnection>
        mode="client"
        loadAll={loadAll}
        queryKey={QUERY_KEY}
        columns={columns}
        rowKey={(r) => r.id}
        columnWidths={COLUMN_WIDTHS}
        searchPlaceholder="Search connections…"
        emptyMessage="No external connections yet. Add one to wire an LLM or search provider into the agent."
        loadingMessage="Loading connections…"
        initialSort={[{ id: "kind", desc: false }]}
        // Match against fields that aren't all in column accessors (description
        // would otherwise be ignored by the default scan).
        globalFilterFn={(r, search) => {
          const needle = search.toLowerCase();
          return (
            r.name.toLowerCase().includes(needle) ||
            r.kind.toLowerCase().includes(needle) ||
            (r.description ?? "").toLowerCase().includes(needle)
          );
        }}
        toolbarBeforeSearch={
          <Tooltip label="New connection" withArrow>
            <ActionIcon
              size="lg"
              variant="filled"
              aria-label="New connection"
              onClick={startNew}
            >
              <i className="fa fa-plus" />
            </ActionIcon>
          </Tooltip>
        }
      />

      {editing && (
        <ConnectionFormModal
          form={editing}
          onChange={setEditing}
          onSubmit={submit}
          onCancel={() => setEditing(null)}
          submitting={createMutation.isPending || updateMutation.isPending}
          submitError={(createMutation.error ?? updateMutation.error) as Error | null}
        />
      )}

      <Modal
        opened={pendingDelete !== null}
        onClose={() => (deleteMutation.isPending ? undefined : setPendingDelete(null))}
        title="Delete connection"
        centered
      >
        <Stack gap="md">
          <Text>
            Delete <strong>{pendingDelete?.name}</strong>? Any agent or skill that resolves
            to this connection will fall back to its default — or fail if no other connection
            of kind <Code>{pendingDelete?.kind}</Code> is available.
          </Text>
          <Group justify="flex-end" gap="xs">
            <Button
              variant="default"
              onClick={() => setPendingDelete(null)}
              disabled={deleteMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              color="red"
              leftSection={<i className="fa fa-trash" />}
              loading={deleteMutation.isPending}
              onClick={() => pendingDelete && deleteMutation.mutate(pendingDelete.id)}
            >
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Card>
  );
}

type ConnectionFormModalProps = {
  form: FormState;
  onChange: (next: FormState) => void;
  onSubmit: (e: FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  submitting: boolean;
  submitError: Error | null;
};

function ConnectionFormModal({ form, onChange, onSubmit, onCancel, submitting, submitError }: ConnectionFormModalProps) {
  const kindDef = findKind(form.kind);

  // When creating a new row, switching the kind dropdown resets the
  // dynamic-field values to that kind's defaults (so the admin doesn't
  // have to remember API URLs by hand). Edits never re-key, so we
  // skip this for existing rows.
  useEffect(() => {
    if (form.id !== null) return;
    if (!kindDef) return;
    onChange({ ...form, metadata: defaultMetadataFor(kindDef) });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.kind]);

  const update = (patch: Partial<FormState>) => onChange({ ...form, ...patch });
  const updateField = (fieldKey: string, value: string) =>
    onChange({ ...form, metadata: { ...form.metadata, [fieldKey]: value } });

  return (
    <Modal
      opened
      onClose={onCancel}
      title={form.id ? "Edit connection" : "New connection"}
      centered
    >
      <Box component="form" onSubmit={onSubmit}>
        <Stack gap="md">
          <NativeSelect
            label="Kind"
            value={form.kind}
            onChange={(e) => update({ kind: e.currentTarget.value })}
            disabled={form.id !== null}
            data={KINDS.map((k) => ({ value: k.value, label: k.label }))}
            description={form.id !== null ? "Kind is locked once a connection exists." : undefined}
          />
          <TextInput
            label="Name"
            value={form.name}
            onChange={(e) => update({ name: e.currentTarget.value })}
            placeholder="e.g. Production Anthropic"
            required
          />
          <TextInput
            label="Description"
            value={form.description}
            onChange={(e) => update({ description: e.currentTarget.value })}
            placeholder="Optional"
          />
          {(kindDef?.fields ?? []).map((field) => {
            const value = form.metadata[field.key] ?? "";
            return (
              <TextInput
                key={field.key}
                label={field.label}
                value={value}
                onChange={(e) => updateField(field.key, e.currentTarget.value)}
                placeholder={field.placeholder}
                description={field.hint}
              />
            );
          })}
          <TextInput
            label="API key"
            type="password"
            value={form.secret}
            onChange={(e) => update({ secret: e.currentTarget.value })}
            placeholder={form.id ? "Leave blank to keep existing" : "sk-…"}
            autoComplete="off"
            description={
              kindDef?.secretHint ?? "Stored encrypted via DataProtection. Never echoed back."
            }
          />
          <Switch
            id="connection-enabled"
            checked={form.isEnabled}
            onChange={(e) => update({ isEnabled: e.currentTarget.checked })}
            label="Enabled"
          />
        </Stack>

        {submitError && (
          <Alert color="red" variant="light" mt="md">
            {submitError.message ?? "Save failed."}
          </Alert>
        )}

        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
          <Button type="submit" loading={submitting}>
            {form.id ? "Save changes" : "Create"}
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}
