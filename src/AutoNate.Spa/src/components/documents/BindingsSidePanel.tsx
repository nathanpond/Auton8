import { useMemo, useState } from "react";
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Group,
  Loader,
  Modal,
  ScrollArea,
  Select,
  Stack,
  Text,
  Textarea,
  TextInput,
  Tooltip
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import type {
  AqlTableResolvedValue,
  DocumentBindingDto,
  DocumentBindingKind,
  RecordFieldResolvedValue
} from "@/api/documentBindings";
import {
  useCreateDocumentBinding,
  useDeleteDocumentBinding,
  useDocumentBindings,
  useRefreshAllDocumentBindings,
  useRefreshDocumentBinding
} from "@/hooks/useDocumentBindings";

// Side panel listing every binding on the open document. From here a
// user can create new bindings, refresh individual rows or all at once,
// and delete bindings. Insertion of the placeholder text into the
// document body is the caller's responsibility (we surface `onInsert`
// so DocxDocumentEditor can dispatch a ProseMirror transaction).
//
// v1 is intentionally side-panel-only — the in-document decoration
// plugin paints the resolved value over `{{binding:UUID}}` placeholders
// independently. The two surfaces share the same React Query cache.

type Props = {
  documentId: string;
  // Called when the user picks an existing binding to insert into the
  // editor body, or after creating a new binding via the Insert dialog.
  // Receives the binding id; the caller is responsible for actually
  // dispatching the ProseMirror transaction that adds the placeholder.
  onInsert?: (binding: DocumentBindingDto) => void;
  // True when the user can modify bindings (Document.Edit). False for
  // viewers and commenters — they see the list + can refresh but can't
  // create or delete.
  canEdit: boolean;
  // Optional handler for the close button. When omitted, the close
  // button is hidden — useful for embedding contexts that don't want
  // the panel dismissable (e.g. preview surfaces that always show it).
  // In the editor we wire this to a toolbar toggle in DocxDocumentEditor.
  onClose?: () => void;
};

export default function BindingsSidePanel({ documentId, onInsert, canEdit, onClose }: Props) {
  const { data: bindings = [], isLoading } = useDocumentBindings(documentId);
  const refreshOne = useRefreshDocumentBinding();
  const refreshAll = useRefreshAllDocumentBindings();
  const deleteBinding = useDeleteDocumentBinding();
  const [insertModalOpen, setInsertModalOpen] = useState(false);

  return (
    <Box
      style={{
        width: 320,
        height: "100%",
        borderLeft: "1px solid var(--mantine-color-gray-3)",
        background: "var(--mantine-color-body)",
        display: "flex",
        flexDirection: "column",
        minHeight: 0
      }}
    >
      <Group justify="space-between" px="sm" py="xs">
        <Text fw={600} size="sm">
          Live data bindings
        </Text>
        <Group gap={4}>
          <Tooltip label="Refresh all" withArrow openDelay={350}>
            <ActionIcon
              size="sm"
              variant="subtle"
              disabled={bindings.length === 0}
              loading={refreshAll.isPending}
              onClick={async () => {
                try {
                  const res = await refreshAll.mutateAsync({ documentId });
                  const failed = res.failures.length;
                  notifications.show({
                    message:
                      failed === 0
                        ? `Refreshed ${res.items.length} bindings.`
                        : `Refreshed ${res.items.length - failed}/${res.items.length}; ${failed} failed.`,
                    color: failed === 0 ? "green" : "yellow"
                  });
                } catch {
                  notifications.show({
                    message: "Refresh all failed.",
                    color: "red"
                  });
                }
              }}
            >
              <i className="fa fa-rotate" aria-hidden />
            </ActionIcon>
          </Tooltip>
          {canEdit ? (
            <Tooltip label="New binding" withArrow openDelay={350}>
              <ActionIcon
                size="sm"
                variant="filled"
                color="blue"
                onClick={() => setInsertModalOpen(true)}
                aria-label="Insert binding"
              >
                <i className="fa fa-plus" aria-hidden />
              </ActionIcon>
            </Tooltip>
          ) : null}
          {onClose ? (
            <Tooltip label="Close bindings panel" withArrow openDelay={350}>
              <ActionIcon
                size="sm"
                variant="subtle"
                onClick={onClose}
                aria-label="Close bindings panel"
              >
                <i className="fa fa-xmark" aria-hidden />
              </ActionIcon>
            </Tooltip>
          ) : null}
        </Group>
      </Group>
      <Divider />
      <ScrollArea style={{ flex: 1 }}>
        {isLoading ? (
          <Group justify="center" py="md">
            <Loader size="xs" />
          </Group>
        ) : bindings.length === 0 ? (
          <Stack p="sm" gap={4}>
            <Text c="dimmed" size="xs">
              No bindings yet.
            </Text>
            <Text c="dimmed" size="xs">
              Bindings embed live data (record fields, AQL tables) into the document.
              They render in-place where you insert the placeholder text.
            </Text>
          </Stack>
        ) : (
          <Stack gap={4} p="xs">
            {bindings.map((b) => (
              <BindingRow
                key={b.id}
                binding={b}
                canEdit={canEdit}
                onRefresh={() =>
                  refreshOne.mutate({ documentId, bindingId: b.id })
                }
                onDelete={() =>
                  deleteBinding.mutate(
                    { documentId, bindingId: b.id },
                    {
                      onSuccess: () =>
                        notifications.show({
                          message: "Binding deleted.",
                          color: "green"
                        })
                    }
                  )
                }
                onInsert={() => onInsert?.(b)}
              />
            ))}
          </Stack>
        )}
      </ScrollArea>

      <InsertBindingModal
        open={insertModalOpen}
        documentId={documentId}
        onClose={() => setInsertModalOpen(false)}
        onCreated={(created) => {
          setInsertModalOpen(false);
          onInsert?.(created);
        }}
      />
    </Box>
  );
}

function BindingRow({
  binding,
  canEdit,
  onRefresh,
  onDelete,
  onInsert
}: {
  binding: DocumentBindingDto;
  canEdit: boolean;
  onRefresh: () => void;
  onDelete: () => void;
  onInsert: () => void;
}) {
  const resolved = useMemo(
    () => decodeResolvedValue(binding),
    [binding]
  );
  return (
    <Box
      p="xs"
      style={{
        border: "1px solid var(--mantine-color-gray-3)",
        borderRadius: 4
      }}
    >
      <Group justify="space-between" gap={4} wrap="nowrap">
        <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
          <Group gap={6} wrap="nowrap">
            <Badge
              size="xs"
              variant="light"
              color={binding.kind === "record-field" ? "blue" : "grape"}
            >
              {binding.kind}
            </Badge>
            <Text size="xs" truncate>
              {binding.label ?? "(unlabelled)"}
            </Text>
          </Group>
          <BindingPreview kind={binding.kind} resolved={resolved} />
        </Stack>
        <Group gap={2}>
          <Tooltip label="Insert at cursor" withArrow openDelay={350}>
            <ActionIcon size="xs" variant="subtle" onClick={onInsert} aria-label="Insert at cursor">
              <i className="fa fa-arrow-left" aria-hidden />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Refresh" withArrow openDelay={350}>
            <ActionIcon size="xs" variant="subtle" onClick={onRefresh} aria-label="Refresh binding">
              <i className="fa fa-rotate" aria-hidden />
            </ActionIcon>
          </Tooltip>
          {canEdit ? (
            <Tooltip label="Delete" withArrow openDelay={350}>
              <ActionIcon
                size="xs"
                variant="subtle"
                color="red"
                onClick={onDelete}
                aria-label="Delete binding"
              >
                <i className="fa fa-trash" aria-hidden />
              </ActionIcon>
            </Tooltip>
          ) : null}
        </Group>
      </Group>
    </Box>
  );
}

function BindingPreview({
  kind,
  resolved
}: {
  kind: DocumentBindingKind;
  resolved: ReturnType<typeof decodeResolvedValue>;
}) {
  if (!resolved) {
    return (
      <Text size="xs" c="dimmed">
        (not yet resolved)
      </Text>
    );
  }
  if (kind === "record-field") {
    const v = resolved as RecordFieldResolvedValue;
    return (
      <Text size="xs" truncate>
        {v.text}
      </Text>
    );
  }
  if (kind === "aql-table") {
    const v = resolved as AqlTableResolvedValue;
    return (
      <Text size="xs" c="dimmed" truncate>
        {v.totalCount} rows · {v.columns.length} cols · {v.durationMs}ms
        {v.truncated ? " (truncated)" : ""}
      </Text>
    );
  }
  return null;
}

function decodeResolvedValue(
  binding: DocumentBindingDto
): RecordFieldResolvedValue | AqlTableResolvedValue | null {
  if (!binding.lastResolvedValueJsonb) return null;
  try {
    return JSON.parse(binding.lastResolvedValueJsonb);
  } catch {
    return null;
  }
}

// ── Insert binding modal ────────────────────────────────────────────────

function InsertBindingModal({
  open,
  documentId,
  onClose,
  onCreated
}: {
  open: boolean;
  documentId: string;
  onClose: () => void;
  onCreated: (binding: DocumentBindingDto) => void;
}) {
  const [kind, setKind] = useState<DocumentBindingKind>("record-field");
  const [label, setLabel] = useState("");
  const [recordId, setRecordId] = useState("");
  const [fieldKey, setFieldKey] = useState("");
  const [queryText, setQueryText] = useState("");
  const [limit, setLimit] = useState("200");
  const create = useCreateDocumentBinding();

  const submit = async () => {
    let configJsonb: string;
    if (kind === "record-field") {
      const id = recordId.trim();
      const key = fieldKey.trim();
      if (!id || !key) {
        notifications.show({ message: "Record id and field key are required.", color: "red" });
        return;
      }
      configJsonb = JSON.stringify({ recordId: id, fieldKey: key });
    } else {
      const q = queryText.trim();
      if (!q) {
        notifications.show({ message: "AQL query is required.", color: "red" });
        return;
      }
      const parsedLimit = Number.parseInt(limit, 10);
      configJsonb = JSON.stringify({
        queryText: q,
        ...(Number.isFinite(parsedLimit) ? { limit: parsedLimit } : {})
      });
    }
    try {
      const created = await create.mutateAsync({
        documentId,
        kind,
        configJsonb,
        label: label.trim() || undefined
      });
      notifications.show({ message: "Binding created.", color: "green" });
      // Reset form for next use
      setLabel("");
      setRecordId("");
      setFieldKey("");
      setQueryText("");
      onCreated(created);
    } catch (err) {
      notifications.show({
        message: extractErrorMessage(err) ?? "Failed to create binding.",
        color: "red"
      });
    }
  };

  return (
    <Modal opened={open} onClose={onClose} title="Insert live data binding" size="lg">
      <Stack>
        <Select
          label="Binding kind"
          data={[
            { value: "record-field", label: "Record field — one field from one record" },
            { value: "aql-table", label: "AQL table — run a query, render results as a table" }
          ]}
          value={kind}
          onChange={(v) => v && setKind(v as DocumentBindingKind)}
          allowDeselect={false}
        />
        <TextInput
          label="Label (optional)"
          description="Shown in the side panel + on hover in the document"
          value={label}
          onChange={(e) => setLabel(e.currentTarget.value)}
        />
        {kind === "record-field" ? (
          <>
            <TextInput
              label="Record ID"
              description="UUID of the record (find via the Records page; copy from the URL)"
              placeholder="00000000-0000-0000-0000-000000000000"
              value={recordId}
              onChange={(e) => setRecordId(e.currentTarget.value)}
              autoFocus
            />
            <TextInput
              label="Field key"
              description="The field's key (not its label) — e.g. 'name', 'priority', 'due_date'"
              value={fieldKey}
              onChange={(e) => setFieldKey(e.currentTarget.value)}
            />
          </>
        ) : (
          <>
            <Textarea
              label="AQL query"
              description={
                <Text size="xs" c="dimmed">
                  Same AQL grammar as the Query page. Permissions apply per-row.{" "}
                  <Code>FROM Records WHERE …</Code> is the common shape.
                </Text>
              }
              minRows={5}
              value={queryText}
              onChange={(e) => setQueryText(e.currentTarget.value)}
              autoFocus
            />
            <TextInput
              label="Row limit"
              description="Caps the number of rows persisted into the snapshot (1–1000)"
              value={limit}
              onChange={(e) => setLimit(e.currentTarget.value)}
            />
          </>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={submit} loading={create.isPending}>
            Insert
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
