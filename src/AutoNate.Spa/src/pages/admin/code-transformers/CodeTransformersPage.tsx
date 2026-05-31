import { FormEvent, useEffect, useMemo, useState } from "react";
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
  Switch,
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
  CodeTransformer,
  CodeTransformerKind,
  CodeTransformerLanguage,
  createCodeTransformer,
  deleteCodeTransformer,
  listCodeTransformers,
  updateCodeTransformer
} from "@/api/codeTransformers";

const QUERY_KEY = ["code-transformers", "list"] as const;
const COLUMN_WIDTHS = ["1fr", "140px", "140px", "180px", "180px", "130px"];

const JS_TRANSFORMER_STARTER = `function transform(inputs, config) {
  // inputs is an array of frames; inputs[0] is rows[] from the upstream node.
  // Return rows[] or { columns, rows }.
  return inputs[0];
}`;

const PYTHON_TRANSFORMER_STARTER = `def transform(inputs, config):
    # inputs is a list of row lists; inputs[0] is rows from the upstream node.
    # Return rows directly or {"columns": [...], "rows": [...]}.
    return inputs[0]
`;

const JS_ANALYZER_STARTER = `function analyze(input, config) {
  // input is rows[] from the upstream node.
  // Return rows[] or { columns, rows }.
  return [{ count: input.length }];
}`;

const PYTHON_ANALYZER_STARTER = `def analyze(input, config):
    return [{"count": len(input)}]
`;

function starterCode(kind: CodeTransformerKind, language: CodeTransformerLanguage): string {
  if (kind === "transformer") {
    return language === "python" ? PYTHON_TRANSFORMER_STARTER : JS_TRANSFORMER_STARTER;
  }
  return language === "python" ? PYTHON_ANALYZER_STARTER : JS_ANALYZER_STARTER;
}

export default function CodeTransformersPage() {
  const queryClient = useQueryClient();
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState<CodeTransformerKind>("transformer");
  const [language, setLanguage] = useState<CodeTransformerLanguage>("js");
  const [code, setCode] = useState(JS_TRANSFORMER_STARTER);
  const [isUnsafe, setIsUnsafe] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  function resetForm() {
    setEditingId(null);
    setName("");
    setDescription("");
    setKind("transformer");
    setLanguage("js");
    setCode(JS_TRANSFORMER_STARTER);
    setIsUnsafe(false);
    setSubmitError(null);
  }

  useEffect(() => {
    if (editingId !== null) return;
    const starters = [
      JS_TRANSFORMER_STARTER,
      PYTHON_TRANSFORMER_STARTER,
      JS_ANALYZER_STARTER,
      PYTHON_ANALYZER_STARTER
    ];
    if (starters.includes(code)) {
      setCode(starterCode(kind, language));
    }
  }, [kind, language, code, editingId]);

  const createMutation = useMutation({
    mutationFn: createCodeTransformer,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setModalOpen(false);
      resetForm();
      notifications.show({ message: "Code transformer created.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Create failed.");
      setSubmitError(message);
    }
  });

  const updateMutation = useMutation({
    mutationFn: (vars: { id: string }) =>
      updateCodeTransformer(vars.id, {
        name,
        description: description || null,
        code,
        isUnsafe
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      setModalOpen(false);
      resetForm();
      notifications.show({ message: "Code transformer updated.", color: "green" });
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Update failed.");
      setSubmitError(message);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteCodeTransformer,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      notifications.show({ message: "Code transformer deleted.", color: "green" });
    }
  });

  function openCreate() {
    resetForm();
    setModalOpen(true);
  }

  function openEdit(row: CodeTransformer) {
    setEditingId(row.id);
    setName(row.name);
    setDescription(row.description ?? "");
    setKind(row.kind);
    setLanguage(row.language);
    setCode(row.code);
    setIsUnsafe(row.isUnsafe);
    setSubmitError(null);
    setModalOpen(true);
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setSubmitError("Name is required.");
      return;
    }
    if (editingId) {
      updateMutation.mutate({ id: editingId });
    } else {
      createMutation.mutate({
        name: name.trim(),
        description: description.trim() || null,
        kind,
        language,
        code,
        isUnsafe
      });
    }
  }

  const columns = useMemo<DataTableColumn<CodeTransformer>[]>(
    () => [
      { id: "name", accessorKey: "name", header: "Name", cell: ({ row }) => row.original.name },
      {
        id: "kind",
        accessorKey: "kind",
        header: "Kind",
        cell: ({ row }) => <Badge variant="light">{row.original.kind}</Badge>
      },
      {
        id: "language",
        accessorKey: "language",
        header: "Language",
        cell: ({ row }) => <Badge variant="light">{row.original.language}</Badge>
      },
      {
        id: "isUnsafe",
        accessorKey: "isUnsafe",
        header: "Sandbox",
        cell: ({ row }) =>
          row.original.isUnsafe ? (
            <Badge color="red">Trusted (unsafe)</Badge>
          ) : (
            <Badge color="green">Sandboxed</Badge>
          )
      },
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => new Date(row.original.updatedAtUtc).toLocaleString()
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <Group gap={4} wrap="nowrap">
            <Tooltip label="Edit code">
              <ActionIcon variant="subtle" aria-label={`Edit ${row.original.name}`} onClick={() => openEdit(row.original)}>
                <i className="fa fa-pen-to-square" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete">
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={`Delete ${row.original.name}`}
                onClick={() => {
                  if (window.confirm(`Delete code transformer "${row.original.name}"?`)) {
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
        <Title order={1}>Code Transformers</Title>
        <Button leftSection={<i className="fa fa-plus" />} onClick={openCreate}>
          New code transformer
        </Button>
      </Group>

      <Text c="dimmed">
        Write JS or Python transformer / analyzer code that runs in a sandboxed sidecar (V8 isolate
        for JS, Pyodide for Python). Reference these by name from a pipeline transformer or analyzer
        node alongside the built-in catalog. Trusted code can opt out of the sandbox; flipping that
        switch requires the <code>executeunsafe</code> permission.
      </Text>

      <Box>
        <DataTable<CodeTransformer>
          mode="client"
          loadAll={() => listCodeTransformers()}
          queryKey={QUERY_KEY}
          columns={columns}
          rowKey={(row) => row.id}
          columnWidths={COLUMN_WIDTHS}
          emptyMessage="No code transformers yet."
          loadingMessage="Loading code transformers…"
        />
      </Box>

      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingId ? "Edit code transformer" : "New code transformer"}
        size="xl"
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
            <Group grow>
              <NativeSelect
                label="Kind"
                data={[
                  { value: "transformer", label: "Transformer" },
                  { value: "analyzer", label: "Analyzer" }
                ]}
                value={kind}
                onChange={(e) => setKind(e.currentTarget.value as CodeTransformerKind)}
                disabled={editingId !== null}
              />
              <NativeSelect
                label="Language"
                data={[
                  { value: "js", label: "JavaScript (isolated-vm)" },
                  { value: "python", label: "Python (Pyodide)" }
                ]}
                value={language}
                onChange={(e) => setLanguage(e.currentTarget.value as CodeTransformerLanguage)}
                disabled={editingId !== null}
              />
            </Group>
            <Switch
              label="Trusted code — skip sandbox (requires executeunsafe permission)"
              checked={isUnsafe}
              onChange={(e) => setIsUnsafe(e.currentTarget.checked)}
            />
            <Textarea
              label="Code"
              description={
                kind === "transformer"
                  ? "Define a `transform(inputs, config)` function that returns rows."
                  : "Define an `analyze(input, config)` function that returns rows."
              }
              autosize
              minRows={12}
              value={code}
              onChange={(e) => setCode(e.currentTarget.value)}
              styles={{
                input: {
                  fontFamily: "var(--mantine-font-family-monospace)",
                  fontSize: 13,
                  lineHeight: 1.5
                }
              }}
            />
            {submitError ? <Alert color="red">{submitError}</Alert> : null}
            <Group justify="flex-end" mt="sm">
              <Button variant="default" onClick={() => setModalOpen(false)}>
                Cancel
              </Button>
              <Button
                type="submit"
                loading={createMutation.isPending || updateMutation.isPending}
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
