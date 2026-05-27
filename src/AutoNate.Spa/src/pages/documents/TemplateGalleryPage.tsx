import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  ActionIcon,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Group,
  Loader,
  Menu,
  Modal,
  Select,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import PageHeader from "@/components/PageHeader";
import { DocumentDto } from "@/api/documents";
import { useProjects } from "@/hooks/useContent";
import {
  useCloneFromTemplate,
  useCreateDocument,
  useDeleteDocument,
  useTemplates,
  useUpdateDocument
} from "@/hooks/useDocuments";

// /documents/templates — cross-project template gallery. Templates live in
// the documents table (kind='template'), are NOT shown in regular folder
// grids (the /children + /page endpoints filter them out unless kind=template
// is explicitly requested), and are cloned into ordinary documents via the
// dedicated /from-template/{id} endpoint. v1 deliberately keeps the
// destination picker simple: the new doc lands at the destination project's
// root and the user can move it via the folder grid afterward.

export default function TemplateGalleryPage() {
  const navigate = useNavigate();
  const templatesQuery = useTemplates();
  const { data: projects = [] } = useProjects();

  // All dialog variants share one modal mount.
  const [dialog, setDialog] = useState<
    | { kind: "create" }
    | { kind: "use"; template: DocumentDto }
    | { kind: "rename"; template: DocumentDto }
    | { kind: "delete"; template: DocumentDto }
    | null
  >(null);

  // Build a project-id → name lookup once per render. Templates are cross-
  // project so each card shows which project owns it; without this map
  // we'd be calling find() per card.
  const projectNameById = useMemo(() => {
    const m = new Map<string, string>();
    for (const p of projects) m.set(p.id, p.name);
    return m;
  }, [projects]);

  const templates = templatesQuery.data ?? [];

  return (
    <Box p="md">
      <PageHeader
        title="Template Gallery"
        description="Reusable document templates. Use a template to spin up a new document with the same body and live data bindings; bindings start unresolved and refresh against the destination project's data."
        actions={
          <Button
            leftSection={<i className="fa fa-file-circle-plus" aria-hidden />}
            onClick={() => setDialog({ kind: "create" })}
          >
            New template
          </Button>
        }
      />

      {templatesQuery.isLoading ? (
        <Group justify="center" mt="lg">
          <Loader />
        </Group>
      ) : templates.length === 0 ? (
        <Card withBorder padding="lg" mt="md">
          <Stack gap={6} align="center">
            <Text c="dimmed">No templates yet.</Text>
            <Text c="dimmed" size="sm">
              Create one with "New template" — its body and bindings become the
              starting point for documents cloned from it.
            </Text>
          </Stack>
        </Card>
      ) : (
        <Box
          mt="md"
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
            gap: 12
          }}
        >
          {templates.map((tpl) => (
            <TemplateCard
              key={tpl.id}
              template={tpl}
              projectName={projectNameById.get(tpl.projectId) ?? "—"}
              onUse={() => setDialog({ kind: "use", template: tpl })}
              onEdit={() => navigate(`/documents/edit/${tpl.id}`)}
              onRename={() => setDialog({ kind: "rename", template: tpl })}
              onDelete={() => setDialog({ kind: "delete", template: tpl })}
            />
          ))}
        </Box>
      )}

      <CreateTemplateModal
        open={dialog?.kind === "create"}
        projects={projects}
        onClose={() => setDialog(null)}
      />
      <UseTemplateModal
        open={dialog?.kind === "use"}
        template={dialog?.kind === "use" ? dialog.template : null}
        projects={projects}
        onClose={() => setDialog(null)}
      />
      <RenameTemplateModal
        open={dialog?.kind === "rename"}
        template={dialog?.kind === "rename" ? dialog.template : null}
        onClose={() => setDialog(null)}
      />
      <DeleteTemplateModal
        open={dialog?.kind === "delete"}
        template={dialog?.kind === "delete" ? dialog.template : null}
        onClose={() => setDialog(null)}
      />
    </Box>
  );
}

function TemplateCard({
  template,
  projectName,
  onUse,
  onEdit,
  onRename,
  onDelete
}: {
  template: DocumentDto;
  projectName: string;
  onUse: () => void;
  onEdit: () => void;
  onRename: () => void;
  onDelete: () => void;
}) {
  return (
    <Card withBorder padding="md" style={{ height: "100%" }}>
      <Stack gap={8} style={{ height: "100%" }}>
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
            <Group gap={6} wrap="nowrap">
              <i
                className="fa fa-file-lines"
                style={{ color: "var(--mantine-color-blue-6)" }}
                aria-hidden
              />
              <Text fw={600} truncate>
                {template.title}
              </Text>
            </Group>
            <Badge size="xs" variant="light" color="grape" w="fit-content">
              Template
            </Badge>
          </Stack>
          <Menu shadow="md" position="bottom-end">
            <Menu.Target>
              <ActionIcon variant="subtle" aria-label="Template actions">
                <i className="fa fa-ellipsis-vertical" aria-hidden />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item
                leftSection={<i className="fa fa-pen" aria-hidden />}
                onClick={onEdit}
              >
                Edit template
              </Menu.Item>
              <Menu.Item
                leftSection={<i className="fa fa-i-cursor" aria-hidden />}
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
                Delete
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>

        {template.description ? (
          <Text size="sm" c="dimmed" lineClamp={2}>
            {template.description}
          </Text>
        ) : (
          <Text size="sm" c="dimmed" fs="italic">
            No description.
          </Text>
        )}

        <Group gap={4} mt="auto">
          <Text size="xs" c="dimmed">
            Project:
          </Text>
          <Tooltip label="Open this project's documents" withArrow>
            <Anchor
              component={Link}
              to={`/documents/p/${template.projectId}`}
              size="xs"
              truncate
              style={{ maxWidth: 140 }}
            >
              {projectName}
            </Anchor>
          </Tooltip>
        </Group>

        <Button
          fullWidth
          variant="light"
          leftSection={<i className="fa fa-copy" aria-hidden />}
          onClick={onUse}
        >
          Use template
        </Button>
      </Stack>
    </Card>
  );
}

// ── Modals ────────────────────────────────────────────────────────────────

function CreateTemplateModal({
  open,
  projects,
  onClose
}: {
  open: boolean;
  projects: { id: string; name: string }[];
  onClose: () => void;
}) {
  const createDocument = useCreateDocument();
  const [title, setTitle] = useState("");
  const [projectId, setProjectId] = useState<string | null>(null);

  // Default to the first project the user has, so the common case
  // (single-project users) is zero clicks.
  useEffect(() => {
    if (open && projectId == null && projects.length > 0) {
      setProjectId(projects[0].id);
    }
  }, [open, projectId, projects]);

  const projectOptions = useMemo(
    () => projects.map((p) => ({ value: p.id, label: p.name })),
    [projects]
  );

  const close = () => {
    setTitle("");
    onClose();
  };

  const submit = async () => {
    const trimmed = title.trim();
    if (!trimmed || !projectId) return;
    try {
      const created = await createDocument.mutateAsync({
        projectId,
        kind: "template",
        title: trimmed
      });
      notifications.show({
        message: `Template "${trimmed}" created.`,
        color: "green"
      });
      close();
      // Open the new template in the editor so the user can immediately
      // fill in the body + add bindings.
      window.open(`/documents/edit/${created.id}`, "_blank", "noopener");
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to create template.",
        color: "red"
      });
    }
  };

  return (
    <Modal opened={open} onClose={close} title="New template">
      <Stack>
        <Select
          label="Project"
          placeholder="Pick a project"
          data={projectOptions}
          value={projectId}
          onChange={setProjectId}
          searchable
          required
        />
        <TextInput
          label="Template title"
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
          required
        />
        <Text size="xs" c="dimmed">
          Templates do not appear in regular folder views. They are only
          visible from this gallery and from the "Use template" flow.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={close}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!title.trim() || !projectId}
            loading={createDocument.isPending}
          >
            Create
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function UseTemplateModal({
  open,
  template,
  projects,
  onClose
}: {
  open: boolean;
  template: DocumentDto | null;
  projects: { id: string; name: string }[];
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const clone = useCloneFromTemplate();
  const [title, setTitle] = useState("");
  const [projectId, setProjectId] = useState<string | null>(null);

  // Pre-fill: destination project = template's project; suggested title
  // adds a " (copy)" suffix the user can edit. Both reset every time the
  // modal opens against a different template.
  useEffect(() => {
    if (open && template) {
      setTitle(`${template.title} (copy)`);
      setProjectId(template.projectId);
    }
  }, [open, template]);

  const projectOptions = useMemo(
    () => projects.map((p) => ({ value: p.id, label: p.name })),
    [projects]
  );

  const close = () => {
    setTitle("");
    onClose();
  };

  const submit = async () => {
    if (!template || !projectId) return;
    const trimmed = title.trim();
    if (!trimmed) return;
    try {
      const created = await clone.mutateAsync({
        templateId: template.id,
        projectId,
        folderId: null,
        title: trimmed
      });
      notifications.show({
        message: `Document "${trimmed}" created from template.`,
        color: "green"
      });
      close();
      // Land the user in the new document so they can immediately review +
      // refresh bindings against the destination project's data.
      navigate(`/documents/edit/${created.id}`);
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to clone template.",
        color: "red"
      });
    }
  };

  if (!template) return null;

  return (
    <Modal opened={open} onClose={close} title={`Use template "${template.title}"`}>
      <Stack>
        <Select
          label="Destination project"
          data={projectOptions}
          value={projectId}
          onChange={setProjectId}
          searchable
          required
        />
        <TextInput
          label="New document title"
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
          required
        />
        <Text size="xs" c="dimmed">
          The new document lands at the destination project's root. Live data
          bindings copy over with no resolved values — refresh from the
          bindings panel after the document opens.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={close}>
            Cancel
          </Button>
          <Button
            onClick={submit}
            disabled={!title.trim() || !projectId}
            loading={clone.isPending}
          >
            Create document
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function RenameTemplateModal({
  open,
  template,
  onClose
}: {
  open: boolean;
  template: DocumentDto | null;
  onClose: () => void;
}) {
  const updateDocument = useUpdateDocument();
  const [title, setTitle] = useState(template?.title ?? "");
  useEffect(() => {
    if (template) setTitle(template.title);
  }, [template]);
  if (!template) return null;
  const submit = async () => {
    const trimmed = title.trim();
    if (!trimmed || trimmed === template.title) {
      onClose();
      return;
    }
    try {
      await updateDocument.mutateAsync({
        id: template.id,
        previousProjectId: template.projectId,
        previousFolderId: template.folderId,
        patch: { title: trimmed }
      });
      notifications.show({
        message: `Template renamed to "${trimmed}".`,
        color: "green"
      });
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to rename template.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title={`Rename "${template.title}"`}>
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
            disabled={!title.trim() || title.trim() === template.title}
            loading={updateDocument.isPending}
          >
            Rename
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function DeleteTemplateModal({
  open,
  template,
  onClose
}: {
  open: boolean;
  template: DocumentDto | null;
  onClose: () => void;
}) {
  const deleteDocument = useDeleteDocument();
  if (!template) return null;
  const submit = async () => {
    try {
      await deleteDocument.mutateAsync({
        id: template.id,
        projectId: template.projectId,
        folderId: template.folderId,
        kind: template.kind
      });
      notifications.show({
        message: `Template "${template.title}" deleted.`,
        color: "green"
      });
      onClose();
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to delete template.",
        color: "red"
      });
    }
  };
  return (
    <Modal opened={open} onClose={onClose} title="Delete template">
      <Stack>
        <Text>
          Delete template <strong>{template.title}</strong>? Existing documents
          cloned from it remain — only the template itself is removed.
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
    const msg = (err as { message?: unknown }).message;
    if (typeof msg === "string") return msg;
  }
  return null;
}
