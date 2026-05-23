import { useCallback, useMemo, useState } from "react";
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  Paper,
  Stack,
  Text,
  Textarea
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  DataTable,
  type DataTableColumn
} from "@/components/data-table/DataTable";
import {
  type AqlDataType,
  type AqlQueryResponse,
  type AqlRow,
  executeQuery,
  extractValidationErrors
} from "@/api/aql";

type IndexedRow = AqlRow & { __rowId: string };

const QUERY_EXAMPLES: string[] = [
  "FROM Records",
  "FROM Records WHERE RecordType = \"Car\"",
  "FROM Records WHERE CreatedDate > -2w ORDER BY CreatedDate DESC",
  "FROM Workflows WHERE Published = True ORDER BY ModelName"
];

const NUMBER_FORMATTER = new Intl.NumberFormat();

function formatCell(value: unknown, dataType: AqlDataType): string {
  if (value === null || value === undefined) return "";
  switch (dataType) {
    case "number":
      if (typeof value === "number") return NUMBER_FORMATTER.format(value);
      if (typeof value === "string" && value !== "" && !Number.isNaN(Number(value))) {
        return NUMBER_FORMATTER.format(Number(value));
      }
      return String(value);
    case "bool":
      if (typeof value === "boolean") return value ? "Yes" : "No";
      return String(value);
    case "date": {
      const d = typeof value === "string" ? new Date(value) : (value as Date | null);
      if (!d || Number.isNaN(d.valueOf())) return String(value ?? "");
      return d.toLocaleString();
    }
    case "json":
      try {
        return JSON.stringify(value);
      } catch {
        return String(value);
      }
    default:
      return typeof value === "object" ? JSON.stringify(value) : String(value);
  }
}

export default function QueryPage() {
  const [queryText, setQueryText] = useState<string>("FROM Records");
  const [response, setResponse] = useState<AqlQueryResponse | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [running, setRunning] = useState(false);
  const [executionId, setExecutionId] = useState(0);

  const runQuery = useCallback(async () => {
    setRunning(true);
    setErrors(null);
    try {
      const res = await executeQuery(queryText);
      setResponse(res);
      setExecutionId((n) => n + 1);
    } catch (err) {
      const ve = extractValidationErrors(err);
      if (ve) {
        setErrors(ve);
        setResponse(null);
      } else {
        setErrors([err instanceof Error ? err.message : String(err)]);
      }
    } finally {
      setRunning(false);
    }
  }, [queryText]);

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      // Ctrl/Cmd+Enter runs the query, matching most query consoles.
      if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
        e.preventDefault();
        if (!running) void runQuery();
      }
    },
    [running, runQuery]
  );

  const indexedRows = useMemo<IndexedRow[]>(() => {
    if (!response) return [];
    return response.rows.map((row, idx) => ({
      __rowId: String(idx),
      ...row
    }));
  }, [response]);

  const columns = useMemo<DataTableColumn<IndexedRow>[]>(() => {
    if (!response) return [];
    return response.columns.map((col) => ({
      id: col.name,
      accessorFn: (row) => row[col.name],
      header: col.name,
      enableSorting: true,
      cell: ({ row }) => formatCell(row.original[col.name], col.dataType)
    }));
  }, [response]);

  const columnWidths = useMemo(() => {
    if (!response) return [];
    const n = response.columns.length;
    if (n === 0) return [];
    const pct = Math.max(8, Math.floor(100 / n));
    return Array<string>(n).fill(`${pct}%`);
  }, [response]);

  const loadAll = useCallback(async () => indexedRows, [indexedRows]);

  const fillExample = useCallback((q: string) => {
    setQueryText(q);
  }, []);

  return (
    <Stack gap="md">
      <PageHeader
        title="Query"
        description="Run AQL queries against records, workflows, and other entities. Press Ctrl/Cmd+Enter to execute."
      />

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Textarea
            value={queryText}
            onChange={(e) => setQueryText(e.currentTarget.value)}
            onKeyDown={onKeyDown}
            autosize
            minRows={4}
            maxRows={14}
            placeholder="FROM Records WHERE RecordType = &quot;Car&quot;"
            styles={{ input: { fontFamily: "var(--mantine-font-family-monospace, monospace)" } }}
            disabled={running}
          />
          <Group justify="space-between">
            <Group gap="xs" wrap="wrap">
              <Text size="xs" c="dimmed">
                Examples:
              </Text>
              {QUERY_EXAMPLES.map((ex) => (
                <Anchor
                  key={ex}
                  component="button"
                  type="button"
                  size="xs"
                  onClick={() => fillExample(ex)}
                  disabled={running}
                >
                  <Code>{ex}</Code>
                </Anchor>
              ))}
            </Group>
            <Button onClick={() => void runQuery()} loading={running}>
              Execute
            </Button>
          </Group>
        </Stack>
      </Paper>

      {errors && errors.length > 0 && (
        <Alert color="red" title="Query errors" icon={<i className="fa fa-circle-exclamation" />}>
          <Stack gap={4}>
            {errors.map((e, i) => (
              <Text key={i} size="sm">
                {e}
              </Text>
            ))}
          </Stack>
        </Alert>
      )}

      {response && (
        <Paper p="md" withBorder>
          <Stack gap="sm">
            <Group justify="space-between" align="center">
              <Group gap="xs">
                <Badge variant="light">{response.rows.length} rows</Badge>
                <Badge variant="light" color="gray">
                  {response.columns.length} columns
                </Badge>
                <Text size="xs" c="dimmed">
                  Executed in {response.durationMs} ms
                </Text>
              </Group>
            </Group>

            {response.truncated && (
              <Alert color="yellow" title="Results were truncated">
                Showing the first {response.rows.length} rows. Add filters or use LIMIT to
                narrow the result.
              </Alert>
            )}

            {response.columns.length === 0 ? (
              <Text c="dimmed" size="sm">
                Query returned no columns. (For Workflows: you may not have permission to
                view workflow models.)
              </Text>
            ) : (
              <DataTable<IndexedRow>
                mode="client"
                queryKey={["aql", executionId]}
                loadAll={loadAll}
                columns={columns}
                rowKey={(row) => row.__rowId}
                columnWidths={columnWidths}
                searchEnabled={false}
                emptyMessage="Query returned no rows."
              />
            )}
          </Stack>
        </Paper>
      )}
    </Stack>
  );
}
