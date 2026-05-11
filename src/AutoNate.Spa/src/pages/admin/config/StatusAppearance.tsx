import { useEffect, useRef, useState } from "react";
import { AxiosError } from "axios";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Card,
  Group,
  Table,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import ColorPicker from "@/components/ColorPicker";
import {
  useCreateStatusAppearance,
  useDeleteStatusAppearance,
  useStatusAppearance,
  useUpdateStatusAppearance
} from "@/hooks/useStatusAppearance";
import { badgeTextColor, normalizeHex } from "@/lib/statusAppearance";
import { StatusAppearanceEntry } from "@/types/statusAppearance";

type DraftStatusAppearanceRow = {
  id: string;
  status: string;
  color: string;
};

export default function StatusAppearance() {
  const { data, isLoading } = useStatusAppearance();
  const createEntry = useCreateStatusAppearance();
  const deleteEntry = useDeleteStatusAppearance();
  const [draftRows, setDraftRows] = useState<DraftStatusAppearanceRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const rows = Array.isArray(data) ? data : [];

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
              <Table.Th style={{ width: "32%" }}>Status</Table.Th>
              <Table.Th style={{ width: "34%" }}>Color</Table.Th>
              <Table.Th style={{ width: "22%" }}>Preview</Table.Th>
              <Table.Th style={{ width: "12%" }} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {isLoading && (
              <Table.Tr>
                <Table.Td colSpan={4} ta="center" py="lg">
                  <Text c="dimmed">Loading...</Text>
                </Table.Td>
              </Table.Tr>
            )}

            {!isLoading && rows.length === 0 && draftRows.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={4} ta="center" py="lg">
                  <Text c="dimmed">No statuses yet. Add one to get started.</Text>
                </Table.Td>
              </Table.Tr>
            )}

            {rows.map((row) => (
              <PersistedRow
                key={row.id}
                row={row}
                onDelete={async () => {
                  setError(null);
                  try {
                    await deleteEntry.mutateAsync(row.id);
                  } catch (err) {
                    setError(describeError(err));
                  }
                }}
              />
            ))}

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
    </>
  );
}

function PersistedRow({
  row,
  onDelete
}: {
  row: StatusAppearanceEntry;
  onDelete: () => Promise<void>;
}) {
  const updateEntry = useUpdateStatusAppearance();
  const [status, setStatus] = useState(row.status);
  const [color, setColor] = useState(row.color);
  const [error, setError] = useState<string | null>(null);

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

  return (
    <Table.Tr>
      <Table.Td>
        <TextInput
          placeholder="Enter a status"
          value={status}
          onChange={(e) => setStatus(e.currentTarget.value)}
          error={error}
        />
      </Table.Td>
      <Table.Td>
        <ColorPicker id={`status-color-${row.id}`} value={color} onChange={setColor} />
      </Table.Td>
      <Table.Td>
        <PreviewBadge status={status} color={color} />
      </Table.Td>
      <Table.Td ta="center">
        <ActionIcon
          variant="outline"
          color="red"
          size="sm"
          onClick={() => void onDelete()}
          aria-label={`Delete ${status.trim() || row.status}`}
        >
          <i className="fa fa-trash" />
        </ActionIcon>
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
          variant="outline"
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
