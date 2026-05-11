import { useState } from "react";
import { Button, Group, Modal, Stack, Text, TextInput } from "@mantine/core";

type Props = {
  taskLabel: string;
  currentDueDate: string | null;
  busy: boolean;
  onConfirm: (dueDateIso: string | null) => void;
  onCancel: () => void;
};

// Parses an ISO 8601 string into the "yyyy-MM-dd" form a date input needs.
function isoToInputValue(iso: string | null): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (n: number) => n.toString().padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

// Admin override picker for setting/clearing a runtime task's due date. Empty
// input clears the due date on save.
export default function ChangeDueDateModal({
  taskLabel,
  currentDueDate,
  busy,
  onConfirm,
  onCancel
}: Props) {
  const [value, setValue] = useState<string>(isoToInputValue(currentDueDate));

  const submit = () => {
    if (value.trim().length === 0) {
      onConfirm(null);
      return;
    }
    // The date input gives us "yyyy-MM-dd" with no time. Anchor to noon UTC
    // so the chosen calendar date round-trips for every timezone from UTC-12
    // through UTC+12.
    const [yearStr, monthStr, dayStr] = value.split("-");
    const year = Number(yearStr);
    const month = Number(monthStr);
    const day = Number(dayStr);
    if (!year || !month || !day) {
      onConfirm(null);
      return;
    }
    const noonUtc = new Date(Date.UTC(year, month - 1, day, 12, 0, 0));
    if (Number.isNaN(noonUtc.getTime())) {
      onConfirm(null);
      return;
    }
    onConfirm(noonUtc.toISOString());
  };

  return (
    <Modal
      opened
      onClose={onCancel}
      title="Change Due Date"
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
      withCloseButton={!busy}
      zIndex={1090}
    >
      <Stack gap="md">
        <Text size="sm">
          Change due date for <strong>{taskLabel}</strong>.
        </Text>
        <Text size="xs" c="dimmed">
          Currently due:{" "}
          <strong>
            {currentDueDate ? new Date(currentDueDate).toLocaleDateString() : "(no due date)"}
          </strong>
        </Text>
        <TextInput
          label="New due date"
          type="date"
          value={value}
          onChange={(e) => setValue(e.currentTarget.value)}
          disabled={busy}
          description="Leave blank to clear the due date."
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
