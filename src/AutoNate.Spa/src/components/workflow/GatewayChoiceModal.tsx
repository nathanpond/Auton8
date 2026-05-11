import { useState } from "react";
import { Alert, Button, Group, Modal, Stack, Text } from "@mantine/core";
import { GATEWAY_CHOICE_VARIABLE, GatewayChoice, TaskFormConfig } from "@/api/executions";

type Props = {
  config: TaskFormConfig;
  onClose: () => void;
  onComplete: (taskId: string, variables?: Record<string, unknown>) => Promise<void>;
};

// Default user-task UI when the workflow author chose mode="simple" AND the
// task flows directly into an exclusive gateway. One button per outgoing
// flow; clicking sets the reserved __autonateChosenFlow variable, which the
// publish-time-injected condition expressions on the gateway use to route.
export default function GatewayChoiceModal({ config, onClose, onComplete }: Props) {
  const [submittingFlowId, setSubmittingFlowId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const choices = config.gatewayChoices ?? [];

  const onPick = async (choice: GatewayChoice) => {
    setSubmittingFlowId(choice.flowId);
    setError(null);
    try {
      await onComplete(config.taskId, { [GATEWAY_CHOICE_VARIABLE]: choice.flowId });
      onClose();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setSubmittingFlowId(null);
    }
  };

  const submitting = submittingFlowId !== null;

  return (
    <Modal
      opened
      onClose={onClose}
      title={config.taskName || "Choose a path"}
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
        <Text>Pick a path to continue:</Text>
        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        <Group justify="space-between" gap="xs" wrap="wrap" mt="sm">
          <Button variant="default" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Group gap="xs" wrap="wrap">
            {choices.map((choice) => (
              <Button
                key={choice.flowId}
                onClick={() => onPick(choice)}
                disabled={submitting && submittingFlowId !== choice.flowId}
                loading={submittingFlowId === choice.flowId}
                title={choice.description ?? undefined}
              >
                {choice.label}
              </Button>
            ))}
          </Group>
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
