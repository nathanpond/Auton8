import { useState } from "react";
import { useParams } from "react-router-dom";
import { Alert, Box } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { ExecutionContent, describeError } from "./WorkflowExecutions";

export default function ExecutionPage() {
  const { id } = useParams<{ id: string }>();
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

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

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="sm"
        >
          {flash.message}
        </Alert>
      )}

      <div className="workflow-execution-page">
        <ExecutionContent
          processInstanceId={id}
          onTaskCompleted={(message) => setFlash({ kind: "success", message })}
          onError={(message) => setFlash({ kind: "error", message })}
        />
      </div>
    </Box>
  );
}

// Re-export so callers don't need to know which file owns the helper.
export { describeError };
