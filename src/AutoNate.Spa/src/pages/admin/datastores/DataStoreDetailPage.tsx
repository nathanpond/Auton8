import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Box,
  Button,
  Card,
  Group,
  Loader,
  Modal,
  NativeSelect,
  Stack,
  Table,
  Text,
  TextInput
} from "@mantine/core";
import { Dropzone } from "@mantine/dropzone";
import { notifications } from "@mantine/notifications";
import PageHeader from "@/components/PageHeader";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import {
  CsvColumn,
  CsvIngestPreview,
  CsvIngestResult,
  DataStore,
  getDataStore,
  ingestCsv,
  kindLabel,
  previewCsvIngest
} from "@/api/datastores";
import DataStoreFileManager from "./DataStoreFileManager";

export default function DataStoreDetailPage() {
  const { id } = useParams<{ id: string }>();
  const storeId = id ?? "";

  const storeQuery = useQuery<DataStore>({
    queryKey: ["datastores", "detail", storeId],
    queryFn: () => getDataStore(storeId),
    enabled: !!storeId
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
  | { kind: "ingesting"; file: File; tableName: string; columns: CsvColumn[] }
  | { kind: "done"; result: CsvIngestResult };

function SqlPanel({ storeId }: { storeId: string }) {
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState<IngestStep>({ kind: "idle" });
  const [error, setError] = useState<string | null>(null);

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

  async function onConfirmIngest() {
    if (step.kind !== "ready") return;
    setError(null);
    setStep({
      kind: "ingesting",
      file: step.file,
      tableName: step.tableName,
      columns: step.columns
    });
    try {
      const result = await ingestCsv(storeId, step.tableName, step.columns, step.file);
      setStep({ kind: "done", result });
      notifications.show({
        message: `Ingested ${result.rowsInserted} row(s) into ${result.tableName}.`,
        color: "green"
      });
    } catch (err) {
      setError(describeError(err, "Ingest failed."));
      setStep({
        kind: "ready",
        file: step.file,
        tableName: step.tableName,
        columns: step.columns,
        sampleRowCount: 0
      });
    }
  }

  function updateColumn(index: number, patch: Partial<CsvColumn>) {
    if (step.kind !== "ready") return;
    setStep({
      ...step,
      columns: step.columns.map((c, i) => (i === index ? { ...c, ...patch } : c))
    });
  }

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group justify="space-between" align="center">
          <Box>
            <Text fw={500}>CSV ingest</Text>
            <Text size="sm" c="dimmed">
              Drop a CSV; AutoNate infers a Postgres column schema from a sample of rows,
              you confirm the table name and types, and the rows land in this datastore's
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

          {step.kind === "done" ? (
            <Alert color="green" title="Ingest complete">
              Created table <code>{step.result.schemaName}.{step.result.tableName}</code> and
              inserted <strong>{step.result.rowsInserted}</strong> row(s). Query it from the AQL
              playground with{" "}
              <code>FROM Dataset(&quot;…&quot;)</code> after defining a Dataset over this table.
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
                Ingesting…
              </Button>
            ) : null}
          </Group>
        </Stack>
      </Modal>
    </Stack>
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
