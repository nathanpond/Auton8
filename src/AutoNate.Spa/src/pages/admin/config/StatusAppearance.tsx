import { useEffect, useMemo, useRef, useState } from "react";
import { AxiosError } from "axios";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Card,
  Code,
  Group,
  Modal,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
  UnstyledButton
} from "@mantine/core";
import {
  DndContext,
  DragEndEvent,
  PointerSensor,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  useSortable,
  verticalListSortingStrategy
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import PageHeader from "@/components/PageHeader";
import ColorPicker from "@/components/ColorPicker";
import {
  useCreateStatusAppearance,
  useDeleteStatusAppearance,
  useReorderStatusAppearance,
  useStatusAppearance,
  useUpdateStatusAppearance
} from "@/hooks/useStatusAppearance";
import { badgeTextColor, normalizeHex } from "@/lib/statusAppearance";
import { StatusAppearanceEntry } from "@/types/statusAppearance";

const isSiteDefault = (status: string) => status.trim().toLowerCase() === "site_default";

type DraftStatusAppearanceRow = {
  id: string;
  status: string;
  color: string;
};

export default function StatusAppearance() {
  const { data, isLoading } = useStatusAppearance();
  const createEntry = useCreateStatusAppearance();
  const deleteEntry = useDeleteStatusAppearance();
  const reorderEntries = useReorderStatusAppearance();
  const [draftRows, setDraftRows] = useState<DraftStatusAppearanceRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  // Pending-confirmation target. Lives at the page level so the modal can
  // outlive the row that opened it (closing the modal doesn't unmount the
  // row, but the modal needs to render outside the row's table cell).
  const [pendingDelete, setPendingDelete] = useState<StatusAppearanceEntry | null>(null);
  const rows = Array.isArray(data) ? data : [];

  // Site_Default is pinned at the top regardless of sort_order; everything
  // else is the draggable list. Server already sorts by sort_order, so this
  // split preserves order within `sortable`.
  const { siteDefault, sortable } = useMemo(() => {
    let pinned: StatusAppearanceEntry | null = null;
    const others: StatusAppearanceEntry[] = [];
    for (const r of rows) {
      if (!pinned && isSiteDefault(r.status)) pinned = r;
      else others.push(r);
    }
    return { siteDefault: pinned, sortable: others };
  }, [rows]);

  // Local mirror of the sortable list so the order updates immediately on
  // drop, before the server round-trip resolves. Re-syncs whenever the
  // server-side list changes (mutation invalidation etc.).
  const [sortableOrder, setSortableOrder] = useState<StatusAppearanceEntry[]>(sortable);
  useEffect(() => {
    setSortableOrder(sortable);
  }, [sortable]);

  const dragSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  );

  const handleDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    const oldIdx = sortableOrder.findIndex((r) => r.id === active.id);
    const newIdx = sortableOrder.findIndex((r) => r.id === over.id);
    if (oldIdx === -1 || newIdx === -1) return;
    const next = arrayMove(sortableOrder, oldIdx, newIdx);
    setSortableOrder(next);
    setError(null);
    reorderEntries.mutate(
      next.map((r) => r.id),
      {
        onError: (err) => {
          setError(describeError(err));
          // Snap back to the server's order on failure.
          setSortableOrder(sortable);
        }
      }
    );
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setError(null);
    try {
      await deleteEntry.mutateAsync(pendingDelete.id);
      setPendingDelete(null);
    } catch (err) {
      setError(describeError(err));
      setPendingDelete(null);
    }
  };

  const addRow = () => {
    setDraftRows((current) => [
      ...current,
      {
        id: `draft-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        status: "",
        color: "#0d6efd"
      }
    ]);
  };

  const updateDraftRow = (id: string, patch: Partial<DraftStatusAppearanceRow>) => {
    setDraftRows((current) =>
      current.map((row) => (row.id === id ? { ...row, ...patch } : row))
    );
  };

  return (
    <>
      <PageHeader
        title="Status Appearance"
        description="Configure the status-to-color combinations used for badge previews."
      />

      <Card withBorder shadow="sm">
        <Group justify="space-between" align="center" mb="md">
          <Title order={5} m={0}>
            Statuses
          </Title>
          <Button size="xs" leftSection={<i className="fa fa-plus" />} onClick={addRow}>
            Add status
          </Button>
        </Group>

        {error && (
          <Alert color="red" variant="light" mb="md">
            {error}
          </Alert>
        )}

        <Table withTableBorder withColumnBorders striped verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th style={{ width: "4%" }} />
              <Table.Th style={{ width: "30%" }}>Status</Table.Th>
              <Table.Th style={{ width: "32%" }}>Color</Table.Th>
              <Table.Th style={{ width: "22%" }}>Preview</Table.Th>
              <Table.Th style={{ width: "12%" }} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {isLoading && (
              <Table.Tr>
                <Table.Td colSpan={5} ta="center" py="lg">
                  <Text c="dimmed">Loading...</Text>
                </Table.Td>
              </Table.Tr>
            )}

            {!isLoading && rows.length === 0 && draftRows.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={5} ta="center" py="lg">
                  <Text c="dimmed">No statuses yet. Add one to get started.</Text>
                </Table.Td>
              </Table.Tr>
            )}

            {siteDefault && (
              <PersistedRow
                key={siteDefault.id}
                row={siteDefault}
                pinned
                onRequestDelete={() => setPendingDelete(siteDefault)}
              />
            )}

            <DndContext sensors={dragSensors} onDragEnd={handleDragEnd}>
              <SortableContext
                items={sortableOrder.map((r) => r.id)}
                strategy={verticalListSortingStrategy}
              >
                {sortableOrder.map((row) => (
                  <PersistedRow
                    key={row.id}
                    row={row}
                    onRequestDelete={() => setPendingDelete(row)}
                  />
                ))}
              </SortableContext>
            </DndContext>

            {draftRows.map((row) => (
              <DraftRow
                key={row.id}
                row={row}
                isSaving={createEntry.isPending}
                onChange={(patch) => updateDraftRow(row.id, patch)}
                onDelete={() => setDraftRows((current) => current.filter((x) => x.id !== row.id))}
                onCreate={async (request) => {
                  setError(null);
                  try {
                    await createEntry.mutateAsync(request);
                    setDraftRows((current) => current.filter((x) => x.id !== row.id));
                  } catch (err) {
                    setError(describeError(err));
                    throw err;
                  }
                }}
              />
            ))}
          </Table.Tbody>
        </Table>
      </Card>

      <Modal
        opened={pendingDelete !== null}
        onClose={() => (deleteEntry.isPending ? undefined : setPendingDelete(null))}
        title="Delete status appearance"
        centered
      >
        <Stack gap="md">
          <Text>
            Delete the appearance for status{" "}
            <Code>{pendingDelete?.status}</Code>? Records with this status will fall back
            to the default badge color.
          </Text>
          <Group justify="flex-end" gap="xs">
            <Button
              variant="default"
              onClick={() => setPendingDelete(null)}
              disabled={deleteEntry.isPending}
            >
              Cancel
            </Button>
            <Button
              color="red"
              leftSection={<i className="fa fa-trash" />}
              onClick={confirmDelete}
              loading={deleteEntry.isPending}
            >
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}

function PersistedRow({
  row,
  pinned = false,
  onRequestDelete
}: {
  row: StatusAppearanceEntry;
  pinned?: boolean;
  onRequestDelete: () => void;
}) {
  const updateEntry = useUpdateStatusAppearance();
  const [status, setStatus] = useState(row.status);
  const [color, setColor] = useState(row.color);
  const [error, setError] = useState<string | null>(null);

  // Pinned rows (Site_Default) opt out of @dnd-kit; other rows participate
  // in the SortableContext that wraps their map.
  const sortable = useSortable({ id: row.id, disabled: pinned });

  useEffect(() => {
    setStatus(row.status);
    setColor(row.color);
  }, [row.id, row.status, row.color]);

  useEffect(() => {
    if (status === row.status && color === row.color) return;
    if (!status.trim()) return;

    const timeoutId = window.setTimeout(() => {
      void (async () => {
        setError(null);
        try {
          await updateEntry.mutateAsync({
            id: row.id,
            request: { status: status.trim(), color: color.trim() }
          });
        } catch (err) {
          setError(describeError(err));
        }
      })();
    }, 450);

    return () => window.clearTimeout(timeoutId);
  }, [status, color, row.id, row.status, row.color, updateEntry]);

  const rowStyle = pinned
    ? undefined
    : {
        transform: CSS.Transform.toString(sortable.transform),
        transition: sortable.transition,
        opacity: sortable.isDragging ? 0.5 : 1
      };

  return (
    <Table.Tr ref={pinned ? undefined : sortable.setNodeRef} style={rowStyle}>
      <Table.Td ta="center">
        {pinned ? (
          <Tooltip label="Site_Default is pinned" withArrow>
            <i className="fa fa-thumbtack" style={{ color: "var(--mantine-color-gray-5)" }} />
          </Tooltip>
        ) : (
          <UnstyledButton
            ref={sortable.setActivatorNodeRef}
            {...sortable.attributes}
            {...sortable.listeners}
            title="Drag to reorder"
            aria-label="Drag to reorder"
            style={{
              cursor: "grab",
              color: "var(--mantine-color-gray-6)",
              padding: "0 4px",
              touchAction: "none"
            }}
          >
            <i className="fa fa-grip-vertical" />
          </UnstyledButton>
        )}
      </Table.Td>
      <Table.Td>
        <TextInput
          placeholder="Enter a status"
          value={status}
          onChange={(e) => setStatus(e.currentTarget.value)}
          error={error}
          readOnly={pinned}
        />
      </Table.Td>
      <Table.Td>
        <ColorPicker id={`status-color-${row.id}`} value={color} onChange={setColor} />
      </Table.Td>
      <Table.Td>
        <PreviewBadge status={status} color={color} />
      </Table.Td>
      <Table.Td ta="center">
        {pinned ? null : (
          <ActionIcon
            variant="subtle"
            color="red"
            size="sm"
            onClick={onRequestDelete}
            aria-label={`Delete ${status.trim() || row.status}`}
          >
            <i className="fa fa-trash" />
          </ActionIcon>
        )}
      </Table.Td>
    </Table.Tr>
  );
}

function DraftRow({
  row,
  isSaving,
  onChange,
  onDelete,
  onCreate
}: {
  row: DraftStatusAppearanceRow;
  isSaving: boolean;
  onChange: (patch: Partial<DraftStatusAppearanceRow>) => void;
  onDelete: () => void;
  onCreate: (request: { status: string; color: string }) => Promise<void>;
}) {
  const attemptedKeyRef = useRef<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const status = row.status.trim();
    if (!status) return;
    const requestKey = `${status}::${row.color.trim().toLowerCase()}`;
    if (attemptedKeyRef.current === requestKey) return;

    const timeoutId = window.setTimeout(() => {
      attemptedKeyRef.current = requestKey;
      void (async () => {
        try {
          setError(null);
          await onCreate({
            status,
            color: row.color.trim()
          });
        } catch (err) {
          setError(describeError(err));
        }
      })();
    }, 450);

    return () => window.clearTimeout(timeoutId);
  }, [row.status, row.color]);

  useEffect(() => {
    attemptedKeyRef.current = null;
    setError(null);
  }, [row.status, row.color]);

  return (
    <Table.Tr>
      <Table.Td />
      <Table.Td>
        <TextInput
          placeholder="Enter a status"
          value={row.status}
          onChange={(e) => onChange({ status: e.currentTarget.value })}
          error={error}
        />
      </Table.Td>
      <Table.Td>
        <ColorPicker
          id={`status-color-${row.id}`}
          value={row.color}
          onChange={(value) => onChange({ color: value })}
        />
      </Table.Td>
      <Table.Td>
        <PreviewBadge status={row.status} color={row.color} />
        {isSaving && row.status.trim() && (
          <Text size="sm" c="dimmed" mt={4}>
            Saving...
          </Text>
        )}
      </Table.Td>
      <Table.Td ta="center">
        <ActionIcon
          variant="subtle"
          color="red"
          size="sm"
          onClick={onDelete}
          aria-label={`Delete ${row.status.trim() || "draft status"}`}
        >
          <i className="fa fa-trash" />
        </ActionIcon>
      </Table.Td>
    </Table.Tr>
  );
}

function PreviewBadge({ status, color }: { status: string; color: string }) {
  const backgroundColor = normalizeHex(color) ?? "#6c757d";
  const textColor = badgeTextColor(color);
  const previewText = status.trim() || "Preview";

  return (
    <Box
      component="span"
      px="md"
      py={6}
      style={{
        backgroundColor,
        color: textColor,
        display: "inline-block",
        borderRadius: 999,
        fontWeight: 600,
        fontSize: 12
      }}
    >
      {previewText}
    </Box>
  );
}

function describeError(error: unknown): string {
  const axiosError = error as AxiosError<{ error?: string }>;
  return axiosError.response?.data?.error ?? axiosError.message ?? "Something went wrong.";
}
