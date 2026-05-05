import { useNavigate, useParams } from "react-router-dom";
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
      <div className="app-content-margin p-4 text-muted">
        <i className="fa fa-spinner fa-spin me-2" />
        Loading task…
      </div>
    );
  }

  if (error) {
    const status = (error as { response?: { status?: number } }).response?.status;
    return (
      <div className="app-content-margin p-4">
        <div className="alert alert-danger">
          {status === 403
            ? "You don't have permission to view this task (workflowtask/view required)."
            : `Failed to load task: ${(error as Error).message}`}
        </div>
      </div>
    );
  }

  if (!config) {
    return (
      <div className="app-content-margin p-4">
        <div className="alert alert-warning">
          Task <code>{taskId}</code> was not found, or it has already been completed.
        </div>
      </div>
    );
  }

  if (!config.form) {
    return (
      <div className="app-content-margin p-4">
        <div className="alert alert-warning">
          {config.formShortCode
            ? `The form "${config.formShortCode}" referenced by this task could not be loaded.`
            : "This task is configured for Form Page mode but no form is selected. Edit the user task in Workflow Studio."}
        </div>
      </div>
    );
  }

  const onSubmit = async (payload: Record<string, unknown>) => {
    await completeTask.mutateAsync({ taskId: config.taskId, variables: payload });
    navigate("/", { replace: true });
  };

  return (
    <div className="app-content-margin p-3">
      <div className="page-head d-flex flex-wrap gap-3 align-items-start justify-content-between">
        <div>
          <h1 className="page-header mb-1">{config.taskName || "Complete task"}</h1>
          <p className="page-head-copy mb-0">
            <i className="fa fa-diagram-project me-2" />
            {config.processInstanceName ?? config.processDefinitionName ?? "Workflow"}
            {config.form.isDraftFallback && (
              <span className="badge bg-warning text-dark ms-2">Draft form</span>
            )}
          </p>
        </div>
        <button
          type="button"
          className="btn btn-outline-secondary"
          onClick={() => navigate(-1)}
        >
          <i className="fa fa-chevron-left me-1" /> Back
        </button>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-body">
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
        </div>
      </div>
    </div>
  );
}
