import { FormEvent, useMemo, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Breadcrumbs,
  Button,
  Card,
  Group,
  Loader,
  Modal,
  NativeSelect,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip
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
  DataStoreFile,
  DataStoreFolder,
  DataStoreListing,
  createDataStoreFolder,
  dataStoreFileDownloadUrl,
  deleteDataStoreFile,
  deleteDataStoreFolder,
  getDataStore,
  ingestCsv,
  kindLabel,
  listDataStoreFiles,
  previewCsvIngest,
  uploadDataStoreFile
} from "@/api/datastores";

// Normalize a folder path so the server-side validator is happy and the
// breadcrumb logic has a single source of truth. Always starts with "/",
// never ends with "/" (except for the root "/" itself), and collapses
// any double slashes a hand-typed URL might contain.
function normalizeFolder(raw: string | null | undefined): string {
  const trimmed = (raw ?? "/").trim();
  if (!trimmed || trimmed === "/") return "/";
  const withLeading = trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
  const collapsed = withLeading.replace(/\/+/g, "/");
  return collapsed.length > 1 && collapsed.endsWith("/")
    ? collapsed.slice(0, -1)
    : collapsed;
}

function parentFolder(path: string): string {
  if (path === "/" || path === "") return "/";
  const idx = path.lastIndexOf("/");
  return idx <= 0 ? "/" : path.slice(0, idx);
}

function joinFolder(base: string, name: string): string {
  return normalizeFolder(base === "/" ? `/${name}` : `${base}/${name}`);
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

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

      {isFiles ? <FilesPanel storeId={storeId} /> : <SqlPanel storeId={storeId} />}
    </Stack>
  );
}

// ----------------------------------------------------------------------------
// File-type sub-panel
// ----------------------------------------------------------------------------

function FilesPanel({ storeId }: { storeId: string }) {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const folder = normalizeFolder(searchParams.get("folder"));

  function navigateTo(nextFolder: string) {
    const next = normalizeFolder(nextFolder);
    const params = new URLSearchParams(searchParams);
    if (next === "/") params.delete("folder");
    else params.set("folder", next);
    setSearchParams(params, { replace: false });
  }

  const listKey = ["datastores", "files", storeId, folder] as const;
  const listingQuery = useQuery<DataStoreListing>({
    queryKey: listKey,
    queryFn: () => listDataStoreFiles(storeId, folder)
  });

  const [newFolderOpen, setNewFolderOpen] = useState(false);
  const [uploadOpen, setUploadOpen] = useState(false);

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["datastores", "files", storeId] });
  }

  const deleteFile = useMutation({
    mutationFn: (file: DataStoreFile) => deleteDataStoreFile(storeId, file.id),
    onSuccess: () => {
      invalidate();
      notifications.show({ message: "File deleted.", color: "green" });
    },
    onError: (err: unknown) => {
      notifications.show({ message: describeError(err, "Delete failed."), color: "red" });
    }
  });

  const deleteFolder = useMutation({
    mutationFn: (sub: DataStoreFolder) => deleteDataStoreFolder(storeId, sub.folderPath),
    onSuccess: () => {
      invalidate();
      notifications.show({ message: "Folder deleted.", color: "green" });
    },
    onError: (err: unknown) => {
      notifications.show({ message: describeError(err, "Delete failed."), color: "red" });
    }
  });

  const crumbs = useMemo(() => {
    const items: { label: string; path: string }[] = [{ label: "/", path: "/" }];
    if (folder !== "/") {
      const parts = folder.split("/").filter(Boolean);
      let acc = "";
      for (const part of parts) {
        acc = acc === "" ? `/${part}` : `${acc}/${part}`;
        items.push({ label: part, path: acc });
      }
    }
    return items;
  }, [folder]);

  return (
    <Stack gap="md">
      <Card withBorder padding="sm">
        <Group justify="space-between" wrap="wrap" gap="sm">
          <Group gap="xs" wrap="wrap">
            <Text size="sm" c="dimmed">
              Folder:
            </Text>
            <Breadcrumbs separator="/" aria-label="Current folder">
              {crumbs.map((c) => (
                <Anchor
                  key={c.path}
                  component="button"
                  type="button"
                  size="sm"
                  onClick={() => navigateTo(c.path)}
                  fw={c.path === folder ? 600 : 400}
                >
                  {c.label}
                </Anchor>
              ))}
            </Breadcrumbs>
          </Group>
          <Group gap="xs">
            {folder !== "/" ? (
              <Button
                variant="default"
                size="sm"
                leftSection={<i className="fa fa-arrow-up" />}
                onClick={() => navigateTo(parentFolder(folder))}
              >
                Up
              </Button>
            ) : null}
            <Button
              variant="default"
              size="sm"
              leftSection={<i className="fa fa-folder-plus" />}
              onClick={() => setNewFolderOpen(true)}
            >
              New folder
            </Button>
            <Button
              size="sm"
              leftSection={<i className="fa fa-arrow-up-from-bracket" />}
              onClick={() => setUploadOpen(true)}
            >
              Upload file
            </Button>
          </Group>
        </Group>
      </Card>

      {listingQuery.isLoading ? (
        <Group justify="center">
          <Loader />
        </Group>
      ) : listingQuery.error ? (
        <Alert color="red">Failed to load folder contents.</Alert>
      ) : (
        <Card withBorder padding="sm">
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th style={{ width: 140 }}>Size</Table.Th>
                <Table.Th style={{ width: 220 }}>Uploaded</Table.Th>
                <Table.Th style={{ width: 120 }} aria-label="Actions" />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {listingQuery.data?.folders.length === 0 &&
              listingQuery.data?.files.length === 0 ? (
                <Table.Tr>
                  <Table.Td colSpan={4}>
                    <Text c="dimmed" ta="center" py="md">
                      This folder is empty.
                    </Text>
                  </Table.Td>
                </Table.Tr>
              ) : null}
              {listingQuery.data?.folders.map((sub) => {
                const name = sub.folderPath.split("/").filter(Boolean).pop() ?? sub.folderPath;
                return (
                  <Table.Tr key={`f-${sub.folderPath}`}>
                    <Table.Td>
                      <Anchor
                        component="button"
                        type="button"
                        onClick={() => navigateTo(sub.folderPath)}
                      >
                        <Group gap="xs">
                          <i className="fa fa-folder" />
                          <Text fw={500}>{name}</Text>
                        </Group>
                      </Anchor>
                    </Table.Td>
                    <Table.Td>
                      <Text c="dimmed">—</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text c="dimmed">—</Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="flex-end">
                        <Tooltip label="Delete folder (must be empty)">
                          <ActionIcon
                            color="red"
                            variant="subtle"
                            aria-label={`Delete folder ${name}`}
                            loading={deleteFolder.isPending}
                            onClick={() => {
                              if (window.confirm(`Delete folder "${name}"?`)) {
                                deleteFolder.mutate(sub);
                              }
                            }}
                          >
                            <i className="fa fa-trash" />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
              {listingQuery.data?.files.map((file) => (
                <Table.Tr key={`file-${file.id}`}>
                  <Table.Td>
                    <Group gap="xs">
                      <i className="fa fa-file" />
                      <Text fw={500}>{file.filename}</Text>
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatBytes(file.sizeBytes)}</Table.Td>
                  <Table.Td>{new Date(file.uploadedAtUtc).toLocaleString()}</Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end">
                      <Tooltip label="Download">
                        <ActionIcon
                          component="a"
                          href={dataStoreFileDownloadUrl(storeId, file.id)}
                          download={file.filename}
                          variant="subtle"
                          aria-label={`Download ${file.filename}`}
                        >
                          <i className="fa fa-download" />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete file">
                        <ActionIcon
                          color="red"
                          variant="subtle"
                          aria-label={`Delete ${file.filename}`}
                          loading={deleteFile.isPending}
                          onClick={() => {
                            if (window.confirm(`Delete "${file.filename}"?`)) {
                              deleteFile.mutate(file);
                            }
                          }}
                        >
                          <i className="fa fa-trash" />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      )}

      <NewFolderModal
        opened={newFolderOpen}
        onClose={() => setNewFolderOpen(false)}
        parentFolder={folder}
        storeId={storeId}
        onCreated={() => {
          setNewFolderOpen(false);
          invalidate();
        }}
      />
      <UploadFileModal
        opened={uploadOpen}
        onClose={() => setUploadOpen(false)}
        folder={folder}
        storeId={storeId}
        onUploaded={() => {
          setUploadOpen(false);
          invalidate();
        }}
      />
    </Stack>
  );
}

function NewFolderModal({
  opened,
  onClose,
  parentFolder: parent,
  storeId,
  onCreated
}: {
  opened: boolean;
  onClose: () => void;
  parentFolder: string;
  storeId: string;
  onCreated: () => void;
}) {
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: (folderPath: string) => createDataStoreFolder(storeId, folderPath),
    onSuccess: () => {
      setName("");
      setError(null);
      notifications.show({ message: "Folder created.", color: "green" });
      onCreated();
    },
    onError: (err: unknown) => setError(describeError(err, "Create failed."))
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) {
      setError("Folder name is required.");
      return;
    }
    if (trimmed.includes("/")) {
      setError("Folder name cannot contain '/'. Create nested folders one level at a time.");
      return;
    }
    setError(null);
    create.mutate(joinFolder(parent, trimmed));
  }

  return (
    <Modal opened={opened} onClose={onClose} title="New folder" centered>
      <form onSubmit={submit}>
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Created inside <code>{parent}</code>.
          </Text>
          <TextInput
            label="Folder name"
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            required
            data-autofocus
          />
          {error ? <Alert color="red">{error}</Alert> : null}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={create.isPending}>
              Create
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function UploadFileModal({
  opened,
  onClose,
  folder,
  storeId,
  onUploaded
}: {
  opened: boolean;
  onClose: () => void;
  folder: string;
  storeId: string;
  onUploaded: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  const upload = useMutation({
    mutationFn: (f: File) => uploadDataStoreFile(storeId, folder, f),
    onSuccess: () => {
      setFile(null);
      setError(null);
      notifications.show({ message: "File uploaded.", color: "green" });
      onUploaded();
    },
    onError: (err: unknown) => setError(describeError(err, "Upload failed."))
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!file) {
      setError("Choose a file first.");
      return;
    }
    upload.mutate(file);
  }

  return (
    <Modal
      opened={opened}
      onClose={() => {
        setFile(null);
        setError(null);
        onClose();
      }}
      title="Upload file"
      centered
    >
      <form onSubmit={submit}>
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Uploading into <code>{folder}</code>.
          </Text>
          <Dropzone
            onDrop={(files) => {
              setError(null);
              setFile(files[0] ?? null);
            }}
            onReject={(rejections) => {
              const first = rejections[0]?.errors?.[0];
              setError(first?.message ?? "Drop a single file.");
            }}
            multiple={false}
            maxFiles={1}
            disabled={upload.isPending}
            aria-label="File dropzone"
          >
            <Group justify="center" gap="md" mih={120} style={{ pointerEvents: "none" }}>
              <Dropzone.Accept>
                <i className="fa fa-arrow-up-from-bracket" style={{ fontSize: 32 }} />
              </Dropzone.Accept>
              <Dropzone.Reject>
                <i
                  className="fa fa-circle-xmark"
                  style={{ fontSize: 32, color: "var(--mantine-color-red-filled)" }}
                />
              </Dropzone.Reject>
              <Dropzone.Idle>
                <i
                  className="fa fa-file-arrow-up"
                  style={{ fontSize: 32, color: "var(--mantine-color-dimmed)" }}
                />
              </Dropzone.Idle>
              <div>
                <Text size="sm" fw={500}>
                  Drop a file here or click to browse
                </Text>
                <Text size="xs" c="dimmed" mt={4}>
                  One file at a time. Re-upload with the same name to replace.
                </Text>
              </div>
            </Group>
          </Dropzone>
          {file ? (
            <Text size="xs" c="dimmed">
              Selected: <strong>{file.name}</strong> ({formatBytes(file.size)})
            </Text>
          ) : null}
          {error ? <Alert color="red">{error}</Alert> : null}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!file} loading={upload.isPending}>
              Upload
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
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
