import { useState } from "react";
import { useParams } from "react-router-dom";
import { ExecutionContent, describeError } from "./WorkflowExecutions";

export default function ExecutionPage() {
  const { id } = useParams<{ id: string }>();
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  if (!id) {
    return (
      <div className="alert alert-danger" role="alert">
        Missing execution id in the URL.
      </div>
    );
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Execution</h1>
        </div>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      <div className="workflow-execution-page">
        <ExecutionContent
          processInstanceId={id}
          onTaskCompleted={(message) => setFlash({ kind: "success", message })}
          onError={(message) => setFlash({ kind: "error", message })}
        />
      </div>
    </>
  );
}

// Re-export so callers don't need to know which file owns the helper.
export { describeError };
