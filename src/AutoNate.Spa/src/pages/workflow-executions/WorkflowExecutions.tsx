import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBusConnection } from "@/hooks/useBusConnection";
import {
  EXECUTIONS_QUERY_KEY,
  executionDiagramQueryKey,
  executionTasksQueryKey,
  useCancelExecution,
  useExecutionDiagram,
  useExecutionTasks,
  useExecutions,
  useDeleteExecution,
  useForceCompleteTask
} from "@/hooks/useExecutions";
import {
  ContextMenuActiveTask,
  useBpmnReadonlyViewer
} from "@/hooks/useBpmnReadonlyViewer";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { usePermissionChecks, permissionKey } from "@/hooks/usePermissionChecks";
import { getCompletedAssigneesForActivity } from "@/api/executions";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { FlowableTaskSummary, WorkflowExecutionSummary } from "@/types/flowable";
import ProcessVariablesPanel from "./ProcessVariablesPanel";
import "./WorkflowExecutions.css";

const WORKFLOW_EXECUTION_TOPIC_PREFIX = "workflow.execution";

export default function WorkflowExecutions() {
  const qc = useQueryClient();
  const { data: executions = [], isLoading, error, refetch } = useExecutions();
  const { data: statusAppearance = [] } = useStatusAppearance();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const deleteExecution = useDeleteExecution();
  const cancelExecution = useCancelExecution();

  // Batched cancel-permission lookups, one per row.
  const cancelChecks = useMemo(
    () =>
      executions.map((execution) => ({
        kind: "workflowexecution",
        action: "cancel",
        id: execution.id
      })),
    [executions]
  );
  const { data: cancelPermissions } = usePermissionChecks(cancelChecks);

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

  const onCancel = async (execution: WorkflowExecutionSummary) => {
    const confirmed = window.confirm(
      `Cancel workflow execution '${execution.id}'? It will stop and be marked as cancelled.`
    );
    if (!confirmed) return;

    try {
      await cancelExecution.mutateAsync(execution.id);
      setFlash({ kind: "success", message: `Execution '${execution.id}' was cancelled.` });
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
                {executions.map((execution) => {
                  const canCancel =
                    cancelPermissions?.get(
                      permissionKey({ kind: "workflowexecution", action: "cancel", id: execution.id })
                    ) ?? false;
                  const isRunning = execution.status === "Running";
                  const cancelInFlight =
                    cancelExecution.isPending && cancelExecution.variables === execution.id;

                  return (
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
                        <span
                          className="badge rounded-pill"
                          style={statusBadgeStyle(execution.status, statusAppearance)}
                        >
                          {execution.status}
                        </span>
                      </td>
                      <td>{execution.currentStep ?? "Not running"}</td>
                      <td className="workflow-executions-actions-cell">
                        {isRunning && canCancel && (
                          <button
                            type="button"
                            className="btn btn-outline-warning btn-sm workflow-execution-cancel-button"
                            title="Cancel execution"
                            aria-label={`Cancel execution ${execution.id}`}
                            onClick={(e) => {
                              e.stopPropagation();
                              onCancel(execution);
                            }}
                            disabled={cancelInFlight}
                          >
                            <span>Cancel</span>
                          </button>
                        )}
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
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedId && (
        <div
          className="workflow-execution-modal-backdrop"
          onClick={() => setSelectedId(null)}
        >
          <div
            className="workflow-execution-modal"
            role="dialog"
            aria-modal="true"
            onClick={(e) => e.stopPropagation()}
          >
            <ExecutionContent
              processInstanceId={selectedId}
              onClose={() => setSelectedId(null)}
              onTaskCompleted={(message) => setFlash({ kind: "success", message })}
              onError={(message) => setFlash({ kind: "error", message })}
            />
          </div>
        </div>
      )}
    </>
  );
}

type ExecutionContentProps = {
  processInstanceId: string;
  onClose?: () => void;
  onTaskCompleted: (message: string) => void;
  onError: (message: string) => void;
};

export function ExecutionContent({
  processInstanceId,
  onClose,
  onTaskCompleted,
  onError
}: ExecutionContentProps) {
  const { data: detail, isLoading: detailLoading, error } = useExecutionDiagram(processInstanceId);
  const { data: tasks = [] } = useExecutionTasks(processInstanceId);
  const forceCompleteTask = useForceCompleteTask(processInstanceId);

  const overrideCheck = useMemo(
    () => [{ kind: "workflowexecution", action: "override", id: processInstanceId }],
    [processInstanceId]
  );
  const { data: permissions } = usePermissionChecks(overrideCheck);
  const canOverride = permissions?.get(permissionKey(overrideCheck[0])) ?? false;

  const onCompleteTaskFromContextMenu = useCallback(
    async (activityId: string, _activityName: string | null, taskId: string) => {
      try {
        await forceCompleteTask.mutateAsync({ taskId });
        onTaskCompleted(`Override-completed task at step '${activityId}'.`);
      } catch (err) {
        onError(describeError(err));
      }
    },
    [forceCompleteTask, onError, onTaskCompleted]
  );

  const onCompleteAllTasksFromContextMenu = useCallback(
    async (activityId: string, _activityName: string | null, taskIds: string[]) => {
      try {
        for (const id of taskIds) {
          await forceCompleteTask.mutateAsync({ taskId: id });
        }
        onTaskCompleted(`Override-completed ${taskIds.length} tasks at step '${activityId}'.`);
      } catch (err) {
        onError(describeError(err));
      }
    },
    [forceCompleteTask, onError, onTaskCompleted]
  );

  const viewerCallbacks = useMemo(
    () => ({
      CompleteTaskFromContextMenu: onCompleteTaskFromContextMenu,
      CompleteAllTasksFromContextMenu: onCompleteAllTasksFromContextMenu
    }),
    [onCompleteTaskFromContextMenu, onCompleteAllTasksFromContextMenu]
  );

  const contextMenuOptions = useMemo(
    () => ({
      getCanOverride: () => canOverride,
      getActiveTasksAtActivity: (
        activityId: string,
        activityName: string | null
      ): ContextMenuActiveTask[] =>
        resolveTasksForActivity(tasks, activityId, activityName).map((t) => ({
          id: t.id,
          assignee: t.assignee
        })),
      getCompletedAssignees: (activityId: string) =>
        getCompletedAssigneesForActivity(processInstanceId, activityId)
    }),
    [canOverride, tasks, processInstanceId]
  );

  const { containerRef, error: viewerError } = useBpmnReadonlyViewer({
    xml: detail?.bpmnXml ?? null,
    completedActivityIds: detail?.completedActivityIds ?? [],
    currentActivityIds: detail?.currentActivityIds ?? [],
    cancelledActivityIds: detail?.cancelledActivityIds ?? [],
    callbacks: viewerCallbacks,
    enableContextMenu: true,
    contextMenu: contextMenuOptions
  });

  const hasCancelledActivities = (detail?.cancelledActivityIds?.length ?? 0) > 0;

  const errorMessage = error
    ? describeError(error)
    : viewerError
      ? `The execution diagram could not be rendered. ${viewerError.message}`
      : null;

  return (
    <>
      <div className="workflow-execution-modal-header">
        <div>
          <h2>Execution {processInstanceId}</h2>
          <p className="workflow-execution-modal-copy">
            Read-only execution view with completed, current, and future steps highlighted.
            {canOverride && " Right-click an active step to override-complete its task."}
          </p>
        </div>
        {onClose && (
          <button type="button" className="btn btn-outline-secondary" onClick={onClose} title="Close">
            Close
          </button>
        )}
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
            {hasCancelledActivities && (
              <span>
                <span className="workflow-execution-swatch workflow-execution-swatch-cancelled"></span>{" "}
                Cancelled
              </span>
            )}
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

            <ProcessVariablesPanel
              processInstanceId={processInstanceId}
              variables={detail.variables}
              canOverride={canOverride}
              onError={onError}
              onSaved={() => onTaskCompleted("Process variables updated.")}
            />
          </div>
        </>
      ) : null}
    </>
  );
}

// Returns all runtime tasks at this BPMN activity. For parallel multi-instance
// user tasks Flowable returns one task row per assignee, and the override-
// complete UX needs the whole set so it can offer "complete all" or
// "complete for…" submenu options.
//
// Fallbacks mirror the original single-task resolver: prefer
// taskDefinitionKey, fall back to display-name match (some legacy deployments
// key tasks differently than the BPMN xml id), and finally — when there's
// only one active runtime task in the whole process — assume it belongs to
// the clicked node. The single-task fallback is what keeps the menu usable
// against simple linear workflows where Flowable's task key may not surface
// on the BPMN side.
function resolveTasksForActivity(
  tasks: readonly FlowableTaskSummary[],
  activityId: string,
  activityName: string | null
): FlowableTaskSummary[] {
  const byDefinition = tasks.filter((t) => t.taskDefinitionKey === activityId);
  if (byDefinition.length > 0) return byDefinition;

  if (activityName) {
    const byName = tasks.filter((t) => t.name === activityName);
    if (byName.length > 0) return byName;
  }

  return tasks.length === 1 ? [tasks[0]] : [];
}

function statusBadgeStyle(
  status: string,
  entries: StatusAppearanceEntry[]
): React.CSSProperties {
  const backgroundColor = resolveStatusBadgeColor(status, entries);
  return {
    backgroundColor,
    color: badgeTextColor(backgroundColor)
  };
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

export function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
