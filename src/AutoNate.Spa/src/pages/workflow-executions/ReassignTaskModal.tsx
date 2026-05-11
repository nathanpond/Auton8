import { useMemo, useState } from "react";
import { Button, Group, Modal, Select, Stack, Text } from "@mantine/core";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useUsers } from "@/hooks/useUsers";

type Props = {
  taskLabel: string;
  currentAssignee: string | null;
  busy: boolean;
  onConfirm: (assignee: string | null) => void;
  onCancel: () => void;
};

// Admin override picker for reassigning a single runtime task. The save
// button posts the chosen userId (or null to clear).
export default function ReassignTaskModal({
  taskLabel,
  currentAssignee,
  busy,
  onConfirm,
  onCancel
}: Props) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();
  const [selected, setSelected] = useState<string | null>(currentAssignee ?? null);

  const sortedUsers = useMemo(
    () =>
      [...users].sort((a, b) => {
        const an = userDisplayName(a) ?? a.username;
        const bn = userDisplayName(b) ?? b.username;
        return an.localeCompare(bn);
      }),
    [users]
  );

  const currentLabel = (() => {
    if (!currentAssignee) return "(unassigned)";
    const u = directory.get(currentAssignee);
    return userDisplayName(u) ?? currentAssignee;
  })();

  const submit = () => {
    onConfirm(selected && selected.trim().length > 0 ? selected : null);
  };

  return (
    <Modal
      opened
      onClose={onCancel}
      title="Reassign Task"
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
      withCloseButton={!busy}
      zIndex={1090}
    >
      <Stack gap="md">
        <Text size="sm">
          Reassign <strong>{taskLabel}</strong>.
        </Text>
        <Text size="xs" c="dimmed">
          Currently assigned to: <strong>{currentLabel}</strong>
        </Text>
        <Select
          label="New assignee"
          value={selected}
          onChange={setSelected}
          disabled={busy}
          clearable
          searchable
          placeholder="(unassigned)"
          data={sortedUsers.map((u) => ({
            value: u.userId,
            label: userDisplayName(u) ?? u.username
          }))}
        />
        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} loading={busy}>
            Save
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
