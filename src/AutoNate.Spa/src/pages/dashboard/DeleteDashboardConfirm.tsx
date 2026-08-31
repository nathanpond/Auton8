import { Button, Group, Modal, Stack, Text } from "@mantine/core";

type Props = {
  opened: boolean;
  name: string;
  onConfirm: () => void;
  onCancel: () => void;
};

export function DeleteDashboardConfirm({ opened, name, onConfirm, onCancel }: Props) {
  return (
    <Modal opened={opened} onClose={onCancel} title="Delete dashboard?" zIndex={1075}>
      <Stack>
        <Text>
          Delete <strong>{name}</strong> and all its widgets? This can&apos;t be undone.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={onCancel}>Cancel</Button>
          <Button color="red" onClick={onConfirm}>Delete</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
