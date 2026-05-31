import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useQuery, useQueryClient, useMutation } from "@tanstack/react-query";
import {
  ActionIcon,
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
import AqlEditor from "@/components/aql-editor/AqlEditor";
import AqlHelpModal from "@/components/aql-editor/AqlHelpModal";
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
import SavedQueryShareModal from "@/pages/query/SavedQueryShareModal";
import { useQueryPagePageContext } from "@/pages/query/useQueryPagePageContext";
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

// Returns the deep-link path for a row when the column is an identity-bearing
// field on a known entity. Returns null when no link applies (unknown entity,
// non-identity column, or the required key/id sibling field isn't in the
// projection). Hardcoded for the three entities the SPA can route to today.
//
// Workflows are linked to the studio's bare /workflow route since the studio
// doesn't yet accept a per-model deep-link. Once it does, the right side of
// the Workflows entry below grows a `?model=${id}` query string and the
// metadata-driven approach (a per-column "linkable" flag returned by the
// backend) becomes worth it.
function deepLinkFor(
  entity: string | null,
  columnName: string,
  row: AqlRow
): string | null {
  if (!entity) return null;
  const e = entity.toLowerCase();
  const c = columnName.toLowerCase();

  if (e === "records" && (c === "name" || c === "id" || c === "key")) {
    // The record-detail route is keyed by Key (not Id), so even when the
    // user clicks an Id cell we route via the row's Key. If Key isn't in
    // the projection we drop the link rather than invent a URL.
    const key = row["Key"];
    if (typeof key === "string" && key.length > 0) {
      return `/record/${encodeURIComponent(key)}`;
    }
    return null;
  }

  if ((e === "flows" || e === "workflowexecutions")
      && (c === "flowname" || c === "id")) {
    const id = row["Id"];
    if (typeof id === "string" && id.length > 0) {
      return `/executions/${encodeURIComponent(id)}`;
    }
    return null;
  }

  if (e === "workflows" && c === "modelname") {
    // No per-model deep-link target yet; the studio's bare route is the best
    // we can do until WorkflowStudio learns to honor a query param.
    return "/workflow";
  }

  return null;
}

function renderCell(
  value: unknown,
  dataType: AqlDataType,
  columnName: string,
  row: AqlRow,
  entity: string | null
): ReactNode {
  const text = formatCell(value, dataType);
  if (text === "") return text;
  const link = deepLinkFor(entity, columnName, row);
  if (link === null) return text;
  return (
    <Anchor component={Link} to={link}>
      {text}
    </Anchor>
  );
}

// Extracts the entity name from the user's last successfully-executed query
// text. Returns null when the FROM clause is missing (the AQL parser defaults
// to "Records" in that case, so we mirror that here so deep-links still work
// for shorthand queries like `Name = "x"`).
function entityFromQueryText(queryText: string | null): string | null {
  if (!queryText) return null;
  const m = /\bfrom\s+([A-Za-z_][A-Za-z0-9_]*)/i.exec(queryText);
  if (m) return m[1];
  // The AQL grammar accepts a bare WHERE without a FROM, defaulting to Records.
  if (/\bwhere\b|\border\s+by\b|\bcolumns\b|\bgroup\b/i.test(queryText)) {
    return "Records";
  }
  return "Records";
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

  // Help modal state.
  const [helpOpen, setHelpOpen] = useState(false);

  // Save modal state.
  const [saveOpen, setSaveOpen] = useState(false);
  const [saveName, setSaveName] = useState("");
  const [saveDescription, setSaveDescription] = useState("");
  const [saveShared, setSaveShared] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  // Phase 3 share modal — opens for the currently-selected owned query.
  const [shareOpen, setShareOpen] = useState(false);

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

  const indexedRows = useMemo<IndexedRow[]>(() => {
    if (!response) return [];
    return response.rows.map((row, idx) => ({
      __rowId: String(idx),
      ...row
    }));
  }, [response]);

  // Drives the deep-link logic in renderCell — derived from the text the
  // user last *executed* (not the editor's current contents) so a partial
  // edit doesn't break the links for rows that came from the prior run.
  const executedEntity = useMemo(
    () => entityFromQueryText(lastSuccessfulText),
    [lastSuccessfulText]
  );

  const columns = useMemo<DataTableColumn<IndexedRow>[]>(() => {
    if (!response) return [];
    return response.columns.map((col) => ({
      id: col.name,
      accessorFn: (row) => row[col.name],
      header: col.name,
      enableSorting: true,
      cell: ({ row }) =>
        renderCell(row.original[col.name], col.dataType, col.name, row.original, executedEntity)
    }));
  }, [response, executedEntity]);

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

  const openSaveModal = useCallback(
    (defaults?: { name?: string; description?: string; isShared?: boolean }) => {
      setSaveError(null);
      if (selectedQuery && canUpdateSelected) {
        setSaveName(defaults?.name ?? selectedQuery.name);
        setSaveDescription(defaults?.description ?? selectedQuery.description ?? "");
        setSaveShared(defaults?.isShared ?? selectedQuery.isShared);
      } else {
        setSaveName(defaults?.name ?? "");
        setSaveDescription(defaults?.description ?? "");
        setSaveShared(defaults?.isShared ?? false);
      }
      setSaveOpen(true);
    },
    [selectedQuery, canUpdateSelected]
  );

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

  // Chatbot page-awareness: expose editor + last-result state and a small
  // catalog of mutating actions (set/append/run/save) the assistant can
  // call via apply_page_action with confirmed=true semantics.
  useQueryPagePageContext({
    queryText,
    lastSuccessfulText,
    response,
    errors,
    running,
    selectedQuery,
    setQueryText,
    runQuery,
    openSaveModal
  });

  return (
    <Stack gap="md">
      <PageHeader
        title="Query"
        description="Run AQL queries against records, workflows, and other entities. Press Ctrl/Cmd+Enter to execute."
      />

      <Paper p="md" withBorder>
        <Stack gap="sm">
          <Group justify="flex-end" gap="xs">
            <Tooltip label="AQL reference" withArrow position="left">
              <ActionIcon
                variant="subtle"
                color="gray"
                onClick={() => setHelpOpen(true)}
                aria-label="Open AQL help"
              >
                <i className="fa fa-circle-question" aria-hidden />
              </ActionIcon>
            </Tooltip>
          </Group>
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
          <AqlEditor
            value={queryText}
            onChange={setQueryText}
            onExecute={() => { if (!running) void runQuery(); }}
            readOnly={running}
            placeholder='FROM Records WHERE RecordType = "Car"'
            minHeight="6em"
            maxHeight="22em"
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
                  onClick={() => openSaveModal()}
                  disabled={!saveEnabled}
                  leftSection={<i className="fa fa-floppy-disk" aria-hidden />}
                >
                  {isUpdate ? "Update" : "Save"}
                </Button>
              </Tooltip>
              {selectedQuery && canUpdateSelected ? (
                <Tooltip label="Generate a public share link for this query" withArrow position="top">
                  <Button
                    variant="default"
                    onClick={() => setShareOpen(true)}
                    leftSection={<i className="fa fa-share-nodes" aria-hidden />}
                  >
                    Share
                  </Button>
                </Tooltip>
              ) : null}
              <Button onClick={() => void runQuery()} loading={running}>
                Execute
              </Button>
            </Group>
          </Group>
        </Stack>
      </Paper>

      <SavedQueryShareModal
        savedQuery={selectedQuery}
        opened={shareOpen}
        onClose={() => setShareOpen(false)}
      />

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

      <AqlHelpModal opened={helpOpen} onClose={() => setHelpOpen(false)} />
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
