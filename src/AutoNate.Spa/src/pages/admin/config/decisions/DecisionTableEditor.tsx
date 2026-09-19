import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Group,
  List,
  NativeSelect,
  Paper,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  getDecisionTable,
  listDecisionTableVersions,
  publishDecisionTable,
  saveDecisionTable,
  testDecisionTable,
  HIT_POLICY_HELP,
  TYPE_REFS,
  type DecisionColumn,
  type DecisionHitPolicy,
  type DecisionTable,
  type DecisionTableVersion,
  type DecisionTestResult,
  type DecisionTypeRef
} from "@/api/decisionTables";
import { toast } from "@/components/notifications/toast";
import { describeError } from "@/lib/describeError";
import { DECISION_TABLES_QUERY_KEY } from "./DecisionTablesList";

const HIT_POLICIES: DecisionHitPolicy[] = ["FIRST", "UNIQUE", "ANY", "COLLECT"];

/**
 * Write a decision table, try it, publish it (#110).
 *
 * <b>A grid is the control type most often built mouse-only</b>, which the story
 * says outright. Every cell here is a real `<input>` in a real `<table>` with
 * scoped headers, so Tab reaches every one of them in reading order and a screen
 * reader announces which column it is in — no roving tabindex, no custom key
 * handling to get wrong.
 */
export default function DecisionTableEditor() {
  const { id } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const navigate = useNavigate();

  const [draft, setDraft] = useState<DecisionTable | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [testInputs, setTestInputs] = useState<Record<string, string>>({});
  const [testResult, setTestResult] = useState<DecisionTestResult | null>(null);

  const { data: loaded, isLoading, error } = useQuery<DecisionTable>({
    queryKey: ["decision-tables", id],
    queryFn: ({ signal }) => getDecisionTable(id!, signal),
    enabled: Boolean(id)
  });

  const { data: versions = [] } = useQuery<DecisionTableVersion[]>({
    queryKey: ["decision-tables", id, "versions"],
    queryFn: ({ signal }) => listDecisionTableVersions(id!, signal),
    enabled: Boolean(id)
  });

  useEffect(() => {
    if (loaded) setDraft(loaded);
  }, [loaded]);

  const save = useMutation({
    mutationFn: (table: DecisionTable) => saveDecisionTable(table),
    onSuccess: (saved) => {
      setDraft(saved);
      setErrors([]);
      toast.success("Saved.");
      qc.invalidateQueries({ queryKey: DECISION_TABLES_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ["decision-tables", saved.id] });
    },
    onError: (err) => {
      // Server-side validation comes back as a list, and all of it is shown.
      // Surfacing only the first would make an author fix a forty-rule table one
      // round trip at a time, which is what this story exists to avoid.
      const list = extractErrors(err);
      setErrors(list);
      if (list.length === 0) toast.error(`Could not save. ${describeError(err)}`);
    }
  });

  const publish = useMutation({
    mutationFn: () => publishDecisionTable(id!),
    onSuccess: (published) => {
      setDraft(published);
      setErrors([]);
      toast.success(`Published version ${published.publishedVersionNumber}.`);
      qc.invalidateQueries({ queryKey: DECISION_TABLES_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ["decision-tables", published.id] });
      qc.invalidateQueries({ queryKey: ["decision-tables", published.id, "versions"] });
    },
    onError: (err) => {
      const list = extractErrors(err);
      setErrors(list);
      if (list.length === 0) toast.error(`Could not publish. ${describeError(err)}`);
    }
  });

  const runTest = useMutation({
    mutationFn: () => testDecisionTable(id!, coerceInputs(draft, testInputs)),
    onSuccess: setTestResult,
    onError: (err) => {
      setTestResult(null);
      const list = extractErrors(err);
      toast.error(list.length > 0 ? list.join(" ") : `Could not evaluate. ${describeError(err)}`);
    }
  });

  const update = useCallback((patch: Partial<DecisionTable>) => {
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));
  }, []);

  const setCell = useCallback(
    (ruleIndex: number, kind: "inputEntries" | "outputEntries", cellIndex: number, value: string) => {
      setDraft((prev) => {
        if (!prev) return prev;
        const rules = prev.rules.map((rule, r) =>
          r !== ruleIndex
            ? rule
            : { ...rule, [kind]: rule[kind].map((c, i) => (i === cellIndex ? value : c)) }
        );
        return { ...prev, rules };
      });
    },
    []
  );

  const addRule = useCallback(() => {
    setDraft((prev) =>
      prev
        ? {
            ...prev,
            rules: [
              ...prev.rules,
              {
                id: crypto.randomUUID(),
                // Sized to the CURRENT columns. A rule whose cell count does not
                // match is refused at save, so creating one ragged would hand the
                // author an error they did not cause.
                inputEntries: prev.inputs.map(() => ""),
                outputEntries: prev.outputs.map(() => "")
              }
            ]
          }
        : prev
    );
  }, []);

  const addColumn = useCallback((kind: "inputs" | "outputs") => {
    setDraft((prev) => {
      if (!prev) return prev;
      const n = prev[kind].length + 1;
      const column: DecisionColumn = {
        id: crypto.randomUUID(),
        label: `${kind === "inputs" ? "Input" : "Output"} ${n}`,
        name: `${kind === "inputs" ? "input" : "output"}${n}`,
        typeRef: "string"
      };
      const cellKey = kind === "inputs" ? "inputEntries" : "outputEntries";
      // Every existing rule grows a cell in the same operation. Adding a column
      // without widening the rules leaves every one of them ragged, and the table
      // cannot be saved until the author fixes damage the editor did.
      return {
        ...prev,
        [kind]: [...prev[kind], column],
        rules: prev.rules.map((r) => ({ ...r, [cellKey]: [...r[cellKey], ""] }))
      };
    });
  }, []);

  const removeColumn = useCallback((kind: "inputs" | "outputs", index: number) => {
    setDraft((prev) => {
      if (!prev) return prev;
      const cellKey = kind === "inputs" ? "inputEntries" : "outputEntries";
      return {
        ...prev,
        [kind]: prev[kind].filter((_, i) => i !== index),
        rules: prev.rules.map((r) => ({ ...r, [cellKey]: r[cellKey].filter((_, i) => i !== index) }))
      };
    });
  }, []);

  const hitPolicyData = useMemo(
    () => HIT_POLICIES.map((p) => ({ value: p, label: p })),
    []
  );

  if (isLoading) {
    return (
      <Box py="md">
        <Text size="sm" c="dimmed">
          Loading decision table…
        </Text>
      </Box>
    );
  }

  if (error || !draft) {
    return (
      <Box py="md">
        <Alert color="red" variant="light" role="alert">
          Could not load this decision table. {error ? describeError(error) : ""}
        </Alert>
      </Box>
    );
  }

  return (
    <Box py="md">
      <PageHeader
        title={draft.name}
        description={
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              {draft.decisionKey}
            </Text>
            {draft.publishedVersionNumber === null ? (
              <Badge color="gray" variant="light">
                Never published
              </Badge>
            ) : (
              <Badge color="green" variant="light">
                Published v{draft.publishedVersionNumber}
              </Badge>
            )}
            {draft.isDraft && draft.publishedVersionNumber !== null && (
              <Badge color="yellow" variant="light">
                Draft changes
              </Badge>
            )}
          </Group>
        }
        actions={
          <Group gap="xs">
            <Button variant="default" onClick={() => navigate("/admin/config/decision-tables")}>
              Back
            </Button>
            <Button
              variant="light"
              loading={save.isPending}
              onClick={() => save.mutate(draft)}
            >
              Save
            </Button>
            <Button loading={publish.isPending} onClick={() => publish.mutate()}>
              Publish
            </Button>
          </Group>
        }
      />

      {/*
        An in-page Alert, not a toast: validation is a condition belonging to the
        page. It is still true after a reload and should still be there when the
        author comes back to look at the cell it names.
      */}
      {errors.length > 0 && (
        <Alert
          color="red"
          variant="light"
          role="alert"
          title={`${errors.length} ${errors.length === 1 ? "problem" : "problems"} to fix`}
          mb="md"
        >
          <List size="sm">
            {errors.map((e) => (
              <List.Item key={e}>{e}</List.Item>
            ))}
          </List>
        </Alert>
      )}

      <Stack gap="md">
        <Paper withBorder radius="md" p="md">
          <Group align="flex-end" gap="md" wrap="wrap">
            <TextInput
              label="Name"
              value={draft.name}
              onChange={(e) => update({ name: e.target.value })}
            />
            <TextInput
              label="Key"
              value={draft.decisionKey}
              onChange={(e) => update({ decisionKey: e.target.value })}
              description="A business rule task references the table by this."
            />
            <NativeSelect
              label="Hit policy"
              value={draft.hitPolicy}
              onChange={(e) => update({ hitPolicy: e.target.value as DecisionHitPolicy })}
              data={hitPolicyData}
              // Explained in the UI, because the story says so and because FIRST
              // and COLLECT produce different results from the same rules -- a
              // difference no amount of staring at the grid reveals.
              description={HIT_POLICY_HELP[draft.hitPolicy]}
            />
          </Group>
        </Paper>

        <Paper withBorder radius="md" p="md">
          <Group justify="space-between" mb="sm">
            <Title order={5}>Rules</Title>
            <Group gap="xs">
              <Button size="xs" variant="light" onClick={() => addColumn("inputs")}>
                Add input
              </Button>
              <Button size="xs" variant="light" onClick={() => addColumn("outputs")}>
                Add output
              </Button>
              <Button size="xs" onClick={addRule}>
                Add rule
              </Button>
            </Group>
          </Group>

          <Table withTableBorder withColumnBorders>
            <Table.Thead>
              <Table.Tr>
                <Table.Th scope="col" w={40}>
                  #
                </Table.Th>
                {draft.inputs.map((column, i) => (
                  <ColumnHeader
                    key={column.id}
                    column={column}
                    kind="inputs"
                    index={i}
                    onChange={(next) =>
                      update({ inputs: draft.inputs.map((c, j) => (j === i ? next : c)) })
                    }
                    onRemove={() => removeColumn("inputs", i)}
                  />
                ))}
                {draft.outputs.map((column, i) => (
                  <ColumnHeader
                    key={column.id}
                    column={column}
                    kind="outputs"
                    index={i}
                    onChange={(next) =>
                      update({ outputs: draft.outputs.map((c, j) => (j === i ? next : c)) })
                    }
                    onRemove={() => removeColumn("outputs", i)}
                  />
                ))}
                <Table.Th scope="col" w={50} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {draft.rules.map((rule, r) => (
                <Table.Tr key={rule.id}>
                  <Table.Th scope="row">{r + 1}</Table.Th>
                  {rule.inputEntries.map((cell, c) => (
                    <Table.Td key={`${rule.id}-i${c}`}>
                      <TextInput
                        size="xs"
                        // Named by rule AND column, because "cell" tells a screen
                        // reader nothing about where it is, and the server's error
                        // messages name exactly this pair.
                        aria-label={`Rule ${r + 1}, ${draft.inputs[c]?.label ?? `input ${c + 1}`}`}
                        placeholder="any"
                        value={cell}
                        onChange={(e) => setCell(r, "inputEntries", c, e.target.value)}
                      />
                    </Table.Td>
                  ))}
                  {rule.outputEntries.map((cell, c) => (
                    <Table.Td key={`${rule.id}-o${c}`}>
                      <TextInput
                        size="xs"
                        aria-label={`Rule ${r + 1}, ${draft.outputs[c]?.label ?? `output ${c + 1}`}`}
                        value={cell}
                        onChange={(e) => setCell(r, "outputEntries", c, e.target.value)}
                      />
                    </Table.Td>
                  ))}
                  <Table.Td>
                    <Tooltip label={`Delete rule ${r + 1}`}>
                      <ActionIcon
                        variant="subtle"
                        color="red"
                        aria-label={`Delete rule ${r + 1}`}
                        onClick={() =>
                          update({ rules: draft.rules.filter((_, i) => i !== r) })
                        }
                      >
                        <i className="fa fa-trash" />
                      </ActionIcon>
                    </Tooltip>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>

          {draft.rules.length === 0 && (
            <Text size="sm" c="dimmed" mt="sm">
              No rules yet. A table with no rules deploys and matches nothing.
            </Text>
          )}
        </Paper>

        <Paper withBorder radius="md" p="md">
          <Title order={5} mb="xs">
            Try it
          </Title>
          <Text size="sm" c="dimmed" mb="sm">
            Runs the <strong>published</strong> table through the same path a process uses, so what
            you see here is what a process would get. Publish first if you have unsaved changes you
            want to try.
          </Text>

          <Group align="flex-end" gap="sm" wrap="wrap">
            {draft.inputs.map((column) => (
              <TextInput
                key={column.id}
                label={column.label}
                description={column.typeRef}
                value={testInputs[column.name] ?? ""}
                onChange={(e) =>
                  setTestInputs((prev) => ({ ...prev, [column.name]: e.target.value }))
                }
              />
            ))}
            <Button
              variant="light"
              loading={runTest.isPending}
              disabled={draft.publishedVersionNumber === null}
              onClick={() => runTest.mutate()}
            >
              Evaluate
            </Button>
          </Group>

          {draft.publishedVersionNumber === null && (
            <Alert color="gray" variant="light" role="presentation" mt="sm">
              This table has never been published, so there is nothing deployed to evaluate.
            </Alert>
          )}

          {testResult && (
            <Alert
              color={testResult.matched ? "green" : "yellow"}
              variant="light"
              role="status"
              mt="sm"
              title={testResult.matched ? "A rule matched" : "No rule matched"}
            >
              {testResult.matched ? (
                <List size="sm">
                  {testResult.outputs.map((row, i) => (
                    <List.Item key={i}>
                      {Object.entries(row)
                        .map(([k, v]) => `${k} = ${String(v)}`)
                        .join(", ")}
                    </List.Item>
                  ))}
                </List>
              ) : (
                <Text size="sm">
                  Nothing in this table covers those inputs. A process would read the output
                  variables as null.
                </Text>
              )}
            </Alert>
          )}
        </Paper>

        {versions.length > 0 && (
          <Paper withBorder radius="md" p="md">
            <Title order={5} mb="xs">
              Published versions
            </Title>
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th scope="col">Version</Table.Th>
                  <Table.Th scope="col">Published</Table.Th>
                  <Table.Th scope="col">Engine decision</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {versions.map((v) => (
                  <Table.Tr key={v.id}>
                    <Table.Td>v{v.versionNumber}</Table.Td>
                    <Table.Td>{new Date(v.publishedAtUtc).toLocaleString()}</Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {v.decisionId}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Paper>
        )}
      </Stack>
    </Box>
  );
}

function ColumnHeader({
  column,
  kind,
  index,
  onChange,
  onRemove
}: {
  column: DecisionColumn;
  kind: "inputs" | "outputs";
  index: number;
  onChange: (next: DecisionColumn) => void;
  onRemove: () => void;
}) {
  const noun = kind === "inputs" ? "input" : "output";
  return (
    <Table.Th scope="col">
      <Stack gap={4}>
        <Group gap={4} wrap="nowrap">
          <TextInput
            size="xs"
            aria-label={`Label for ${noun} ${index + 1}`}
            value={column.label}
            onChange={(e) => onChange({ ...column, label: e.target.value })}
          />
          <Tooltip label={`Remove ${column.label}`}>
            <ActionIcon
              variant="subtle"
              color="red"
              size="sm"
              aria-label={`Remove ${noun} ${column.label}`}
              onClick={onRemove}
            >
              <i className="fa fa-xmark" />
            </ActionIcon>
          </Tooltip>
        </Group>
        <TextInput
          size="xs"
          aria-label={`Variable name for ${column.label}`}
          placeholder="variable"
          value={column.name}
          onChange={(e) => onChange({ ...column, name: e.target.value })}
        />
        <NativeSelect
          size="xs"
          aria-label={`Type of ${column.label}`}
          value={column.typeRef}
          onChange={(e) => onChange({ ...column, typeRef: e.target.value as DecisionTypeRef })}
          data={TYPE_REFS.map((t) => ({ value: t, label: t }))}
        />
      </Stack>
    </Table.Th>
  );
}

/**
 * The server's `{ errors: [...] }` body, or an empty list when the failure was
 * something else entirely.
 */
function extractErrors(err: unknown): string[] {
  const data = (err as { response?: { data?: { errors?: unknown } } }).response?.data;
  return Array.isArray(data?.errors) ? (data.errors as string[]).map(String) : [];
}

/**
 * Turns the try-it panel's text fields into the types the columns declare.
 *
 * The panel is text because a cell is text; the API is typed because the engine
 * binds by type name. Sending every value as a string would make a number column
 * match nothing, which reads exactly like a table that decides nothing.
 */
function coerceInputs(
  table: DecisionTable | null,
  raw: Record<string, string>
): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const column of table?.inputs ?? []) {
    const value = raw[column.name];
    if (value === undefined || value === "") continue;
    switch (column.typeRef) {
      case "number": {
        const n = Number(value);
        out[column.name] = Number.isNaN(n) ? value : n;
        break;
      }
      case "boolean":
        out[column.name] = value === "true";
        break;
      default:
        out[column.name] = value;
    }
  }
  return out;
}
