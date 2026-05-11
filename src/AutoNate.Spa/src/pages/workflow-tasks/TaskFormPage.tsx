import { useNavigate, useParams } from "react-router-dom";
import { Alert, Badge, Box, Button, Group, Paper, Text } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { JsxFormHost } from "@/components/JsxFormHost";
import { useCompleteTask, useTaskFormConfig } from "@/hooks/useExecutions";

// Full-page render of a workflow user-task form. Reachable when the workflow
// author chose mode="page" on the user task — the My Tasks panel routes
// here instead of opening a modal.
export default function TaskFormPage() {
  const { taskId } = useParams<{ taskId: string }>();
  const navigate = useNavigate();
  const { data: config, isLoading, error } = useTaskFormConfig(taskId ?? null);
  const completeTask = useCompleteTask();

  if (isLoading) {
    return (
      <Box py="md">
        <Text c="dimmed">
          <i className="fa fa-spinner fa-spin" style={{ marginRight: 8 }} />
          Loading task…
        </Text>
      </Box>
    );
  }

  if (error) {
    const status = (error as { response?: { status?: number } }).response?.status;
    return (
      <Box py="md">
        <Alert color="red" variant="light">
          {status === 403
            ? "You don't have permission to view this task (workflowtask/view required)."
            : `Failed to load task: ${(error as Error).message}`}
        </Alert>
      </Box>
    );
  }

  if (!config) {
    return (
      <Box py="md">
        <Alert color="yellow" variant="light">
          Task <code>{taskId}</code> was not found, or it has already been completed.
        </Alert>
      </Box>
    );
  }

  if (!config.form) {
    return (
      <Box py="md">
        <Alert color="yellow" variant="light">
          {config.formShortCode
            ? `The form "${config.formShortCode}" referenced by this task could not be loaded.`
            : "This task is configured for Form Page mode but no form is selected. Edit the user task in Workflow Studio."}
        </Alert>
      </Box>
    );
  }

  const onSubmit = async (payload: Record<string, unknown>) => {
    await completeTask.mutateAsync({ taskId: config.taskId, variables: payload });
    navigate("/", { replace: true });
  };

  return (
    <Box py="md">
      <PageHeader
        title={config.taskName || "Complete task"}
        description={
          <Group gap={6} wrap="wrap" align="center">
            <i className="fa fa-diagram-project" />
            <span>
              {config.processInstanceName ?? config.processDefinitionName ?? "Workflow"}
            </span>
            {config.form.isDraftFallback && (
              <Badge color="yellow" variant="filled">
                Draft form
              </Badge>
            )}
          </Group>
        }
        actions={
          <Button
            variant="default"
            leftSection={<i className="fa fa-chevron-left" />}
            onClick={() => navigate(-1)}
          >
            Back
          </Button>
        }
      />

      <Paper withBorder radius="md" p="md">
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
          onSubmit={onSubmit}
        />
      </Paper>
    </Box>
  );
}
