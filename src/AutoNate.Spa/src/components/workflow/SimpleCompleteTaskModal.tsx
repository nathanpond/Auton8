import { useState } from "react";
import { Alert, Button, Group, Modal, Stack, Text } from "@mantine/core";
import { TaskFormConfig } from "@/api/executions";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string) => Promise<void>;
};

// Default user-task UI when the workflow author chose mode="simple" (or left
// the userForm config off entirely). One button → one POST. No form, no
// payload variables.
export default function SimpleCompleteTaskModal({ config, onClose, onComplete }: Props) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onClick = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await onComplete(config.taskId);
      onClose();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      title={config.taskName || "Complete task"}
      closeOnClickOutside={!submitting}
      closeOnEscape={!submitting}
      withCloseButton={!submitting}
    >
      <Stack gap="sm">
        {config.processInstanceName && (
          <Text size="sm" c="dimmed">
            <i className="fa fa-diagram-project" style={{ marginRight: 8 }} />
            {config.processInstanceName}
          </Text>
        )}
        {config.description && (
          <Text style={{ whiteSpace: "pre-wrap" }}>{config.description}</Text>
        )}
        <Text>
          Mark this task complete? The workflow will continue from here using the process
          variables already on the instance.
        </Text>
        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        <Group justify="flex-end" gap="xs" mt="sm">
          <Button variant="default" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button
            color="green"
            onClick={onClick}
            loading={submitting}
            leftSection={!submitting ? <i className="fa fa-check" /> : undefined}
          >
            Complete Task
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
}
