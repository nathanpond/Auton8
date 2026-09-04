import { toast } from "@/components/notifications/toast";
import { useState } from "react";
import { useParams } from "react-router-dom";
import { Alert, Box } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { ExecutionContent, describeError } from "./WorkflowExecutions";

export default function ExecutionPage() {
  const { id } = useParams<{ id: string }>();

  if (!id) {
    return (
      <Box py="md">
        <Alert color="red" variant="light" role="alert">
          Missing execution id in the URL.
        </Alert>
      </Box>
    );
  }

  return (
    <Box py="md">
      <PageHeader title="Execution" />

      <div className="workflow-execution-page">
        <ExecutionContent
          processInstanceId={id}
          onTaskCompleted={(message) => toast.success(message)}
          onError={(message) => toast.error(message)}
        />
      </div>
    </Box>
  );
}

// Re-export so callers don't need to know which file owns the helper.
export { describeError };
