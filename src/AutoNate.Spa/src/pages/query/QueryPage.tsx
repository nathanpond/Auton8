import { useCallback, useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient, useMutation } from "@tanstack/react-query";
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  Code,
  Group,
  Modal,
  Paper,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
  Tooltip
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
import {
  type SavedQuery,
  createSavedQuery,
  listSavedQueries,
  updateSavedQuery
} from "@/api/savedQueries";
import { AxiosError } from "axios";

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

// Mantine Select needs string values; we use the SavedQuery id as the value
// and look the row up from the cache when something is selected.
type SelectGroup = { group: string; items: { value: string; label: string }[] };

function buildSelectGroups(rows: SavedQuery[]): SelectGroup[] {
  const own = rows.filter((r) => r.isOwn).map((r) => ({ value: r.id, label: r.name }));
  const shared = rows
    .filter((r) => !r.isOwn && r.isShared)
    .map((r) => ({ value: r.id, label: r.name }));
  const groups: SelectGroup[] = [];
  if (own.length > 0) groups.push({ group: "My Queries", items: own });
  if (shared.length > 0) groups.push({ group: "Shared Queries", items: shared });
  return groups;
}

export default function QueryPage() {
  const queryClient = useQueryClient();

  const [queryText, setQueryText] = useState<string>("FROM Records");
  const [response, setResponse] = useState<AqlQueryResponse | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [running, setRunning] = useState(false);
  const [executionId, setExecutionId] = useState(0);
  // The query text that last executed successfully. Save is gated on this
  // matching the current text so users can only persist queries that have
  // actually been validated end-to-end.
  const [lastSuccessfulText, setLastSuccessfulText] = useState<string | null>(null);

  // Saved query the user has loaded — its presence flips Save into "Update".
  const [selectedQueryId, setSelectedQueryId] = useState<string | null>(null);

  // Save modal state.
  const [saveOpen, setSaveOpen] = useState(false);
  const [saveName, setSaveName] = useState("");
  const [saveDescription, setSaveDescription] = useState("");
  const [saveShared, setSaveShared] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const savedQueriesQuery = useQuery({
    queryKey: ["saved-queries"],
    queryFn: ({ signal }) => listSavedQueries(signal),
    staleTime: 30_000
  });
  const savedById = useMemo(() => {
    const map = new Map<string, SavedQuery>();
    (savedQueriesQuery.data ?? []).forEach((q) => map.set(q.id, q));
    return map;
  }, [savedQueriesQuery.data]);
  const selectGroups = useMemo(
    () => buildSelectGroups(savedQueriesQuery.data ?? []),
    [savedQueriesQuery.data]
  );

  const selectedQuery = selectedQueryId ? savedById.get(selectedQueryId) ?? null : null;
  // Once selected, the Save button only updates if the actor is the owner.
  // Non-owners viewing a shared row can still tweak the text and save it
  // as a new query (the selection clears the moment they edit anyway).
  const canUpdateSelected = selectedQuery?.isOwn ?? false;

  const runQuery = useCallback(async () => {
    setRunning(true);
    setErrors(null);
    try {
      const res = await executeQuery(queryText);
      setResponse(res);
      setExecutionId((n) => n + 1);
      setLastSuccessfulText(queryText);
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
    // Typing or picking an example invalidates the "currently editing this
    // saved query" indicator so the next Save creates a fresh row.
    setSelectedQueryId(null);
  }, []);

  // ---- Saved-query selection ------------------------------------------------
  const onSelectSavedQuery = useCallback(
    (value: string | null) => {
      setSelectedQueryId(value);
      if (value) {
        const row = savedById.get(value);
        if (row) {
          setQueryText(row.queryText);
          // Loading a saved query is a fresh editing session — clear any
          // prior result so the table doesn't pretend to be filtered by
          // the saved query before the user re-executes.
          setResponse(null);
          setErrors(null);
          setLastSuccessfulText(null);
        }
      }
    },
    [savedById]
  );

  // When the user edits the textarea, drop the selection if the text no
  // longer matches the saved row — otherwise we'd silently overwrite their
  // saved query with whatever they typed when they hit Save.
  useEffect(() => {
    if (!selectedQuery) return;
    if (selectedQuery.queryText !== queryText) {
      // Don't clear immediately on first paint after load; only on real edits
      // (which would differ from the row's stored text).
      setSelectedQueryId(null);
    }
  }, [queryText, selectedQuery]);

  const saveEnabled =
    !running &&
    lastSuccessfulText !== null &&
    lastSuccessfulText === queryText;

  const openSaveModal = useCallback(() => {
    setSaveError(null);
    if (selectedQuery && canUpdateSelected) {
      setSaveName(selectedQuery.name);
      setSaveDescription(selectedQuery.description ?? "");
      setSaveShared(selectedQuery.isShared);
    } else {
      setSaveName("");
      setSaveDescription("");
      setSaveShared(false);
    }
    setSaveOpen(true);
  }, [selectedQuery, canUpdateSelected]);

  const createMutation = useMutation({
    mutationFn: () =>
      createSavedQuery({
        name: saveName.trim(),
        description: saveDescription.trim() ? saveDescription.trim() : null,
        queryText,
        isShared: saveShared
      }),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["saved-queries"] });
      // Select the freshly-created row so the next Save updates instead of
      // creating again. We optimistically seed the cache so the Select
      // dropdown shows the new name without a round trip.
      queryClient.setQueryData<SavedQuery[]>(["saved-queries"], (old) =>
        old ? [...old.filter((q) => q.id !== saved.id), saved] : [saved]
      );
      setSelectedQueryId(saved.id);
      setSaveOpen(false);
    },
    onError: (err) => {
      setSaveError(extractApiError(err));
    }
  });

  const updateMutation = useMutation({
    mutationFn: ({ id }: { id: string }) =>
      updateSavedQuery(id, {
        name: saveName.trim(),
        description: saveDescription.trim() ? saveDescription.trim() : null,
        queryText,
        isShared: saveShared
      }),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["saved-queries"] });
      queryClient.setQueryData<SavedQuery[]>(["saved-queries"], (old) =>
        old ? old.map((q) => (q.id === saved.id ? saved : q)) : [saved]
      );
      setSelectedQueryId(saved.id);
      setSaveOpen(false);
    },
    onError: (err) => {
      setSaveError(extractApiError(err));
    }
  });

  const handleSaveSubmit = useCallback(() => {
    setSaveError(null);
    if (saveName.trim().length === 0) {
      setSaveError("Name is required.");
      return;
    }
    if (selectedQuery && canUpdateSelected) {
      updateMutation.mutate({ id: selectedQuery.id });
    } else {
      createMutation.mutate();
    }
  }, [saveName, selectedQuery, canUpdateSelected, createMutation, updateMutation]);

  const saving = createMutation.isPending || updateMutation.isPending;
  const isUpdate = Boolean(selectedQuery && canUpdateSelected);

  return (
    <Stack gap="md">
      <PageHeader
        title="Query"
        description="Run AQL queries against records, workflows, and other entities. Press Ctrl/Cmd+Enter to execute."
      />

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Select
            label="Saved queries"
            placeholder="Pick a saved query to load…"
            data={selectGroups}
            value={selectedQueryId}
            onChange={onSelectSavedQuery}
            searchable
            clearable
            nothingFoundMessage={
              savedQueriesQuery.isLoading
                ? "Loading…"
                : (savedQueriesQuery.data ?? []).length === 0
                  ? "No saved queries yet."
                  : "No matches."
            }
            disabled={running || saving}
          />
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
            <Group gap="xs">
              <Tooltip
                label={
                  saveEnabled
                    ? isUpdate
                      ? "Update the loaded saved query"
                      : "Save this query"
                    : "Execute the current query successfully to enable save"
                }
                disabled={false}
                withArrow
                position="top"
              >
                <Button
                  variant="default"
                  onClick={openSaveModal}
                  disabled={!saveEnabled}
                  leftSection={<i className="fa fa-floppy-disk" aria-hidden />}
                >
                  {isUpdate ? "Update" : "Save"}
                </Button>
              </Tooltip>
              <Button onClick={() => void runQuery()} loading={running}>
                Execute
              </Button>
            </Group>
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

      <Modal
        opened={saveOpen}
        onClose={() => (saving ? undefined : setSaveOpen(false))}
        title={isUpdate ? "Update saved query" : "Save query"}
        centered
      >
        <Stack gap="sm">
          <TextInput
            label="Name"
            required
            value={saveName}
            onChange={(e) => setSaveName(e.currentTarget.value)}
            disabled={saving}
          />
          <Textarea
            label="Description"
            placeholder="Optional"
            autosize
            minRows={2}
            maxRows={6}
            value={saveDescription}
            onChange={(e) => setSaveDescription(e.currentTarget.value)}
            disabled={saving}
          />
          <Checkbox
            label="Shared (visible to all users)"
            checked={saveShared}
            onChange={(e) => setSaveShared(e.currentTarget.checked)}
            disabled={saving}
          />
          {saveError && (
            <Alert color="red" variant="light">
              {saveError}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setSaveOpen(false)} disabled={saving}>
              Cancel
            </Button>
            <Button onClick={handleSaveSubmit} loading={saving}>
              {isUpdate ? "Update" : "Save"}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}

function extractApiError(err: unknown): string {
  if (err instanceof AxiosError) {
    const data = err.response?.data as { error?: string } | undefined;
    if (data?.error) return data.error;
    if (err.response?.status === 409) {
      return "A saved query with this name already exists.";
    }
  }
  if (err instanceof Error) return err.message;
  return String(err);
}
