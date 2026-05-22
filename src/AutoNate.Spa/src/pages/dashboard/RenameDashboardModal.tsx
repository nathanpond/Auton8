import { useEffect, useState } from "react";
import { Button, Group, Modal, Stack, TextInput } from "@mantine/core";

type Props = {
  opened: boolean;
  initialName: string;
  onSave: (name: string) => void;
  onCancel: () => void;
};

export function RenameDashboardModal({ opened, initialName, onSave, onCancel }: Props) {
  const [name, setName] = useState(initialName);

  useEffect(() => {
    if (opened) setName(initialName);
  }, [opened, initialName]);

  const trimmed = name.trim();
  const isValid = trimmed.length > 0;

  return (
    <Modal opened={opened} onClose={onCancel} title="Rename dashboard" zIndex={1075}>
      <Stack>
        <TextInput
          label="Name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          autoFocus
          onKeyDown={(e) => {
            if (e.key === "Enter" && isValid) onSave(trimmed);
          }}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onCancel}>Cancel</Button>
          <Button disabled={!isValid} onClick={() => onSave(trimmed)}>Save</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
