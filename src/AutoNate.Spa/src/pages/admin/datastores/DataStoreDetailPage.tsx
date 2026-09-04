import { toast } from "@/components/notifications/toast";
import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AxiosError } from "axios";
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Checkbox,
  Group,
  List,
  Loader,
  Modal,
  NativeSelect,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip
} from "@mantine/core";
import { Dropzone } from "@mantine/dropzone";
import PageHeader from "@/components/PageHeader";
import AqlEditor from "@/components/aql-editor/AqlEditor";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import {
  type CsvColumn,
  type CsvIngestConflict,
  type CsvIngestMode,
  type CsvIngestPreview,
  type CsvIngestResult,
  type DataStore,
  type DataStoreTable,
  type DataStoreTablePreview,
  getDataStore,
  ingestCsv,
  isCsvIngestConflict,
  kindLabel,
  listDataStoreTables,
  previewCsvIngest,
  previewDataStoreTable
} from "@/api/datastores";
import { type Dataset, listDatasets } from "@/api/datasets";
import {
  type AqlQueryResponse,
  executeQuery,
  extractValidationErrors
} from "@/api/aql";
import DataStoreFileManager from "./DataStoreFileManager";
import { useDataStoreDetailPagePageContext } from "./useDataStoreDetailPagePageContext";

export default function DataStoreDetailPage() {
  const { id } = useParams<{ id: string }>();
  const storeId = id ?? "";

  const storeQuery = useQuery<DataStore>({
    queryKey: ["datastores", "detail", storeId],
    queryFn: () => getDataStore(storeId),
    enabled: !!storeId
  });

  // Parallel tables query (SqlPanel has its own; react-query dedupes). We
  // need a copy at the top level so the page-context provider can expose
  // the table list to the chatbot without crossing component boundaries.
  const isSql = storeQuery.data ? kindLabel(storeQuery.data.kind) === "SqlType" : false;
  const tablesQuery = useQuery<DataStoreTable[]>({
    queryKey: ["datastores", storeId, "tables"],
    queryFn: ({ signal }) => listDataStoreTables(storeId, signal),
    enabled: !!storeId && isSql
  });

  useDataStoreDetailPagePageContext({
    store: storeQuery.data ?? null,
    isFiles: storeQuery.data ? kindLabel(storeQuery.data.kind) === "FileType" : false,
    tables: tablesQuery.data ?? [],
    tablesLoading: tablesQuery.isLoading,
    refreshTables: () => tablesQuery.refetch()
  });

  useDocumentTitle(storeQuery.data ? `${storeQuery.data.name} — Data store` : "Data store");

  if (!storeId) {
    return <Alert color="red">Missing data store id.</Alert>;
  }
  if (storeQuery.isLoading) {
    return (
      <Stack align="center" mt="xl">
        <Loader />
      </Stack>
    );
  }
  if (storeQuery.error || !storeQuery.data) {
    return (
      <Stack gap="sm">
        <Alert color="red">Failed to load data store.</Alert>
        <Group>
          <Button component={Link} to="/datastores" variant="default">
            Back to data stores
          </Button>
        </Group>
      </Stack>
    );
  }

  const store = storeQuery.data;
  const isFiles = kindLabel(store.kind) === "FileType";

  return (
    <Stack gap="md">
      <PageHeader
        title={
          <Group gap="xs">
            <span>{store.name}</span>
            <Badge color={isFiles ? "gray" : "blue"}>{kindLabel(store.kind)}</Badge>
          </Group>
        }
        // PageHeader wraps `description` in a Mantine <Text> (which renders
        // as <p>), so it must be inline-safe — nesting a Stack or another
        // <Text> in here is an HTML hierarchy error. Keep the description
        // text-only and put the back link in the actions slot.
        description={store.description ?? undefined}
        actions={
          <Button
            component={Link}
            to="/datastores"
            variant="default"
            leftSection={<i className="fa fa-arrow-left" />}
          >
            Back to data stores
          </Button>
        }
      />

      {isFiles ? (
        <DataStoreFileManager key={storeId} storeId={storeId} storeName={store.name} />
      ) : (
        <SqlPanel storeId={storeId} />
      )}
    </Stack>
  );
}

// ----------------------------------------------------------------------------
// SQL-type sub-panel
// ----------------------------------------------------------------------------

const POSTGRES_TYPES = [
  "text",
  "integer",
  "bigint",
  "numeric",
  "boolean",
  "date",
  "timestamptz",
  "uuid",
  "jsonb"
];

type IngestStep =
  | { kind: "idle" }
  | { kind: "previewing"; file: File }
  | { kind: "ready"; file: File; tableName: string; columns: CsvColumn[]; sampleRowCount: number }
  | { kind: "ingesting"; file: File; tableName: string; columns: CsvColumn[]; mode: CsvIngestMode }
  | {
      kind: "conflict";
      file: File;
      tableName: string;
      columns: CsvColumn[];
      conflict: CsvIngestConflict;
    }
  | { kind: "done"; result: CsvIngestResult };

function SqlPanel({ storeId }: { storeId: string }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState<IngestStep>({ kind: "idle" });
  const [error, setError] = useState<string | null>(null);
  const [selectedTableId, setSelectedTableId] = useState<string | null>(null);

  const tablesQuery = useQuery<DataStoreTable[]>({
    queryKey: ["datastores", storeId, "tables"],
    queryFn: ({ signal }) => listDataStoreTables(storeId, signal),
    enabled: !!storeId
  });

  // Default-select the first table whenever the list arrives or the
  // currently-selected one disappears (e.g. after a re-ingest).
  useEffect(() => {
    const rows = tablesQuery.data;
    if (!rows || rows.length === 0) {
      if (selectedTableId !== null) setSelectedTableId(null);
      return;
    }
    if (!selectedTableId || !rows.some((r) => r.id === selectedTableId)) {
      setSelectedTableId(rows[0].id);
    }
  }, [tablesQuery.data, selectedTableId]);

  function reset() {
    setStep({ kind: "idle" });
    setError(null);
  }

  async function onFileDropped(file: File) {
    setError(null);
    setStep({ kind: "previewing", file });
    try {
      const preview: CsvIngestPreview = await previewCsvIngest(storeId, file);
      setStep({
        kind: "ready",
        file,
        tableName: preview.suggestedTableName,
        columns: preview.columns,
        sampleRowCount: preview.sampleRowCount
      });
    } catch (err) {
      setError(describeError(err, "Preview failed."));
      setStep({ kind: "idle" });
    }
  }

  async function runIngest(
    file: File,
    tableName: string,
    columns: CsvColumn[],
    mode: CsvIngestMode
  ) {
    setError(null);
    setStep({ kind: "ingesting", file, tableName, columns, mode });
    try {
      const result = await ingestCsv(storeId, tableName, columns, file, mode);
      setStep({ kind: "done", result });
      const verb = result.appended
        ? "Appended:"
        : result.replaced
          ? result.schemaChanged
            ? "Replaced (schema changed):"
            : "Replaced:"
          : "Ingested:";
      // A schema change during ingest is a warning: the load worked, but the
      // shape moved under whatever was reading it.
      const ingestMessage =
        `${verb} ${result.rowsInserted.toLocaleString()} row(s) in ${result.tableName}.`;
      if (result.schemaChanged) {
        toast.warning(ingestMessage);
      } else {
        toast.success(ingestMessage);
      }
      await queryClient.invalidateQueries({ queryKey: ["datastores", storeId, "tables"] });
      setSelectedTableId(result.tableId);
    } catch (err) {
      // 409 with a conflict body → drop into the conflict UI so the
      // operator can review row-count + schema diff + bound-dataset impact
      // and pick append vs replace. Distinct conflictKinds drive which
      // affordances are available; both keep us in the conflict view.
      const axiosErr = err as AxiosError | undefined;
      const status = axiosErr?.response?.status;
      const body = axiosErr?.response?.data;
      if (status === 409 && isCsvIngestConflict(body)) {
        setStep({ kind: "conflict", file, tableName, columns, conflict: body });
        return;
      }
      setError(describeError(err, "Ingest failed."));
      setStep({ kind: "ready", file, tableName, columns, sampleRowCount: 0 });
    }
  }

  function onConfirmIngest() {
    if (step.kind !== "ready") return;
    void runIngest(step.file, step.tableName, step.columns, "insert");
  }

  function onPickAppend() {
    if (step.kind !== "conflict") return;
    void runIngest(step.file, step.tableName, step.columns, "append");
  }

  function onPickReplace() {
    if (step.kind !== "conflict") return;
    void runIngest(step.file, step.tableName, step.columns, "replace");
  }

  function updateColumn(index: number, patch: Partial<CsvColumn>) {
    if (step.kind !== "ready") return;
    setStep({
      ...step,
      columns: step.columns.map((c, i) => (i === index ? { ...c, ...patch } : c))
    });
  }

  const tables = tablesQuery.data ?? [];
  const selectedTable = tables.find((t) => t.id === selectedTableId) ?? null;

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group justify="space-between" align="center">
          <Box>
            <Text fw={500}>CSV ingest</Text>
            <Text size="sm" c="dimmed">
              Drop a CSV; Auton8 infers a Postgres column schema from a sample of rows,
              you confirm the table name and types, and the rows land in this datastore&apos;s
              per-store schema in <code>autonate_datastores</code>.
            </Text>
          </Box>
          <Button
            leftSection={<i className="fa fa-file-csv" />}
            onClick={() => {
              reset();
              setOpen(true);
            }}
          >
            Ingest CSV
          </Button>
        </Group>
      </Card>

      {tablesQuery.isLoading ? (
        <Group justify="center" py="md">
          <Loader size="sm" />
        </Group>
      ) : tables.length === 0 ? (
        <Alert color="gray" title="No tables yet">
          This SQL datastore is empty. Ingest a CSV to create the first table, then come
          back here to browse it and query it with AQL.
        </Alert>
      ) : (
        <>
          <Card withBorder padding="md">
            <Stack gap="sm">
              <Group justify="space-between" align="flex-end" wrap="nowrap">
                <Select
                  label="Ingested tables"
                  description={`${tables.length} table${tables.length === 1 ? "" : "s"} in this datastore.`}
                  data={tables.map((t) => ({
                    value: t.id,
                    label: `${t.tableName}  (${t.rowCount.toLocaleString()} row${t.rowCount === 1 ? "" : "s"}, ${t.columns.length} cols)`
                  }))}
                  value={selectedTableId}
                  onChange={(v) => setSelectedTableId(v)}
                  searchable
                  allowDeselect={false}
                  style={{ flex: 1 }}
                />
              </Group>
              {selectedTable ? (
                <Text size="xs" c="dimmed">
                  Physical location:{" "}
                  <code>
                    {selectedTable.schemaName}.{selectedTable.tableName}
                  </code>{" "}
                  in <code>autonate_datastores</code>.
                </Text>
              ) : null}
            </Stack>
          </Card>

          {selectedTable ? (
            <>
              <ColumnsCard table={selectedTable} />
              <PreviewCard storeId={storeId} table={selectedTable} />
              <AqlPlaygroundCard storeId={storeId} table={selectedTable} />
            </>
          ) : null}
        </>
      )}

      <Modal
        opened={open}
        onClose={() => {
          setOpen(false);
          reset();
        }}
        title="Ingest CSV"
        centered
        size="lg"
      >
        <Stack gap="sm">
          {step.kind === "idle" ? (
            <Dropzone
              onDrop={(files) => {
                const f = files[0];
                if (f) void onFileDropped(f);
              }}
              onReject={(rejections) => {
                const first = rejections[0]?.errors?.[0];
                setError(first?.message ?? "Drop a single CSV file.");
              }}
              multiple={false}
              maxFiles={1}
              aria-label="CSV dropzone"
            >
              <Group justify="center" gap="md" mih={140} style={{ pointerEvents: "none" }}>
                <Dropzone.Idle>
                  <i
                    className="fa fa-file-csv"
                    style={{ fontSize: 32, color: "var(--mantine-color-dimmed)" }}
                  />
                </Dropzone.Idle>
                <div>
                  <Text size="sm" fw={500}>
                    Drop a CSV here or click to browse
                  </Text>
                  <Text size="xs" c="dimmed" mt={4}>
                    The first row is treated as a header.
                  </Text>
                </div>
              </Group>
            </Dropzone>
          ) : null}

          {step.kind === "previewing" ? (
            <Group justify="center" py="lg">
              <Loader size="sm" />
              <Text size="sm">Inspecting columns…</Text>
            </Group>
          ) : null}

          {step.kind === "ready" || step.kind === "ingesting" ? (
            <>
              <TextInput
                label="Table name"
                value={step.tableName}
                onChange={(e) =>
                  step.kind === "ready" &&
                  setStep({ ...step, tableName: e.currentTarget.value })
                }
                description={`Inferred from ${step.file.name}. Lowercase, snake-case recommended.`}
                disabled={step.kind === "ingesting"}
              />
              <Text size="sm" fw={500} mt="sm">
                Columns ({step.columns.length})
                {step.kind === "ready" && step.sampleRowCount ? (
                  <Text component="span" size="xs" c="dimmed" ml="xs">
                    inferred from {step.sampleRowCount} sample row(s)
                  </Text>
                ) : null}
              </Text>
              <Card withBorder padding="xs">
                <Table>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Column name</Table.Th>
                      <Table.Th style={{ width: 200 }}>Postgres type</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {step.columns.map((col, i) => (
                      <Table.Tr key={i}>
                        <Table.Td>
                          <TextInput
                            value={col.name}
                            onChange={(e) => updateColumn(i, { name: e.currentTarget.value })}
                            disabled={step.kind === "ingesting"}
                            aria-label={`Column ${i + 1} name`}
                          />
                        </Table.Td>
                        <Table.Td>
                          <NativeSelect
                            value={col.postgresType}
                            data={
                              POSTGRES_TYPES.includes(col.postgresType)
                                ? POSTGRES_TYPES
                                : [col.postgresType, ...POSTGRES_TYPES]
                            }
                            onChange={(e) =>
                              updateColumn(i, { postgresType: e.currentTarget.value })
                            }
                            disabled={step.kind === "ingesting"}
                            aria-label={`Column ${i + 1} type`}
                          />
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Card>
            </>
          ) : null}

          {step.kind === "conflict" ? (
            <ConflictView
              storeId={storeId}
              newColumns={step.columns}
              conflict={step.conflict}
              onAppend={onPickAppend}
              onReplace={onPickReplace}
              onBack={() =>
                setStep({
                  kind: "ready",
                  file: step.file,
                  tableName: step.tableName,
                  columns: step.columns,
                  sampleRowCount: 0
                })
              }
            />
          ) : null}

          {step.kind === "done" ? (
            <Alert
              color={step.result.schemaChanged ? "yellow" : "green"}
              title={
                step.result.appended
                  ? "Appended"
                  : step.result.replaced
                    ? step.result.schemaChanged
                      ? "Replaced — schema changed"
                      : "Replaced"
                    : "Ingest complete"
              }
            >
              {step.result.appended ? (
                <>
                  Appended <strong>{step.result.rowsInserted.toLocaleString()}</strong> row(s) to{" "}
                  <code>{step.result.schemaName}.{step.result.tableName}</code>. Total now{" "}
                  <strong>
                    {((step.result.previousRowCount ?? 0) + step.result.rowsInserted).toLocaleString()}
                  </strong>{" "}
                  row(s).
                </>
              ) : step.result.replaced ? (
                <>
                  Replaced <code>{step.result.schemaName}.{step.result.tableName}</code>:{" "}
                  <strong>{step.result.previousRowCount?.toLocaleString() ?? "?"}</strong> prior
                  row(s) → <strong>{step.result.rowsInserted.toLocaleString()}</strong> new row(s).
                  {step.result.schemaChanged ? (
                    <>
                      {" "}
                      The column schema changed — any Virtual dataset bound to this table may
                      need its column list refreshed.
                    </>
                  ) : null}
                </>
              ) : (
                <>
                  Created table <code>{step.result.schemaName}.{step.result.tableName}</code> and
                  inserted <strong>{step.result.rowsInserted.toLocaleString()}</strong> row(s).
                  Query it from the AQL playground with{" "}
                  <code>FROM Dataset(&quot;…&quot;)</code> after defining a Dataset over this
                  table.
                </>
              )}
            </Alert>
          ) : null}

          {error ? <Alert color="red">{error}</Alert> : null}

          <Group justify="flex-end">
            <Button
              variant="default"
              onClick={() => {
                setOpen(false);
                reset();
              }}
            >
              {step.kind === "done" ? "Close" : "Cancel"}
            </Button>
            {step.kind === "ready" ? (
              <Button onClick={onConfirmIngest}>Ingest</Button>
            ) : null}
            {step.kind === "ingesting" ? (
              <Button disabled loading>
                {step.mode === "replace"
                  ? "Replacing…"
                  : step.mode === "append"
                    ? "Appending…"
                    : "Ingesting…"}
              </Button>
            ) : null}
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}

// ----------------------------------------------------------------------------
// Conflict view: shown when the operator tries to ingest into a table name
// that already has data. Surfaces a schema diff and the list of Virtual
// datasets bound to the existing table so the impact of a wipe-and-replace
// is visible before the operator opts in.
// ----------------------------------------------------------------------------

type SchemaDiff = {
  added: CsvColumn[];
  removed: CsvColumn[];
  changed: { name: string; from: string; to: string }[];
  identical: boolean;
};

function diffColumns(existing: CsvColumn[], next: CsvColumn[]): SchemaDiff {
  const existingByName = new Map(existing.map((c) => [c.name, c]));
  const nextByName = new Map(next.map((c) => [c.name, c]));
  const added = next.filter((c) => !existingByName.has(c.name));
  const removed = existing.filter((c) => !nextByName.has(c.name));
  const changed: { name: string; from: string; to: string }[] = [];
  for (const n of next) {
    const e = existingByName.get(n.name);
    if (e && e.postgresType !== n.postgresType) {
      changed.push({ name: n.name, from: e.postgresType, to: n.postgresType });
    }
  }
  return {
    added,
    removed,
    changed,
    identical:
      added.length === 0 &&
      removed.length === 0 &&
      changed.length === 0 &&
      existing.length === next.length
  };
}

function ConflictView({
  storeId,
  newColumns,
  conflict,
  onAppend,
  onReplace,
  onBack
}: {
  storeId: string;
  newColumns: CsvColumn[];
  conflict: CsvIngestConflict;
  onAppend: () => void;
  onReplace: () => void;
  onBack: () => void;
}) {
  const [confirmReplace, setConfirmReplace] = useState(false);
  const diff = useMemo(() => diffColumns(conflict.existingColumns, newColumns), [
    conflict.existingColumns,
    newColumns
  ]);

  // Only fetched when the conflict UI is mounted — fine to leave on the
  // /api/datasets list endpoint without a server-side filter; the SPA-side
  // filter to (sourceKind=datastore, sourceId=storeId, sourceTableName=name)
  // is cheap and the list is short for typical deployments.
  const datasetsQuery = useQuery<Dataset[]>({
    queryKey: ["datasets", "list"],
    queryFn: ({ signal }) => listDatasets(signal),
    staleTime: 30_000
  });
  const boundDatasets = useMemo(
    () =>
      (datasetsQuery.data ?? []).filter(
        (d) =>
          d.sourceKind === "datastore" &&
          d.sourceId === storeId &&
          (d.sourceTableName ?? "").toLowerCase() === conflict.sanitizedTableName.toLowerCase()
      ),
    [datasetsQuery.data, storeId, conflict.sanitizedTableName]
  );

  const canAppend = diff.identical;
  const willBreakBindings = !diff.identical && boundDatasets.length > 0;
  // Special-case the server's mid-flight "you asked to append but the
  // schema doesn't match" response — same conflict UI, but with an
  // explicit headline so the operator knows their Append was rejected
  // (rather than landing here because they tried Insert).
  const fromAppendAttempt = conflict.conflictKind === "schemaMismatch";

  return (
    <Stack gap="sm">
      <Alert
        color={canAppend ? "yellow" : "red"}
        title={
          fromAppendAttempt
            ? `Cannot append — "${conflict.sanitizedTableName}" has a different schema`
            : canAppend
              ? `Table "${conflict.sanitizedTableName}" already exists`
              : `Table "${conflict.sanitizedTableName}" exists with a different schema`
        }
      >
        <Text size="sm">
          The existing table holds{" "}
          <strong>{conflict.existingRowCount.toLocaleString()}</strong> row(s) across{" "}
          <strong>{conflict.existingColumns.length}</strong> column(s).{" "}
          {canAppend
            ? "Same schema — append adds the new rows on top, replace swaps everything."
            : "The new CSV's schema differs (see below). Append is unavailable; replace will drop the table and recreate it."}
        </Text>
      </Alert>

      {!diff.identical ? (
        <Card withBorder padding="xs">
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              Schema diff
            </Text>
            {diff.added.length > 0 ? (
              <Text size="sm">
                <Badge color="green" variant="light" mr="xs">
                  + added
                </Badge>
                {diff.added.map((c) => `${c.name} (${c.postgresType})`).join(", ")}
              </Text>
            ) : null}
            {diff.removed.length > 0 ? (
              <Text size="sm">
                <Badge color="red" variant="light" mr="xs">
                  − removed
                </Badge>
                {diff.removed.map((c) => `${c.name} (${c.postgresType})`).join(", ")}
              </Text>
            ) : null}
            {diff.changed.length > 0 ? (
              <Text size="sm">
                <Badge color="yellow" variant="light" mr="xs">
                  ~ type changed
                </Badge>
                {diff.changed.map((c) => `${c.name}: ${c.from} → ${c.to}`).join(", ")}
              </Text>
            ) : null}
          </Stack>
        </Card>
      ) : null}

      <Card withBorder padding="xs">
        <Stack gap={4}>
          <Text size="sm" fw={500}>
            Datasets bound to <code>{conflict.sanitizedTableName}</code>
          </Text>
          {datasetsQuery.isLoading ? (
            <Group gap="xs">
              <Loader size="xs" />
              <Text size="xs" c="dimmed">
                Looking up bound datasets…
              </Text>
            </Group>
          ) : boundDatasets.length === 0 ? (
            <Text size="xs" c="dimmed">
              No datasets reference this table.
            </Text>
          ) : (
            <>
              <List size="sm" spacing={2}>
                {boundDatasets.map((d) => (
                  <List.Item key={d.id}>
                    <Anchor component={Link} to="/datasets" size="sm">
                      {d.name}
                    </Anchor>{" "}
                    <Text component="span" size="xs" c="dimmed">
                      ({d.mode === 2 ? "Cached" : "Virtual"})
                    </Text>
                  </List.Item>
                ))}
              </List>
              {willBreakBindings ? (
                <Text size="xs" c="red">
                  Schema change + bound datasets → AQL queries against these datasets may fail
                  or return unexpected results until each dataset&apos;s column list is updated.
                </Text>
              ) : (
                <Text size="xs" c="dimmed">
                  Same schema, so bound datasets keep working — only their underlying data
                  changes.
                </Text>
              )}
            </>
          )}
        </Stack>
      </Card>

      <Checkbox
        checked={confirmReplace}
        onChange={(e) => setConfirmReplace(e.currentTarget.checked)}
        label={
          <Text size="sm">
            I understand <em>Replace</em> will drop the existing table{" "}
            <code>{conflict.sanitizedTableName}</code> and lose its current rows.
          </Text>
        }
      />

      <Group justify="space-between">
        <Anchor component="button" type="button" size="sm" onClick={onBack}>
          ← Back to columns
        </Anchor>
        <Text size="xs" c="dimmed">
          Or rename the table above to keep both.
        </Text>
      </Group>

      <Group justify="flex-end">
        <Tooltip
          label={
            canAppend
              ? "Add the new CSV's rows on top of the existing table"
              : "Schemas differ — append is only available when the new CSV's columns match exactly"
          }
          withArrow
          position="top"
          disabled={false}
        >
          <Button
            color="teal"
            disabled={!canAppend}
            onClick={onAppend}
            leftSection={<i className="fa fa-plus" aria-hidden />}
          >
            Append rows
          </Button>
        </Tooltip>
        <Button
          color="red"
          disabled={!confirmReplace}
          onClick={onReplace}
          leftSection={<i className="fa fa-trash" aria-hidden />}
        >
          Replace existing table
        </Button>
      </Group>
    </Stack>
  );
}

// ----------------------------------------------------------------------------
// SQL detail sub-cards: columns / preview / AQL playground
// ----------------------------------------------------------------------------

function ColumnsCard({ table }: { table: DataStoreTable }) {
  return (
    <Card withBorder padding="md">
      <Stack gap="xs">
        <Text fw={500}>Columns ({table.columns.length})</Text>
        <ScrollArea.Autosize mah={280}>
          <Table striped withTableBorder withColumnBorders>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th style={{ width: 200 }}>Postgres type</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {table.columns.map((col) => (
                <Table.Tr key={col.name}>
                  <Table.Td>
                    <code>{col.name}</code>
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color="gray">
                      {col.postgresType}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea.Autosize>
      </Stack>
    </Card>
  );
}

const PREVIEW_LIMIT = 30;

function PreviewCard({ storeId, table }: { storeId: string; table: DataStoreTable }) {
  const previewQuery = useQuery<DataStoreTablePreview>({
    queryKey: ["datastores", storeId, "tables", table.id, "preview", PREVIEW_LIMIT],
    queryFn: ({ signal }) => previewDataStoreTable(storeId, table.id, PREVIEW_LIMIT, signal),
    staleTime: 30_000
  });

  return (
    <Card withBorder padding="md">
      <Stack gap="xs">
        <Group justify="space-between" align="center">
          <Box>
            <Text fw={500}>Data preview</Text>
            <Text size="xs" c="dimmed">
              First {PREVIEW_LIMIT} rows of{" "}
              <code>{table.tableName}</code>
              {table.rowCount > 0 ? (
                <>
                  {" "}
                  · {table.rowCount.toLocaleString()} total row
                  {table.rowCount === 1 ? "" : "s"} in this table
                </>
              ) : null}
              .
            </Text>
          </Box>
          <Button
            variant="default"
            size="xs"
            onClick={() => void previewQuery.refetch()}
            leftSection={<i className="fa fa-rotate" aria-hidden />}
            disabled={previewQuery.isFetching}
          >
            Refresh
          </Button>
        </Group>
        {previewQuery.isLoading ? (
          <Group justify="center" py="md">
            <Loader size="sm" />
          </Group>
        ) : previewQuery.error ? (
          <Alert color="red">
            {describeError(previewQuery.error, "Failed to load preview.")}
          </Alert>
        ) : previewQuery.data ? (
          <SampleTable
            columns={previewQuery.data.columns.map((c) => c.name)}
            rows={previewQuery.data.rows}
            emptyMessage="No rows ingested yet — the table exists but is empty."
          />
        ) : null}
      </Stack>
    </Card>
  );
}

// Hand-rolled <Table> renderer rather than the heavier DataTable wrapper —
// the data is bounded at PREVIEW_LIMIT rows, columns are dynamic, and we
// want a tight read-only sample, not sorting/filtering/pagination.
function SampleTable({
  columns,
  rows,
  emptyMessage
}: {
  columns: string[];
  rows: Record<string, unknown>[];
  emptyMessage: string;
}) {
  if (rows.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {emptyMessage}
      </Text>
    );
  }
  return (
    <ScrollArea.Autosize mah={460}>
      <Table striped withTableBorder withColumnBorders highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            {columns.map((c) => (
              <Table.Th key={c}>{c}</Table.Th>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((row, ri) => (
            <Table.Tr key={ri}>
              {columns.map((c) => (
                <Table.Td key={c}>{formatPreviewCell(row[c])}</Table.Td>
              ))}
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </ScrollArea.Autosize>
  );
}

function formatPreviewCell(value: unknown): string {
  if (value === null || value === undefined) return "";
  if (value instanceof Date) return value.toLocaleString();
  if (typeof value === "object") {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
}

// AQL is gated on Datasets — there is no `FROM DataStoreTable(...)` entity,
// so we look for a Virtual dataset already bound to this table and default
// the editor to `FROM Dataset("<name>") TAKE 30`. If no dataset exists,
// the editor is still shown (with a templated query) so the user knows
// what the workflow looks like, plus a banner with a link to /datasets.
function AqlPlaygroundCard({
  storeId,
  table
}: {
  storeId: string;
  table: DataStoreTable;
}) {
  const datasetsQuery = useQuery<Dataset[]>({
    queryKey: ["datasets", "list"],
    queryFn: ({ signal }) => listDatasets(signal),
    staleTime: 30_000
  });

  // A dataset is "bound" to this table iff its source kind is "datastore",
  // the source id is this store, and the source table name matches. The
  // executor uses the same comparison to route Virtual queries.
  const boundDataset = useMemo<Dataset | null>(() => {
    const all = datasetsQuery.data ?? [];
    return (
      all.find(
        (d) =>
          d.sourceKind === "datastore" &&
          d.sourceId === storeId &&
          (d.sourceTableName ?? "").toLowerCase() === table.tableName.toLowerCase()
      ) ?? null
    );
  }, [datasetsQuery.data, storeId, table.tableName]);

  const defaultQuery = useMemo(() => {
    const name = boundDataset?.name ?? table.tableName;
    return `FROM Dataset("${name}") TAKE ${PREVIEW_LIMIT}`;
  }, [boundDataset, table.tableName]);

  const [queryText, setQueryText] = useState(defaultQuery);
  const [running, setRunning] = useState(false);
  const [response, setResponse] = useState<AqlQueryResponse | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);

  // Re-seed the editor whenever the picked table (or its bound dataset)
  // changes — otherwise the editor would keep the prior table's name and
  // give confusing results.
  useEffect(() => {
    setQueryText(defaultQuery);
    setResponse(null);
    setErrors(null);
  }, [defaultQuery]);

  async function runQuery() {
    setRunning(true);
    setErrors(null);
    try {
      const res = await executeQuery(queryText);
      setResponse(res);
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
  }

  const resultColumns = response?.columns.map((c) => c.name) ?? [];

  return (
    <Card withBorder padding="md">
      <Stack gap="sm">
        <Group justify="space-between" align="center">
          <Box>
            <Text fw={500}>AQL playground</Text>
            <Text size="xs" c="dimmed">
              Run AQL against this table. Ctrl/Cmd+Enter executes; results render below.
            </Text>
          </Box>
          <Tooltip
            label="Open the full Query page with this query pre-filled"
            withArrow
            position="top"
          >
            <Button
              component={Link}
              to={`/query?q=${encodeURIComponent(queryText)}`}
              variant="default"
              size="xs"
              leftSection={<i className="fa fa-up-right-from-square" aria-hidden />}
            >
              Open in Query
            </Button>
          </Tooltip>
        </Group>

        {datasetsQuery.isLoading ? (
          <Group py="xs">
            <Loader size="xs" />
            <Text size="sm" c="dimmed">
              Looking up bound dataset…
            </Text>
          </Group>
        ) : boundDataset ? (
          <Alert color="blue" variant="light">
            Queries route through dataset <code>{boundDataset.name}</code>{" "}
            (
            <Anchor component={Link} to={`/datasets`} size="sm">
              manage
            </Anchor>
            ).
          </Alert>
        ) : (
          <Alert color="yellow" variant="light" title="No dataset bound to this table">
            AQL only queries through Datasets. The query below references{" "}
            <code>Dataset(&quot;{table.tableName}&quot;)</code> as a template — running it
            will fail until you create a Virtual dataset over this table.{" "}
            <Anchor component={Link} to="/datasets" size="sm">
              Create one in the Datasets admin
            </Anchor>
            .
          </Alert>
        )}

        <AqlEditor
          value={queryText}
          onChange={setQueryText}
          onExecute={() => {
            if (!running) void runQuery();
          }}
          readOnly={running}
          minHeight="5em"
          maxHeight="14em"
        />

        <Group justify="space-between">
          <Text size="xs" c="dimmed">
            Default:{" "}
            <code>
              FROM Dataset(&quot;
              {boundDataset?.name ?? table.tableName}
              &quot;) TAKE {PREVIEW_LIMIT}
            </code>
          </Text>
          <Button
            onClick={() => void runQuery()}
            loading={running}
            leftSection={<i className="fa fa-play" aria-hidden />}
          >
            Run query
          </Button>
        </Group>

        {errors && errors.length > 0 ? (
          <Alert color="red" title="Query failed">
            <Stack gap={2}>
              {errors.map((e, i) => (
                <Text key={i} size="sm">
                  {e}
                </Text>
              ))}
            </Stack>
          </Alert>
        ) : null}

        {response ? (
          <Stack gap="xs">
            <Text size="xs" c="dimmed">
              {response.rows.length.toLocaleString()} row
              {response.rows.length === 1 ? "" : "s"} ·{" "}
              {response.durationMs.toLocaleString()} ms
              {response.truncated ? " · truncated" : ""}
            </Text>
            <SampleTable
              columns={resultColumns}
              rows={response.rows as Record<string, unknown>[]}
              emptyMessage="Query returned no rows."
            />
          </Stack>
        ) : null}
      </Stack>
    </Card>
  );
}

// ----------------------------------------------------------------------------
// Shared helpers
// ----------------------------------------------------------------------------

function describeError(err: unknown, fallback: string): string {
  const reason = (err as { response?: { data?: { reason?: string } } })?.response?.data?.reason;
  if (reason) return reason;
  if (err instanceof Error) return err.message;
  return fallback;
}
