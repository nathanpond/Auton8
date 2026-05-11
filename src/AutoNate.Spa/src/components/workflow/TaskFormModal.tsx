import { useState } from "react";
import { Alert, Button, Code, Group, Modal, Stack, Text } from "@mantine/core";
import { TaskFormConfig } from "@/api/executions";
import { JsxFormHost } from "@/components/JsxFormHost";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string, variables: Record<string, unknown>) => Promise<void>;
};

// Mode="modal": render the configured Form inside a modal. The form's
// submit payload becomes the workflow variables passed to complete-task.
export default function TaskFormModal({ config, onClose, onComplete }: Props) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!config.form) {
    return (
      <NoFormModal
        title={config.taskName || "Complete task"}
        message={
          config.formShortCode
            ? `The form "${config.formShortCode}" referenced by this task could not be loaded.`
            : "This task is configured for a form but no form is selected. Edit the user task in Workflow Studio."
        }
        onClose={onClose}
      />
    );
  }

  const submit = async (payload: Record<string, unknown>) => {
    setSubmitting(true);
    setError(null);
    try {
      await onComplete(config.taskId, payload);
      onClose();
    } catch (err) {
      setError(describeError(err));
      throw err;
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      size="lg"
      closeOnClickOutside={!submitting}
      closeOnEscape={!submitting}
      withCloseButton={!submitting}
      title={
        <Stack gap={2}>
          <Text fw={600}>{config.taskName || "Complete task"}</Text>
          {config.processInstanceName && (
            <Text size="xs" c="dimmed">
              <i className="fa fa-diagram-project" style={{ marginRight: 8 }} />
              {config.processInstanceName}
            </Text>
          )}
        </Stack>
      }
    >
      <Stack gap="sm">
        {config.form.isDraftFallback && (
          <Alert color="yellow" variant="light">
            <Text size="sm">
              <strong>Heads up:</strong> form <Code>{config.form.shortCode}</Code> has no
              published version yet — the draft is being shown.
            </Text>
          </Alert>
        )}
        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        <JsxFormHost
          source={config.form.formCode}
          data={config.variables as Record<string, unknown>}
          mode="edit"
          context={{
            taskId: config.taskId,
            taskName: config.taskName,
            taskDefinitionKey: config.taskDefinitionKey,
            processInstanceId: config.processInstanceId,
            processInstanceName: config.processInstanceName
          }}
          extras={{ shortCode: config.form.shortCode }}
          onSubmit={submit}
        />
      </Stack>
    </Modal>
  );
}

function NoFormModal({
  title,
  message,
  onClose
}: {
  title: string;
  message: string;
  onClose: () => void;
}) {
  return (
    <Modal opened onClose={onClose} title={title}>
      <Stack gap="sm">
        <Alert color="yellow" variant="light">
          {message}
        </Alert>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Close
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
