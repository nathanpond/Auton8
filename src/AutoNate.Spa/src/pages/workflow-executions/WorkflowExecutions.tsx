import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBusConnection } from "@/hooks/useBusConnection";
import {
  EXECUTIONS_QUERY_KEY,
  executionDiagramQueryKey,
  executionTasksQueryKey,
  useCompleteTask,
  useDeleteExecution,
  useExecutionDiagram,
  useExecutionTasks,
  useExecutions
} from "@/hooks/useExecutions";
import { useBpmnReadonlyViewer } from "@/hooks/useBpmnReadonlyViewer";
import { FlowableTaskSummary, WorkflowExecutionSummary } from "@/types/flowable";
import "./WorkflowExecutions.css";

const WORKFLOW_EXECUTION_TOPIC_PREFIX = "workflow.execution";

export default function WorkflowExecutions() {
  const qc = useQueryClient();
  const { data: executions = [], isLoading, error, refetch } = useExecutions();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const deleteExecution = useDeleteExecution();

  const onBusMessage = useCallback(
    (msg: { topic: string; payload: string }) => {
      if (!msg.topic?.startsWith(WORKFLOW_EXECUTION_TOPIC_PREFIX)) {
        return;
      }

      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      if (selectedId) {
        qc.invalidateQueries({ queryKey: executionDiagramQueryKey(selectedId) });
        qc.invalidateQueries({ queryKey: executionTasksQueryKey(selectedId) });
      }
    },
    [qc, selectedId]
  );

  const { status: busStatus } = useBusConnection({ onMessage: onBusMessage });

  const onDelete = async (execution: WorkflowExecutionSummary) => {
    const confirmed = window.confirm(
      `Are you sure you want to delete workflow execution '${execution.id}'? This will permanently remove it from AutoNate and Flowable.`
    );
    if (!confirmed) return;

    try {
      await deleteExecution.mutateAsync(execution.id);
      if (selectedId === execution.id) {
        setSelectedId(null);
      }
      setFlash({ kind: "success", message: `Execution '${execution.id}' was deleted.` });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Workflow Executions</h1>
          <p className="page-head-copy workflow-executions-copy">
            View every execution in the system, with the newest runs first and live step details for
            anything still in progress.
          </p>
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

      {error && (
        <div className="alert alert-danger" role="alert">
          {describeError(error)}
        </div>
      )}

      <div className="workflow-executions-toolbar">
        <span className={connectionBadgeClass(busStatus)}>{browserStatusLabel(busStatus)}</span>
        <button
          type="button"
          className="btn btn-outline-primary"
          onClick={() => refetch()}
          disabled={isLoading}
          title="Refresh executions"
        >
          Refresh
        </button>
        {isLoading && <span className="workflow-executions-loading">Loading executions...</span>}
      </div>

      <div className="workflow-executions-card">
        {executions.length === 0 && !isLoading ? (
          <p className="workflow-executions-empty">No workflow executions have been recorded yet.</p>
        ) : (
          <div className="table-responsive">
            <table className="table table-hover align-middle workflow-executions-table">
              <thead>
                <tr>
                  <th scope="col">Execution ID</th>
                  <th scope="col">Workflow Model</th>
                  <th scope="col">Started</th>
                  <th scope="col">Last Activity Date</th>
                  <th scope="col">Status</th>
                  <th scope="col">Current Step</th>
                  <th scope="col" className="workflow-executions-actions-header">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {executions.map((execution) => (
                  <tr
                    key={execution.id}
                    className="workflow-execution-row"
                    onClick={() => setSelectedId(execution.id)}
                  >
                    <td className="workflow-execution-id">{execution.id}</td>
                    <td>{execution.workflowModelName ?? "Unknown"}</td>
                    <td>{formatTimestamp(execution.startedAtUtc)}</td>
                    <td>{formatTimestamp(execution.lastActivityAtUtc)}</td>
                    <td>
                      <span className={statusBadgeClass(execution.status)}>{execution.status}</span>
                    </td>
                    <td>{execution.currentStep ?? "Not running"}</td>
                    <td className="workflow-executions-actions-cell">
                      <button
                        type="button"
                        className="btn btn-outline-danger btn-sm workflow-execution-delete-button"
                        title="Delete execution"
                        aria-label={`Delete execution ${execution.id}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          onDelete(execution);
                        }}
                        disabled={deleteExecution.isPending && deleteExecution.variables === execution.id}
                      >
                        <span>Delete</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedId && (
        <ExecutionModal
          processInstanceId={selectedId}
          onClose={() => setSelectedId(null)}
          onTaskCompleted={(message) => setFlash({ kind: "success", message })}
          onError={(message) => setFlash({ kind: "error", message })}
        />
      )}
    </>
  );
}

type ExecutionModalProps = {
  processInstanceId: string;
  onClose: () => void;
  onTaskCompleted: (message: string) => void;
  onError: (message: string) => void;
};

function ExecutionModal({ processInstanceId, onClose, onTaskCompleted, onError }: ExecutionModalProps) {
  const { data: detail, isLoading: detailLoading, error } = useExecutionDiagram(processInstanceId);
  const { data: tasks = [] } = useExecutionTasks(processInstanceId);
  const completeTask = useCompleteTask();

  const onCompleteTaskFromContextMenu = useCallback(
    async (activityId: string, activityName: string | null) => {
      const task = resolveTaskForActivity(tasks, activityId, activityName);
      if (!task) {
        onError(`No active runtime task matched workflow step '${activityName || activityId}'.`);
        return;
      }

      try {
        await completeTask.mutateAsync({ taskId: task.id });
        onTaskCompleted(`Completed task '${task.name}'.`);
      } catch (err) {
        onError(describeError(err));
      }
    },
    [tasks, completeTask, onError, onTaskCompleted]
  );

  const viewerCallbacks = useMemo(
    () => ({ CompleteTaskFromContextMenu: onCompleteTaskFromContextMenu }),
    [onCompleteTaskFromContextMenu]
  );

  const { containerRef, error: viewerError } = useBpmnReadonlyViewer({
    xml: detail?.bpmnXml ?? null,
    completedActivityIds: detail?.completedActivityIds ?? [],
    currentActivityIds: detail?.currentActivityIds ?? [],
    callbacks: viewerCallbacks,
    enableContextMenu: true
  });

  const errorMessage = error
    ? describeError(error)
    : viewerError
      ? `The execution diagram could not be rendered. ${viewerError.message}`
      : null;

  return (
    <div className="workflow-execution-modal-backdrop" onClick={onClose}>
      <div
        className="workflow-execution-modal"
        role="dialog"
        aria-modal="true"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="workflow-execution-modal-header">
          <div>
            <h2>Execution {processInstanceId}</h2>
            <p className="workflow-execution-modal-copy">
              Read-only execution view with completed, current, and future steps highlighted.
              Right-click the active step to complete its task.
            </p>
          </div>
          <button type="button" className="btn btn-outline-secondary" onClick={onClose} title="Close">
            Close
          </button>
        </div>

        {errorMessage && (
          <div className="alert alert-danger" role="alert">
            {errorMessage}
          </div>
        )}

        {detailLoading ? (
          <p className="workflow-executions-loading">Loading execution diagram...</p>
        ) : detail ? (
          <>
            <div className="workflow-execution-legend">
              <span>
                <span className="workflow-execution-swatch workflow-execution-swatch-completed"></span>{" "}
                Completed
              </span>
              <span>
                <span className="workflow-execution-swatch workflow-execution-swatch-current"></span>{" "}
                Current
              </span>
              <span>
                <span className="workflow-execution-swatch workflow-execution-swatch-future"></span>{" "}
                Future
              </span>
            </div>

            <div className="workflow-execution-body">
              <div className="workflow-execution-viewer-shell">
                <div
                  ref={containerRef}
                  className="workflow-execution-viewer-canvas"
                  aria-label="Read-only BPMN execution diagram"
                ></div>
              </div>

              <aside className="workflow-execution-variables-panel" aria-label="Process variables">
                <h3 className="workflow-execution-variables-title">Process Variables</h3>
                {detail.variables.length === 0 ? (
                  <p className="workflow-execution-variables-empty">
                    No variables have been set on this execution.
                  </p>
                ) : (
                  <ul className="workflow-execution-variables-list">
                    {detail.variables.map((variable) => (
                      <li key={variable.name} className="workflow-execution-variable">
                        <div className="workflow-execution-variable-header">
                          <span className="workflow-execution-variable-name">{variable.name}</span>
                          {variable.type && (
                            <span className="workflow-execution-variable-type">{variable.type}</span>
                          )}
                        </div>
                        <div className="workflow-execution-variable-value">
                          {variable.value ?? "null"}
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </aside>
            </div>
          </>
        ) : null}
      </div>
    </div>
  );
}

function resolveTaskForActivity(
  tasks: readonly FlowableTaskSummary[],
  activityId: string,
  activityName: string | null
): FlowableTaskSummary | null {
  const byDefinition = tasks.find((t) => t.taskDefinitionKey === activityId);
  if (byDefinition) return byDefinition;

  if (activityName) {
    const byName = tasks.find((t) => t.name === activityName);
    if (byName) return byName;
  }

  return tasks.length === 1 ? tasks[0] : null;
}

function statusBadgeClass(status: string): string {
  const lower = status.toLowerCase();
  if (lower === "running") return "badge text-bg-primary";
  if (lower === "failed" || lower === "cancelled") return "badge text-bg-danger";
  return "badge text-bg-success";
}

function connectionBadgeClass(status: string): string {
  const lower = status.toLowerCase();
  if (lower.includes("connected")) return "badge text-bg-success";
  if (lower.includes("connecting") || lower.includes("reconnecting")) return "badge text-bg-warning";
  return "badge text-bg-danger";
}

function browserStatusLabel(status: string): string {
  if (status.toLowerCase().includes("connected")) return "Browser stream connected";
  if (status.toLowerCase().includes("connecting")) return "Browser stream connecting...";
  if (status.toLowerCase().includes("reconnecting")) return "Browser stream reconnecting...";
  return `Browser stream ${status.toLowerCase()}`;
}

function formatTimestamp(iso: string | null): string {
  if (!iso) return "Not available";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
