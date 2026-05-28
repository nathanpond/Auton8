import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ActionIcon,
  Anchor,
  Box,
  Breadcrumbs,
  Button,
  Card,
  Group,
  Loader,
  Menu,
  Modal,
  Stack,
  Text,
  TextInput,
  Title,
  UnstyledButton
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { DocumentDto, FolderDto, fetchFolder } from "@/api/documents";
import { useQueries } from "@tanstack/react-query";
import FolderTree from "@/components/documents/FolderTree";
import ImportDocxButton from "@/components/documents/ImportDocxButton";
import {
  useCreateDocument,
  useCreateFolder,
  useDeleteDocument,
  useDeleteFolder,
  useFolder,
  useFolderChildren,
  useProjectRootDocuments,
  useProjectRootFolders,
  useUpdateDocument,
  useUpdateFolder,
  folderKey
} from "@/hooks/useDocuments";
import { useProjects } from "@/hooks/useContent";

// /documents/p/:projectId/folder/:folderId? — single page that drives the
// folder tree sidebar, breadcrumb, and Drive-style child folder grid.
// Phase 1 = folders only; documents land on this same view once the editor
// + Document entity ship.

export default function ProjectDocumentsPage() {
  const { projectId, folderId } = useParams<{ projectId: string; folderId?: string }>();
  const navigate = useNavigate();
  const { data: projects = [] } = useProjects();
  const project = projects.find((p) => p.id === projectId) ?? null;

  const currentFolderId = folderId ?? null;
  const { data: currentFolder } = useFolder(currentFolderId);

  const goToFolder = (nextFolderId: string | null) => {
    if (!projectId) return;
    if (nextFolderId === null) {
      navigate(`/documents/p/${projectId}`);
    } else {
      navigate(`/documents/p/${projectId}/folder/${nextFolderId}`);
    }
  };

  // Folder + document mutation dialog state. All variants share the same
  // modal mount so we only ever have one dialog up at a time.
  const [dialog, setDialog] = useState<
    | { kind: "folder-create"; parent: FolderDto | null }
    | { kind: "folder-rename"; folder: FolderDto }
    | { kind: "folder-delete"; folder: FolderDto }
    | { kind: "doc-create"; parent: FolderDto | null }
    | { kind: "doc-rename"; document: DocumentDto }
    | { kind: "doc-delete"; document: DocumentDto }
    | null
  >(null);

  if (!projectId) {
    return (
      <Box p="md">
        <Text c="red">Missing projectId in the URL.</Text>
      </Box>
    );
  }

  return (
    <Box style={{ display: "flex", height: "100%", minHeight: 0 }}>
      <Box
        style={{
          width: 280,
          borderRight: "1px solid var(--mantine-color-gray-3)",
          display: "flex",
          flexDirection: "column",
          minHeight: 0
        }}
      >
        <Group justify="space-between" px="sm" py="xs">
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Text size="xs" c="dimmed">
              Project
            </Text>
            <Title order={5} style={{ wordBreak: "break-word" }}>
              {project?.name ?? "—"}
            </Title>
          </Stack>
        </Group>
        <FolderTree
          projectId={projectId}
          selectedFolderId={currentFolderId}
          onSelectFolder={goToFolder}
          onCreateChild={(parent) => setDialog({ kind: "folder-create", parent })}
          onRenameFolder={(folder) => setDialog({ kind: "folder-rename", folder })}
          onDeleteFolder={(folder) => setDialog({ kind: "folder-delete", folder })}
        />
      </Box>

      <Box style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16 }}>
        <FolderBreadcrumbs
          projectId={projectId}
          projectName={project?.name ?? "Project"}
          currentFolder={currentFolder ?? null}
        />
        {currentFolder ? (
          <Group justify="space-between" mt="sm" mb="md">
            <Title order={3}>
              <i
                className={currentFolder.icon ? currentFolder.icon : "fa fa-folder"}
                style={{ marginRight: 8, color: "var(--mantine-color-yellow-6)" }}
                aria-hidden
              />
              {currentFolder.name}
            </Title>
            <Group gap="xs">
              <Button
                size="xs"
                variant="default"
                leftSection={<i className="fa fa-folder-plus" aria-hidden />}
                onClick={() => setDialog({ kind: "folder-create", parent: currentFolder })}
              >
                New subfolder
              </Button>
              <ImportDocxButton
                projectId={projectId}
                folderId={currentFolder.id}
                accept=".docx"
              />
              <Button
                size="xs"
                leftSection={<i className="fa fa-file-circle-plus" aria-hidden />}
                onClick={() => setDialog({ kind: "doc-create", parent: currentFolder })}
              >
                New document
              </Button>
            </Group>
          </Group>
        ) : (
          <Group justify="space-between" mt="sm" mb="md">
            <Title order={3}>
              <i
                className="fa fa-folder-tree"
                style={{ marginRight: 8, color: "var(--mantine-color-blue-7)" }}
                aria-hidden
              />
              Project root
            </Title>
            <Group gap="xs">
              <Button
                size="xs"
                variant="subtle"
                component={Link}
                to="/documents/templates"
                leftSection={<i className="fa fa-copy" aria-hidden />}
              >
                Templates
              </Button>
              <Button
                size="xs"
                variant="default"
                leftSection={<i className="fa fa-folder-plus" aria-hidden />}
                onClick={() => setDialog({ kind: "folder-create", parent: null })}
              >
                New folder
              </Button>
              <ImportDocxButton
                projectId={projectId}
                folderId={null}
                accept=".docx"
              />
              <Button
                size="xs"
                leftSection={<i className="fa fa-file-circle-plus" aria-hidden />}
                onClick={() => setDialog({ kind: "doc-create", parent: null })}
              >
                New document
              </Button>
            </Group>
          </Group>
        )}

        <FolderChildGrid
          projectId={projectId}
          folderId={currentFolderId}
          onOpenFolder={goToFolder}
          onRenameDocument={(d) => setDialog({ kind: "doc-rename", document: d })}
          onDeleteDocument={(d) => setDialog({ kind: "doc-delete", document: d })}
        />
      </Box>

      <CreateFolderModal
        open={dialog?.kind === "folder-create"}
        projectId={projectId}
        parent={dialog?.kind === "folder-create" ? dialog.parent : null}
        onClose={() => setDialog(null)}
      />
      <RenameFolderModal
        open={dialog?.kind === "folder-rename"}
        folder={dialog?.kind === "folder-rename" ? dialog.folder : null}
        onClose={() => setDialog(null)}
      />
      <DeleteFolderModal
        open={dialog?.kind === "folder-delete"}
        folder={dialog?.kind === "folder-delete" ? dialog.folder : null}
        onClose={() => setDialog(null)}
        onDeleted={(deleted) => {
          // If we deleted the folder we were viewing, navigate to its parent
          // (or project root if it was a root folder).
          if (deleted.id === currentFolderId) {
            goToFolder(deleted.parentFolderId ?? null);
          }
        }}
      />
      <CreateDocumentModal
        open={dialog?.kind === "doc-create"}
        projectId={projectId}
        parent={dialog?.kind === "doc-create" ? dialog.parent : null}
        onClose={() => setDialog(null)}
      />
      <RenameDocumentModal
        open={dialog?.kind === "doc-rename"}
        document={dialog?.kind === "doc-rename" ? dialog.document : null}
        onClose={() => setDialog(null)}
      />
      <DeleteDocumentModal
        open={dialog?.kind === "doc-delete"}
        document={dialog?.kind === "doc-delete" ? dialog.document : null}
        onClose={() => setDialog(null)}
      />
    </Box>
  );
}

// Walks parent_folder_id back to the project root, fetching ancestor folder
// rows in parallel via `useQueries`. Renders as Mantine Breadcrumbs.
function FolderBreadcrumbs({
  projectId,
  projectName,
  currentFolder
}: {
  projectId: string;
  projectName: string;
  currentFolder: FolderDto | null;
}) {
  const ancestorIds = useMemo(() => {
    // The current folder includes itself; we render it as the last (non-link)
    // breadcrumb separately. Phase 1 fetches ancestors one-at-a-time per id
    // — folder trees deeper than ~5 levels should add a server-side
    // `/api/content/folders/{id}/path` endpoint later to collapse to one
    // round-trip; not worth the extra endpoint yet.
    if (!currentFolder) return [];
    const ids: string[] = [];
    let parentId = currentFolder.parentFolderId;
    while (parentId) {
      ids.push(parentId);
      // We can't resolve grandparents without fetching their rows, so we
      // stop here and the useQueries below fans out to fetch them. Once a
      // row arrives we'll see *its* parentFolderId and the user can navigate
      // up via the tree. (A future server-side path endpoint will fix this.)
      parentId = null;
    }
    return ids.reverse();
  }, [currentFolder]);

  const ancestorQueries = useQueries({
    queries: ancestorIds.map((id) => ({
      queryKey: folderKey(id),
      queryFn: ({ signal }: { signal?: AbortSignal }) => fetchFolder(id, signal)
    }))
  });

  const items: { label: string; href: string | null }[] = [
    { label: projectName, href: `/documents/p/${projectId}` },
    ...ancestorQueries
      .filter((q) => q.data)
      .map((q) => ({
        label: q.data!.name,
        href: `/documents/p/${projectId}/folder/${q.data!.id}`
      })),
    ...(currentFolder ? [{ label: currentFolder.name, href: null }] : [])
  ];

  return (
    <Breadcrumbs separator="/">
      {items.map((item, i) =>
        item.href ? (
          <Anchor component={Link} to={item.href} key={`${item.label}-${i}`} size="sm">
            {item.label}
          </Anchor>
        ) : (
          <Text size="sm" fw={600} key={`${item.label}-${i}`}>
            {item.label}
          </Text>
        )
      )}
    </Breadcrumbs>
  );
}

// Drive-style grid of the current folder's direct children — sub-folders
// AND documents. Sub-folders render as folder cards that navigate; documents
// render as cards that open in the editor route. At the project root, root
// folders + root documents are fetched separately (the folder /children
// endpoint requires a folder id); inside a folder, both arrays come from the
// single /children response so we never two-step the wire.
function FolderChildGrid({
  projectId,
  folderId,
  onOpenFolder,
  onRenameDocument,
  onDeleteDocument
}: {
  projectId: string;
  folderId: string | null;
  onOpenFolder: (id: string) => void;
  onRenameDocument: (d: DocumentDto) => void;
  onDeleteDocument: (d: DocumentDto) => void;
}) {
  const rootFoldersQuery = useProjectRootFolders(folderId === null ? projectId : null);
  const rootDocumentsQuery = useProjectRootDocuments(folderId === null ? projectId : null);
  const childQuery = useFolderChildren(folderId);

  const isLoading = folderId === null
    ? rootFoldersQuery.isLoading || rootDocumentsQuery.isLoading
    : childQuery.isLoading;

  const folders = folderId === null
    ? (rootFoldersQuery.data ?? [])
    : (childQuery.data?.folders ?? []);
  const documents = folderId === null
    ? (rootDocumentsQuery.data ?? [])
    : (childQuery.data?.documents ?? []);

  if (isLoading) {
    return (
      <Group justify="center" mt="md">
        <Loader />
      </Group>
    );
  }

  if (folders.length === 0 && documents.length === 0) {
    return (
      <Card withBorder padding="lg" mt="md">
        <Text c="dimmed" ta="center">
          This folder is empty. Use "New subfolder" or "New document" to add something.
        </Text>
      </Card>
    );
  }

  return (
    <Box
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fill, minmax(220px, 1fr))",
        gap: 12,
        marginTop: 8
      }}
    >
      {folders.map((folder) => (
        <UnstyledButton
          key={folder.id}
          onClick={() => onOpenFolder(folder.id)}
          style={{ height: "100%" }}
        >
          <Card withBorder padding="md" style={{ height: "100%" }}>
            <Group gap={10}>
              <i
                className={folder.icon ? folder.icon : "fa fa-folder"}
                style={{ fontSize: 24, color: "var(--mantine-color-yellow-6)" }}
                aria-hidden
              />
              <Stack gap={2} style={{ minWidth: 0 }}>
                <Text fw={600} truncate>
                  {folder.name}
                </Text>
                {folder.description ? (
                  <Text size="xs" c="dimmed" truncate>
                    {folder.description}
                  </Text>
                ) : null}
              </Stack>
            </Group>
          </Card>
        </UnstyledButton>
      ))}
      {documents.map((doc) => (
        <DocumentCard
          key={doc.id}
          doc={doc}
          onRename={() => onRenameDocument(doc)}
          onDelete={() => onDeleteDocument(doc)}
        />
      ))}
    </Box>
  );
}

// ── Mutation modals ────────────────────────────────────────────────────────

function CreateFolderModal({
  open,
  projectId,
  parent,
  onClose
}: {
  open: boolean;
  projectId: string;
  parent: FolderDto | null;
  onClose: () => void;
}) {
  const [name, setName] = useState("");
  const createFolder = useCreateFolder();
  const submit = async () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    try {
      await createFolder.mutateAsync({
        projectId,
        parentFolderId: parent?.id ?? null,
        name: trimmed
      });
      notifications.show({ message: `Folder "${trimmed}" created.`, color: "green" });
      setName("");
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to create folder.",
        color: "red"
      });
    }
  };
  return (
    <Modal
      opened={open}
      onClose={() => {
        setName("");
        onClose();
      }}
      title={parent ? `New folder in "${parent.name}"` : "New folder at project root"}
    >
      <Stack>
        <TextInput
          label="Name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!name.trim()}
            loading={createFolder.isPending}
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function RenameFolderModal({
  open,
  folder,
  onClose
}: {
  open: boolean;
  folder: FolderDto | null;
  onClose: () => void;
}) {
  const [name, setName] = useState(folder?.name ?? "");
  // Reset the text field when the modal opens with a different folder so the
  // user always sees the current name (the modal mount persists across opens).
  useEffect(() => {
    if (folder) setName(folder.name);
  }, [folder]);
  const updateFolder = useUpdateFolder();
  if (!folder) return null;
  const submit = async () => {
    const trimmed = name.trim();
    if (!trimmed || trimmed === folder.name) {
      onClose();
      return;
    }
    try {
      await updateFolder.mutateAsync({
        id: folder.id,
        previousProjectId: folder.projectId,
        previousParentFolderId: folder.parentFolderId,
        patch: { name: trimmed }
      });
      notifications.show({ message: `Folder renamed to "${trimmed}".`, color: "green" });
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to rename folder.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title={`Rename "${folder.name}"`}>
      <Stack>
        <TextInput
          label="Name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!name.trim() || name.trim() === folder.name}
            loading={updateFolder.isPending}
          >
            Rename
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function DeleteFolderModal({
  open,
  folder,
  onClose,
  onDeleted
}: {
  open: boolean;
  folder: FolderDto | null;
  onClose: () => void;
  onDeleted: (deleted: FolderDto) => void;
}) {
  const deleteFolder = useDeleteFolder();
  if (!folder) return null;
  const submit = async () => {
    try {
      await deleteFolder.mutateAsync({
        id: folder.id,
        projectId: folder.projectId,
        parentFolderId: folder.parentFolderId
      });
      notifications.show({
        message: `Folder "${folder.name}" deleted.`,
        color: "green"
      });
      onDeleted(folder);
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to delete folder.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title="Delete folder">
      <Stack>
        <Text>
          Delete <strong>{folder.name}</strong>? This will also delete every nested folder
          inside it. This action cannot be undone.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button color="red" onClick={submit} loading={deleteFolder.isPending}>
            Delete
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// Renders a single document card in the folder grid. Click navigates to
// the distraction-free editor; the kebab menu surfaces rename + delete.
// Per the plan, the editor opens in a new tab so the AppShell stays put.
function DocumentCard({
  doc,
  onRename,
  onDelete
}: {
  doc: DocumentDto;
  onRename: () => void;
  onDelete: () => void;
}) {
  return (
    <Card withBorder padding="md" style={{ height: "100%" }}>
      <Group gap={10} wrap="nowrap" align="flex-start">
        <i
          className="fa fa-file-lines"
          style={{ fontSize: 24, color: "var(--mantine-color-blue-7)" }}
          aria-hidden
        />
        <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
          <Anchor
            component={Link}
            to={`/documents/edit/${doc.id}`}
            target="_blank"
            rel="noopener"
            fw={600}
          >
            {doc.title}
          </Anchor>
          {doc.description ? (
            <Text size="xs" c="dimmed" truncate>
              {doc.description}
            </Text>
          ) : null}
          <Text size="xs" c="dimmed">
            v{doc.currentVersionNumber - 1} · {new Date(doc.updatedAtUtc).toLocaleString()}
          </Text>
        </Stack>
        <Menu position="bottom-end" shadow="sm">
          <Menu.Target>
            <ActionIcon size="sm" variant="subtle" aria-label="Document menu">
              <i className="fa fa-ellipsis-vertical" aria-hidden />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item
              leftSection={<i className="fa fa-pen-to-square" aria-hidden />}
              onClick={onRename}
            >
              Rename
            </Menu.Item>
            <Menu.Divider />
            <Menu.Item
              color="red"
              leftSection={<i className="fa fa-trash" aria-hidden />}
              onClick={onDelete}
            >
              Delete document
            </Menu.Item>
          </Menu.Dropdown>
        </Menu>
      </Group>
    </Card>
  );
}

function CreateDocumentModal({
  open,
  projectId,
  parent,
  onClose
}: {
  open: boolean;
  projectId: string;
  parent: FolderDto | null;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const [title, setTitle] = useState("");
  const createDocument = useCreateDocument();
  const submit = async () => {
    const trimmed = title.trim();
    if (!trimmed) return;
    try {
      const created = await createDocument.mutateAsync({
        projectId,
        folderId: parent?.id ?? null,
        title: trimmed
      });
      notifications.show({ message: `Document "${trimmed}" created.`, color: "green" });
      setTitle("");
      onClose();
      // Open the new doc in a new tab so the user sees the editor while the
      // current folder view stays put — matches the same-tab semantics of
      // clicking a document card.
      window.open(`/documents/edit/${created.id}`, "_blank", "noopener");
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to create document.",
        color: "red"
      });
    }
  };
  return (
    <Modal
      opened={open}
      onClose={() => {
        setTitle("");
        onClose();
      }}
      title={parent ? `New document in "${parent.name}"` : "New document at project root"}
    >
      <Stack>
        <TextInput
          label="Title"
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!title.trim()}
            loading={createDocument.isPending}
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function RenameDocumentModal({
  open,
  document,
  onClose
}: {
  open: boolean;
  document: DocumentDto | null;
  onClose: () => void;
}) {
  const [title, setTitle] = useState(document?.title ?? "");
  useEffect(() => {
    if (document) setTitle(document.title);
  }, [document]);
  const updateDocument = useUpdateDocument();
  if (!document) return null;
  const submit = async () => {
    const trimmed = title.trim();
    if (!trimmed || trimmed === document.title) {
      onClose();
      return;
    }
    try {
      await updateDocument.mutateAsync({
        id: document.id,
        previousProjectId: document.projectId,
        previousFolderId: document.folderId,
        patch: { title: trimmed }
      });
      notifications.show({ message: `Document renamed to "${trimmed}".`, color: "green" });
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to rename document.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title={`Rename "${document.title}"`}>
      <Stack>
        <TextInput
          label="Title"
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!title.trim() || title.trim() === document.title}
            loading={updateDocument.isPending}
          >
            Rename
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function DeleteDocumentModal({
  open,
  document,
  onClose
}: {
  open: boolean;
  document: DocumentDto | null;
  onClose: () => void;
}) {
  const deleteDocument = useDeleteDocument();
  if (!document) return null;
  const submit = async () => {
    try {
      await deleteDocument.mutateAsync({
        id: document.id,
        projectId: document.projectId,
        folderId: document.folderId,
        kind: document.kind
      });
      notifications.show({
        message: `Document "${document.title}" deleted.`,
        color: "green"
      });
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to delete document.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title="Delete document">
      <Stack>
        <Text>
          Delete <strong>{document.title}</strong>? Its version history will also be
          removed. This action cannot be undone.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button color="red" onClick={submit} loading={deleteDocument.isPending}>
            Delete
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function extractErrorMessage(err: unknown): string | null {
  if (
    typeof err === "object" &&
    err &&
    "response" in err &&
    (err as { response?: { data?: { error?: string } } }).response?.data?.error
  ) {
    return (err as { response: { data: { error: string } } }).response.data.error;
  }
  if (typeof err === "object" && err && "message" in err) {
    return String((err as { message: unknown }).message);
  }
  return null;
}
