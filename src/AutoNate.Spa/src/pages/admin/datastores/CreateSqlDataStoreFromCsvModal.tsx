import { toast } from "@/components/notifications/toast";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Button,
  Card,
  Group,
  Loader,
  Modal,
  NativeSelect,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput
} from "@mantine/core";
import {
  type CsvColumn,
  createDataStore,
  downloadDataStoreFileAsFile,
  ingestCsv,
  previewCsvIngest
} from "@/api/datastores";

// End-to-end CSV → SQL DataStore creation, single submit. On open we pull the
// CSV bytes from the source File datastore and run preview through it (the
// preview endpoint just parses headers + samples — it doesn't touch the
// datastore — so reusing the source's auth context avoids needing a target
// store to exist first). The form is fully editable from there: store name,
// description, table name, and per-column name/type. Submit then provisions
// the new SqlType store and ingests with whatever the operator finalized.
//
// Postgres-type list mirrors the SqlPanel re-ingest editor for consistency.
// Backend's EnsureSafePostgresType narrows unknowns to text on the way into
// CREATE TABLE; values not in the menu but already present in a preview row
// (e.g. "double precision") are preserved by prepending them so the operator
// can keep an inferred type without retyping it.
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

type Phase =
  | { kind: "loading" }
  | { kind: "ready" }
  | { kind: "creating" }
  | { kind: "ingesting" };

const BUSY_LABEL: Partial<Record<Phase["kind"], string>> = {
  loading: "Reading CSV…",
  creating: "Creating SQL data store…",
  ingesting: "Ingesting rows…"
};

function describeError(err: unknown, fallback: string): string {
  const reason = (err as { response?: { data?: { reason?: string } } })?.response?.data
    ?.reason;
  if (reason) return reason;
  if (err instanceof Error) return err.message;
  return fallback;
}

export default function CreateSqlDataStoreFromCsvModal({
  opened,
  onClose,
  sourceStoreId,
  fileId,
  filename
}: {
  opened: boolean;
  onClose: () => void;
  sourceStoreId: string;
  fileId: string;
  filename: string;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const defaultName = filename.replace(/\.csv$/i, "") || filename;
  const [name, setName] = useState(defaultName);
  const [description, setDescription] = useState("");
  const [tableName, setTableName] = useState("");
  const [columns, setColumns] = useState<CsvColumn[]>([]);
  const [sampleRowCount, setSampleRowCount] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [loadError, setLoadError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Stays set even after phase returns to ready on failure so the error alert
  // can warn the operator that an empty datastore was left behind to clean up.
  const [createdStoreId, setCreatedStoreId] = useState<string | null>(null);

  // Reset + load preview whenever the modal re-opens. Same component instance
  // may be re-targeted at a different CSV without unmounting, and the
  // expensive download/preview happens here rather than in submit() so the
  // operator can adjust the schema before any state is mutated server-side.
  useEffect(() => {
    if (!opened) return;
    let cancelled = false;
    setName(filename.replace(/\.csv$/i, "") || filename);
    setDescription("");
    setTableName("");
    setColumns([]);
    setSampleRowCount(0);
    setFile(null);
    setLoadError(null);
    setSubmitError(null);
    setCreatedStoreId(null);
    setPhase({ kind: "loading" });
    (async () => {
      try {
        const csvFile = await downloadDataStoreFileAsFile(
          sourceStoreId, fileId, filename
        );
        if (cancelled) return;
        // Preview against the source datastore — the endpoint requires Edit
        // on _some_ store but doesn't actually use its identity (it just
        // parses the upload). Reusing the source's id keeps us in a
        // single-submit flow without provisioning a target up-front.
        const preview = await previewCsvIngest(sourceStoreId, csvFile);
        if (cancelled) return;
        setFile(csvFile);
        setTableName(preview.suggestedTableName);
        setColumns(preview.columns);
        setSampleRowCount(preview.sampleRowCount);
        setPhase({ kind: "ready" });
      } catch (err) {
        if (cancelled) return;
        setLoadError(describeError(err, "Failed to read CSV."));
        setPhase({ kind: "ready" });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [opened, sourceStoreId, fileId, filename]);

  const busy = phase.kind === "creating" || phase.kind === "ingesting";
  const loading = phase.kind === "loading";
  const canSubmit =
    !loading &&
    !busy &&
    !!file &&
    name.trim().length > 0 &&
    tableName.trim().length > 0 &&
    columns.length > 0;

  function updateColumn(index: number, patch: Partial<CsvColumn>) {
    setColumns((prev) =>
      prev.map((c, i) => (i === index ? { ...c, ...patch } : c))
    );
  }

  async function submit() {
    if (!file) return;
    if (!name.trim()) {
      setSubmitError("Data store name is required.");
      return;
    }
    if (!tableName.trim()) {
      setSubmitError("Table name is required.");
      return;
    }
    if (columns.length === 0) {
      setSubmitError("At least one column is required.");
      return;
    }
    setSubmitError(null);
    setCreatedStoreId(null);
    try {
      setPhase({ kind: "creating" });
      const newStore = await createDataStore({
        name: name.trim(),
        description: description.trim() || null,
        kind: "SqlType"
      });
      setCreatedStoreId(newStore.id);

      setPhase({ kind: "ingesting" });
      const result = await ingestCsv(
        newStore.id, tableName.trim(), columns, file, "insert"
      );

      await queryClient.invalidateQueries({ queryKey: ["datastores", "list"] });
      toast.success(`Created "${newStore.name}" with ${result.rowsInserted.toLocaleString()} row(s) in ${result.tableName}.`);
      onClose();
      navigate(`/datastores/${newStore.id}`);
    } catch (err) {
      setSubmitError(describeError(err, "Failed to create SQL data store."));
      setPhase({ kind: "ready" });
    }
  }

  function handleClose() {
    if (busy) return;
    onClose();
  }

  return (
    <Modal
      opened={opened}
      onClose={handleClose}
      title={`Create SQL data store from ${filename}`}
      centered
      size="lg"
    >
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          Creates a new <strong>SQL</strong>-type data store and ingests{" "}
          <code>{filename}</code> into a single table. Column types are inferred
          from the first 200 rows — edit anything below before submitting.
        </Text>

        <TextInput
          label="Data store name"
          required
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          disabled={busy}
          data-autofocus
        />
        <TextInput
          label="Description"
          placeholder="Optional"
          value={description}
          onChange={(e) => setDescription(e.currentTarget.value)}
          disabled={busy}
        />

        {loading ? (
          <Group gap="xs" py="md" justify="center">
            <Loader size="sm" />
            <Text size="sm">{BUSY_LABEL.loading}</Text>
          </Group>
        ) : null}

        {loadError ? (
          <Alert color="red" title="Couldn't read the CSV">
            <Text size="sm">{loadError}</Text>
          </Alert>
        ) : null}

        {!loading && !loadError ? (
          <>
            <TextInput
              label="Table name"
              required
              value={tableName}
              onChange={(e) => setTableName(e.currentTarget.value)}
              description="Lowercase / snake_case recommended. Lands under this datastore's per-store schema in autonate_datastores."
              disabled={busy}
            />
            <Stack gap={4}>
              <Text size="sm" fw={500}>
                Columns ({columns.length})
                {sampleRowCount ? (
                  <Text component="span" size="xs" c="dimmed" ml="xs">
                    inferred from {sampleRowCount} sample row(s)
                  </Text>
                ) : null}
              </Text>
              <Card withBorder padding="xs">
                <ScrollArea.Autosize mah={320}>
                  <Table>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Column name</Table.Th>
                        <Table.Th style={{ width: 200 }}>Postgres type</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {columns.map((col, i) => (
                        <Table.Tr key={i}>
                          <Table.Td>
                            <TextInput
                              value={col.name}
                              onChange={(e) =>
                                updateColumn(i, { name: e.currentTarget.value })
                              }
                              disabled={busy}
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
                                updateColumn(i, {
                                  postgresType: e.currentTarget.value
                                })
                              }
                              disabled={busy}
                              aria-label={`Column ${i + 1} type`}
                            />
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea.Autosize>
              </Card>
            </Stack>
          </>
        ) : null}

        {busy ? (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="sm">{BUSY_LABEL[phase.kind]}</Text>
          </Group>
        ) : null}

        {submitError ? (
          <Alert color="red" title="Couldn't finish the import">
            <Text size="sm">{submitError}</Text>
            {createdStoreId ? (
              <Text size="xs" mt="xs">
                A new data store was created before the failure. Open it from the
                Data Stores admin to retry the ingest, or delete it if you
                don&apos;t need it.
              </Text>
            ) : null}
          </Alert>
        ) : null}

        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={handleClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} loading={busy} disabled={!canSubmit}>
            Create &amp; ingest
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
