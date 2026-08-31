import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Divider,
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
import CodeMirror from "@uiw/react-codemirror";
import { javascript } from "@codemirror/lang-javascript";
import { python } from "@codemirror/lang-python";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  CodeTransformer,
  CodeTransformerKind,
  CodeTransformerLanguage,
  TestCodeTransformerResult,
  createCodeTransformer,
  deleteCodeTransformer,
  listCodeTransformers,
  testCodeTransformer,
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

  // Test-run panel state. Only meaningful in edit mode (the test endpoint
  // requires a saved row id). The textarea defaults exercise the
  // transformer's pass-through path so a fresh author can click Run and
  // see something interpretable without authoring sample data.
  const DEFAULT_TEST_INPUT = useMemo(
    () => JSON.stringify([{ id: 1, name: "demo" }, { id: 2, name: "row" }], null, 2),
    []
  );
  const [testInputJson, setTestInputJson] = useState(DEFAULT_TEST_INPUT);
  const [testConfigJson, setTestConfigJson] = useState("{}");
  const [testResult, setTestResult] = useState<TestCodeTransformerResult | null>(null);
  const [testError, setTestError] = useState<string | null>(null);

  function resetForm() {
    setEditingId(null);
    setName("");
    setDescription("");
    setKind("transformer");
    setLanguage("js");
    setCode(JS_TRANSFORMER_STARTER);
    setIsUnsafe(false);
    setSubmitError(null);
    setTestInputJson(DEFAULT_TEST_INPUT);
    setTestConfigJson("{}");
    setTestResult(null);
    setTestError(null);
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

  // Test-run mutation — POSTs the editor's current buffer + sample input
  // to /api/code-transformers/{id}/test. Folds backend "failed with
  // error" responses into the result object rather than reactQuery's
  // onError so the panel can render the message inline.
  const testMutation = useMutation({
    mutationFn: (vars: {
      id: string;
      code: string;
      inputRows: Record<string, unknown>[];
      config: Record<string, string>;
    }) =>
      testCodeTransformer(vars.id, {
        code: vars.code,
        inputRows: vars.inputRows,
        config: vars.config
      }),
    onSuccess: (result) => {
      setTestResult(result);
      setTestError(null);
    },
    onError: (err: unknown) => {
      const message =
        (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason ??
        (err instanceof Error ? err.message : "Test run failed.");
      setTestError(message);
      setTestResult(null);
    }
  });

  function runTest() {
    if (!editingId) return;
    let parsedInput: Record<string, unknown>[];
    let parsedConfig: Record<string, string>;
    try {
      const raw = JSON.parse(testInputJson);
      if (!Array.isArray(raw)) throw new Error("Sample input must be a JSON array of row objects.");
      parsedInput = raw;
    } catch (err) {
      setTestError(`Sample input JSON is invalid: ${err instanceof Error ? err.message : err}`);
      setTestResult(null);
      return;
    }
    try {
      const raw = JSON.parse(testConfigJson);
      if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
        throw new Error("Config must be a JSON object of string→string entries.");
      }
      parsedConfig = raw as Record<string, string>;
    } catch (err) {
      setTestError(`Config JSON is invalid: ${err instanceof Error ? err.message : err}`);
      setTestResult(null);
      return;
    }
    setTestError(null);
    testMutation.mutate({
      id: editingId,
      code,
      inputRows: parsedInput,
      config: parsedConfig
    });
  }

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
            <Box>
              <Text size="sm" fw={500} mb={4}>
                Code
              </Text>
              <Text size="xs" c="dimmed" mb={6}>
                {kind === "transformer"
                  ? "Define a `transform(inputs, config)` function that returns rows."
                  : "Define an `analyze(input, config)` function that returns rows."}
                {language === "python" ? (
                  <>
                    {" "}
                    Python runs in a Pyodide WASM runtime in the executor sidecar; the first
                    test run cold-starts that runtime and can take up to ~10 seconds.
                  </>
                ) : null}
              </Text>
              <Box
                style={{
                  border: "1px solid var(--mantine-color-default-border)",
                  borderRadius: "var(--mantine-radius-sm)",
                  overflow: "hidden"
                }}
                aria-label="Code editor"
              >
                <CodeMirror
                  value={code}
                  onChange={setCode}
                  height="320px"
                  extensions={[
                    language === "python"
                      ? python()
                      : javascript({ jsx: false, typescript: false })
                  ]}
                  basicSetup={{
                    lineNumbers: true,
                    foldGutter: true,
                    highlightActiveLine: true,
                    highlightActiveLineGutter: true,
                    bracketMatching: true,
                    closeBrackets: true,
                    autocompletion: true,
                    indentOnInput: true,
                    syntaxHighlighting: true,
                    history: true
                  }}
                />
              </Box>
            </Box>
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

            {editingId ? (
              <>
                <Divider
                  my="md"
                  label="Test run"
                  labelPosition="left"
                  styles={{ label: { fontWeight: 600 } }}
                />
                <Text size="xs" c="dimmed">
                  Dispatches the editor&apos;s current code (unsaved edits included) against the
                  sample below via the executor sidecar. Output rows render below; sidecar
                  errors are caught and shown inline.
                </Text>
                <Group grow align="flex-start" wrap="wrap">
                  <Textarea
                    aria-label="Sample input (JSON)"
                    label={
                      kind === "transformer"
                        ? "Sample input rows (JSON array)"
                        : "Sample analyzer input (JSON array of rows)"
                    }
                    description="Treated as a single input frame; columns are inferred from the union of keys."
                    autosize
                    minRows={6}
                    value={testInputJson}
                    onChange={(e) => setTestInputJson(e.currentTarget.value)}
                    styles={{
                      input: {
                        fontFamily: "var(--mantine-font-family-monospace)",
                        fontSize: 12
                      }
                    }}
                  />
                  <Textarea
                    aria-label="Test config (JSON)"
                    label="Config (JSON object)"
                    description="Flat string→string map, same shape as a pipeline node config."
                    autosize
                    minRows={6}
                    value={testConfigJson}
                    onChange={(e) => setTestConfigJson(e.currentTarget.value)}
                    styles={{
                      input: {
                        fontFamily: "var(--mantine-font-family-monospace)",
                        fontSize: 12
                      }
                    }}
                  />
                </Group>
                <Group justify="flex-end">
                  <Button
                    variant="default"
                    leftSection={<i className="fa fa-play" />}
                    loading={testMutation.isPending}
                    onClick={runTest}
                  >
                    Run test
                  </Button>
                </Group>
                {testError ? <Alert color="red">{testError}</Alert> : null}
                {testResult ? (
                  testResult.success ? (
                    <Alert color="green" title={`Output (${testResult.outputRows.length} row(s))`}>
                      <Box
                        component="pre"
                        aria-label="Test output"
                        style={{
                          margin: 0,
                          maxHeight: 240,
                          overflow: "auto",
                          fontFamily: "var(--mantine-font-family-monospace)",
                          fontSize: 12,
                          whiteSpace: "pre-wrap"
                        }}
                      >
                        {JSON.stringify(testResult.outputRows, null, 2)}
                      </Box>
                    </Alert>
                  ) : (
                    <Alert color="red" title="Test run failed">
                      <Code block>{testResult.errorMessage ?? "(no message)"}</Code>
                    </Alert>
                  )
                ) : null}
              </>
            ) : null}
          </Stack>
        </form>
      </Modal>
    </Stack>
  );
}
