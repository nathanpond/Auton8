import { FormEvent, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
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
  Table,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import {
  ExternalConnection,
  ExternalConnectionMetadata,
  TestConnectionResult,
  createExternalConnection,
  deleteExternalConnection,
  listExternalConnections,
  setDefaultExternalConnection,
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

  const listQuery = useQuery({
    queryKey: ["external-connections", "list"],
    queryFn: ({ signal }) => listExternalConnections(undefined, signal)
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["external-connections", "list"] });

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
    onSuccess: invalidate
  });

  const setDefaultMutation = useMutation({
    mutationFn: setDefaultExternalConnection,
    onSuccess: invalidate
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

  return (
    <Card withBorder shadow="sm">
      <Group justify="space-between" align="center" mb="md">
        <Title order={5} m={0}>
          External Connections
        </Title>
        <Button size="xs" leftSection={<i className="fa fa-plus" />} onClick={startNew}>
          New connection
        </Button>
      </Group>

      {listQuery.isLoading && <Text>Loading…</Text>}
      {listQuery.isError && <Text c="red">Failed to load connections.</Text>}
      {listQuery.data && listQuery.data.length === 0 && (
        <Text c="dimmed">
          No external connections yet. Add one to wire an LLM or search provider into the agent.
        </Text>
      )}
      {listQuery.data && listQuery.data.length > 0 && (
        <Table withTableBorder striped verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Kind</Table.Th>
              <Table.Th>Name</Table.Th>
              <Table.Th>Default</Table.Th>
              <Table.Th>Enabled</Table.Th>
              <Table.Th>Secret</Table.Th>
              <Table.Th>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {listQuery.data.map((row) => (
              <Table.Tr key={row.id}>
                <Table.Td>
                  <Code>{row.kind}</Code>
                </Table.Td>
                <Table.Td>
                  <Text fw={600}>{row.name}</Text>
                  {row.description && (
                    <Text size="sm" c="dimmed">
                      {row.description}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  {row.isDefault ? (
                    <Badge color="green" variant="filled">
                      Default
                    </Badge>
                  ) : (
                    <Button
                      size="xs"
                      variant="default"
                      onClick={() => setDefaultMutation.mutate(row.id)}
                      disabled={setDefaultMutation.isPending}
                    >
                      Set default
                    </Button>
                  )}
                </Table.Td>
                <Table.Td>
                  {row.isEnabled ? (
                    <Badge color="blue" variant="filled">
                      Enabled
                    </Badge>
                  ) : (
                    <Badge color="gray" variant="filled">
                      Disabled
                    </Badge>
                  )}
                </Table.Td>
                <Table.Td>
                  {row.secretFingerprint ? (
                    <Code>{row.secretFingerprint}</Code>
                  ) : (
                    <Text size="sm" c="yellow">
                      No secret
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Button.Group>
                    <Button
                      size="xs"
                      variant="outline"
                      color="blue"
                      onClick={() => startEdit(row)}
                    >
                      Edit
                    </Button>
                    <Button
                      size="xs"
                      variant="default"
                      onClick={() => testMutation.mutate(row.id)}
                      disabled={testMutation.isPending && testMutation.variables === row.id}
                    >
                      Test
                    </Button>
                    <Button
                      size="xs"
                      variant="outline"
                      color="red"
                      onClick={() => {
                        if (window.confirm(`Delete "${row.name}"?`)) {
                          deleteMutation.mutate(row.id);
                        }
                      }}
                    >
                      Delete
                    </Button>
                  </Button.Group>
                  {testResults[row.id] && (
                    <Text
                      size="sm"
                      c={testResults[row.id].ok ? "green" : "red"}
                      mt={4}
                    >
                      {testResults[row.id].ok
                        ? `OK (${testResults[row.id].latencyMs}ms${
                            testResults[row.id].modelEcho
                              ? `, ${testResults[row.id].modelEcho}`
                              : ""
                          })`
                        : `Error: ${testResults[row.id].error}`}
                    </Text>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

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
