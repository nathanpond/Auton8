import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBusConnection } from "@/hooks/useBusConnection";
import {
  EXECUTIONS_QUERY_KEY,
  executionDiagramQueryKey,
  executionHistoryQueryKey,
  executionLogQueryKey,
  executionTasksQueryKey,
  useCancelExecution,
  useExecutionDiagram,
  useExecutionHistory,
  useExecutionTasks,
  useExecutions,
  useDeleteAllExecutions,
  useDeleteExecution,
  useForceCompleteTask,
  useMoveExecutionState,
  useReassignTask,
  useUpdateTaskDueDate
} from "@/hooks/useExecutions";
import {
  ContextMenuActiveTask,
  UserTaskHoverInfo,
  useBpmnReadonlyViewer
} from "@/hooks/useBpmnReadonlyViewer";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { usePermissionChecks, permissionKey } from "@/hooks/usePermissionChecks";
import { getCompletedAssigneesForActivity } from "@/api/executions";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import {
  FlowableTaskSummary,
  WorkflowExecutionHistoryEvent,
  WorkflowExecutionSummary
} from "@/types/flowable";
import ConfirmModal from "@/components/ConfirmModal";
import ChangeDueDateModal from "./ChangeDueDateModal";
import ExecutionHistory from "./ExecutionHistory";
import ExecutionLog from "./ExecutionLog";
import ProcessVariablesPanel from "./ProcessVariablesPanel";
import ReassignTaskModal from "./ReassignTaskModal";
import { describeError as describeErrorUtil, formatTimestamp as formatTimestampUtil } from "./utils";
import "./WorkflowExecutions.css";

const WORKFLOW_EXECUTION_TOPIC_PREFIX = "workflow.execution";

export default function WorkflowExecutions() {
  const qc = useQueryClient();
  const { data: executions = [], isLoading, error, refetch } = useExecutions();
  const { data: statusAppearance = [] } = useStatusAppearance();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [pendingAction, setPendingAction] = useState<
    | { kind: "cancel" | "delete"; execution: WorkflowExecutionSummary }
    | { kind: "delete-all" }
    | null
  >(null);

  const deleteExecution = useDeleteExecution();
  const cancelExecution = useCancelExecution();
  const deleteAllExecutions = useDeleteAllExecutions();

  // Kind-level check for the bulk-delete button. Backend gate uses id="*"
  // (RequireKindPermissionFilter), so the SPA mirrors that.
  const deleteAllCheck = useMemo(
    () => [{ kind: "workflowexecution", action: "deleteall", id: "*" }],
    []
  );
  const { data: deleteAllPermissions } = usePermissionChecks(deleteAllCheck);
  const canDeleteAll =
    deleteAllPermissions?.get(permissionKey(deleteAllCheck[0])) ?? false;

  // Batched per-row permission lookups for the row-action buttons.
  const rowActionChecks = useMemo(
    () =>
      executions.flatMap((execution) => [
        { kind: "workflowexecution", action: "cancel", id: execution.id },
        { kind: "workflowexecution", action: "delete", id: execution.id }
      ]),
    [executions]
  );
  const { data: rowActionPermissions } = usePermissionChecks(rowActionChecks);

  const onBusMessage = useCallback(
    (msg: { topic: string; payload: string }) => {
      if (!msg.topic?.startsWith(WORKFLOW_EXECUTION_TOPIC_PREFIX)) {
        return;
      }

      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      if (selectedId) {
        qc.invalidateQueries({ queryKey: executionDiagramQueryKey(selectedId) });
        qc.invalidateQueries({ queryKey: executionHistoryQueryKey(selectedId) });
        qc.invalidateQueries({ queryKey: executionLogQueryKey(selectedId) });
        qc.invalidateQueries({ queryKey: executionTasksQueryKey(selectedId) });
      }
    },
    [qc, selectedId]
  );

  const { status: busStatus } = useBusConnection({ onMessage: onBusMessage });

  const requestDelete = (execution: WorkflowExecutionSummary) => {
    setPendingAction({ kind: "delete", execution });
  };

  const requestCancel = (execution: WorkflowExecutionSummary) => {
    setPendingAction({ kind: "cancel", execution });
  };

  const requestDeleteAll = () => {
    setPendingAction({ kind: "delete-all" });
  };

  const confirmPendingAction = async () => {
    if (!pendingAction) return;

    try {
      if (pendingAction.kind === "delete-all") {
        const { deleted } = await deleteAllExecutions.mutateAsync();
        setSelectedId(null);
        setFlash({
          kind: "success",
          message: deleted === 0
            ? "There were no executions to delete."
            : `Deleted ${deleted} execution${deleted === 1 ? "" : "s"} from AutoNate and Flowable.`
        });
      } else if (pendingAction.kind === "delete") {
        const { execution } = pendingAction;
        await deleteExecution.mutateAsync(execution.id);
        if (selectedId === execution.id) {
          setSelectedId(null);
        }
        setFlash({ kind: "success", message: `Execution '${execution.id}' was deleted.` });
      } else {
        const { execution } = pendingAction;
        await cancelExecution.mutateAsync(execution.id);
        setFlash({ kind: "success", message: `Execution '${execution.id}' was cancelled.` });
      }
      setPendingAction(null);
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
      setPendingAction(null);
    }
  };

  const pendingActionInFlight =
    (pendingAction?.kind === "delete" && deleteExecution.isPending) ||
    (pendingAction?.kind === "cancel" && cancelExecution.isPending) ||
    (pendingAction?.kind === "delete-all" && deleteAllExecutions.isPending);

  const statusCounts = useMemo(() => {
    const counts = { running: 0, complete: 0, cancelled: 0, errored: 0 };
    for (const e of executions) {
      if (e.status === "Running") counts.running++;
      else if (e.status === "Complete") counts.complete++;
      else if (e.status === "Cancelled") counts.cancelled++;
      else if (e.status === "Errored") counts.errored++;
    }
    return counts;
  }, [executions]);

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

      <div className="row g-3 mb-4">
        <StatusStatCard
          color="bg-blue"
          icon="fa-circle-play"
          title="RUNNING"
          count={statusCounts.running}
        />
        <StatusStatCard
          color="bg-teal"
          icon="fa-circle-check"
          title="COMPLETED"
          count={statusCounts.complete}
        />
        <StatusStatCard
          color="workflow-executions-stat-cancelled"
          icon="fa-ban"
          title="CANCELLED"
          count={statusCounts.cancelled}
        />
        <StatusStatCard
          color="bg-red"
          icon="fa-triangle-exclamation"
          title="ERRORED"
          count={statusCounts.errored}
        />
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
        {canDeleteAll && (
          <button
            type="button"
            className="btn btn-outline-danger ms-auto workflow-executions-delete-all-button"
            onClick={requestDeleteAll}
            disabled={deleteAllExecutions.isPending || executions.length === 0}
            title="Delete every execution from AutoNate and Flowable"
          >
            Delete All Executions
          </button>
        )}
      </div>

      <div className="workflow-executions-card">
        {executions.length === 0 && !isLoading ? (
          <p className="workflow-executions-empty">No workflow executions have been recorded yet.</p>
        ) : (
          <div className="table-responsive">
            <table className="table table-hover align-middle workflow-executions-table">
              <thead>
                <tr>
                  <th scope="col">Execution</th>
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
                    rowActionPermissions?.get(
                      permissionKey({ kind: "workflowexecution", action: "cancel", id: execution.id })
                    ) ?? false;
                  const canDelete =
                    rowActionPermissions?.get(
                      permissionKey({ kind: "workflowexecution", action: "delete", id: execution.id })
                    ) ?? false;
                  const isRunning = execution.status === "Running";
                  const cancelInFlight =
                    cancelExecution.isPending && cancelExecution.variables === execution.id;

                  const displayName = execution.name ?? execution.id;
                  return (
                    <tr
                      key={execution.id}
                      className="workflow-execution-row"
                      onClick={() => setSelectedId(execution.id)}
                    >
                      <td>
                        <div className="workflow-execution-name">{displayName}</div>
                        {execution.name && (
                          <div className="workflow-execution-id workflow-execution-id-secondary">
                            {execution.id}
                          </div>
                        )}
                      </td>
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
                            aria-label={`Cancel execution ${displayName}`}
                            onClick={(e) => {
                              e.stopPropagation();
                              requestCancel(execution);
                            }}
                            disabled={cancelInFlight}
                          >
                            <span>Cancel</span>
                          </button>
                        )}
                        {canDelete && (
                          <button
                            type="button"
                            className="btn btn-outline-danger btn-sm workflow-execution-delete-button"
                            title="Delete execution"
                            aria-label={`Delete execution ${displayName}`}
                            onClick={(e) => {
                              e.stopPropagation();
                              requestDelete(execution);
                            }}
                            disabled={deleteExecution.isPending && deleteExecution.variables === execution.id}
                          >
                            <span>Delete</span>
                          </button>
                        )}
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

      {pendingAction && (() => {
        if (pendingAction.kind === "delete-all") {
          return (
            <ConfirmModal
              title="Delete every execution?"
              message={
                <p className="mb-0">
                  Permanently delete <strong>every workflow execution</strong>{" "}
                  in the system? This removes all runs from AutoNate and
                  Flowable — running and historical, including variables and
                  tasks. Workflow models stay published.
                </p>
              }
              confirmLabel="Delete all"
              cancelLabel="Keep"
              variant="danger"
              busy={pendingActionInFlight}
              onConfirm={confirmPendingAction}
              onCancel={() => setPendingAction(null)}
            />
          );
        }

        const label = pendingAction.execution.name ?? pendingAction.execution.id;
        return (
          <ConfirmModal
            title={pendingAction.kind === "delete" ? "Delete execution?" : "Cancel execution?"}
            message={
              pendingAction.kind === "delete" ? (
                <p className="mb-0">
                  Permanently delete workflow execution{" "}
                  <strong>{label}</strong>? This removes it from AutoNate and
                  Flowable — the run, its history, variables, and tasks will
                  all be gone.
                </p>
              ) : (
                <p className="mb-0">
                  Cancel workflow execution <strong>{label}</strong>? Execution
                  stops immediately and the run is marked as cancelled. The
                  history is kept.
                </p>
              )
            }
            confirmLabel={pendingAction.kind === "delete" ? "Delete" : "Cancel execution"}
            cancelLabel="Keep"
            variant={pendingAction.kind === "delete" ? "danger" : "warning"}
            busy={pendingActionInFlight}
            onConfirm={confirmPendingAction}
            onCancel={() => setPendingAction(null)}
          />
        );
      })()}
    </>
  );
}

type ExecutionContentProps = {
  processInstanceId: string;
  onClose?: () => void;
  onTaskCompleted: (message: string) => void;
  onError: (message: string) => void;
};

type ExecutionTab = "diagram" | "history" | "log";

export function ExecutionContent({
  processInstanceId,
  onClose,
  onTaskCompleted,
  onError
}: ExecutionContentProps) {
  const [tab, setTab] = useState<ExecutionTab>("diagram");
  const { data: detail, isLoading: detailLoading, error } = useExecutionDiagram(processInstanceId);
  const { data: tasks = [] } = useExecutionTasks(processInstanceId);
  // Lifted from the History tab so the diagram-tab hover tooltip can show
  // assignees on user-task nodes that have already completed without waiting
  // for the user to click into History first.
  const { data: history = [] } = useExecutionHistory(processInstanceId);
  const directory = useUserDirectory();
  const forceCompleteTask = useForceCompleteTask(processInstanceId);
  const reassignTask = useReassignTask(processInstanceId);
  const updateTaskDueDate = useUpdateTaskDueDate(processInstanceId);
  const moveExecutionState = useMoveExecutionState(processInstanceId);

  // Set when the operator picks "Move Execution Here" from the context menu.
  // The confirmation modal renders off this and clears it on confirm/cancel.
  const [pendingMove, setPendingMove] = useState<
    | { activityId: string; activityName: string | null }
    | null
  >(null);

  // The reassign and due-date pickers need their own state because the BPMN
  // context menu fires-and-forgets — it can't render React UI itself, so it
  // hands the chosen task off to us and we open the modal here.
  const [pendingTaskEdit, setPendingTaskEdit] = useState<
    | { kind: "reassign"; taskId: string; taskLabel: string; currentAssignee: string | null }
    | { kind: "due-date"; taskId: string; taskLabel: string; currentDueDate: string | null }
    | null
  >(null);

  const adminChecks = useMemo(
    () => [
      { kind: "workflowexecution", action: "override", id: processInstanceId },
      { kind: "workflowexecution", action: "movestate", id: processInstanceId }
    ],
    [processInstanceId]
  );
  const { data: permissions } = usePermissionChecks(adminChecks);
  const canOverride = permissions?.get(permissionKey(adminChecks[0])) ?? false;
  const canMoveState = permissions?.get(permissionKey(adminChecks[1])) ?? false;
  // Move-here is only meaningful while the run is in flight — once there are
  // no current activity tokens, Flowable's change-state has nothing to cancel.
  const isRunning = (detail?.currentActivityIds?.length ?? 0) > 0;

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

  const onReassignTaskFromContextMenu = useCallback(
    (
      activityId: string,
      activityName: string | null,
      taskId: string,
      currentAssignee: string | null
    ) => {
      setPendingTaskEdit({
        kind: "reassign",
        taskId,
        taskLabel: activityName ?? activityId,
        currentAssignee
      });
    },
    []
  );

  const onChangeDueDateFromContextMenu = useCallback(
    (
      activityId: string,
      activityName: string | null,
      taskId: string,
      currentDueDate: string | null
    ) => {
      setPendingTaskEdit({
        kind: "due-date",
        taskId,
        taskLabel: activityName ?? activityId,
        currentDueDate
      });
    },
    []
  );

  const onMoveExecutionHereFromContextMenu = useCallback(
    (activityId: string, activityName: string | null) => {
      setPendingMove({ activityId, activityName });
    },
    []
  );

  const viewerCallbacks = useMemo(
    () => ({
      CompleteTaskFromContextMenu: onCompleteTaskFromContextMenu,
      CompleteAllTasksFromContextMenu: onCompleteAllTasksFromContextMenu,
      ReassignTaskFromContextMenu: onReassignTaskFromContextMenu,
      ChangeDueDateFromContextMenu: onChangeDueDateFromContextMenu,
      MoveExecutionHereFromContextMenu: onMoveExecutionHereFromContextMenu
    }),
    [
      onCompleteTaskFromContextMenu,
      onCompleteAllTasksFromContextMenu,
      onReassignTaskFromContextMenu,
      onChangeDueDateFromContextMenu,
      onMoveExecutionHereFromContextMenu
    ]
  );

  const contextMenuOptions = useMemo(
    () => ({
      getCanOverride: () => canOverride,
      getCanMoveState: () => canMoveState && isRunning,
      getActiveTasksAtActivity: (
        activityId: string,
        activityName: string | null
      ): ContextMenuActiveTask[] =>
        resolveTasksForActivity(tasks, activityId, activityName).map((t) => ({
          id: t.id,
          assignee: t.assignee,
          dueDate: t.dueDate
        })),
      getCompletedAssignees: (activityId: string) =>
        getCompletedAssigneesForActivity(processInstanceId, activityId)
    }),
    [canOverride, canMoveState, isRunning, tasks, processInstanceId]
  );

  const resolveAssigneeLabel = useCallback(
    (raw: string | null | undefined): string => {
      if (!raw) return "Unassigned";
      // Flowable's assignee fields can hold a literal user id, a username, or
      // an unevaluated expression like ${initiator}. Pass expressions through
      // verbatim — the runtime hasn't resolved them yet — and otherwise try
      // the user directory before falling back to the raw value.
      if (raw.startsWith("${")) return raw;
      const u = directory.get(raw);
      return userDisplayName(u) ?? raw;
    },
    [directory]
  );

  const hoverTooltipOptions = useMemo(
    () => ({
      getInfo: (
        activityId: string,
        activityName: string | null,
        bpmn: { assignee: string | null; dueDate: string | null }
      ): UserTaskHoverInfo | null =>
        buildActivityHoverInfo({
          activityId,
          activityName,
          bpmn,
          tasks,
          history,
          errorMessagesByActivityId: detail?.errorMessagesByActivityId ?? {},
          resolveAssigneeLabel
        })
    }),
    [tasks, history, detail?.errorMessagesByActivityId, resolveAssigneeLabel]
  );

  const { containerRef, error: viewerError } = useBpmnReadonlyViewer({
    xml: detail?.bpmnXml ?? null,
    completedActivityIds: detail?.completedActivityIds ?? [],
    currentActivityIds: detail?.currentActivityIds ?? [],
    cancelledActivityIds: detail?.cancelledActivityIds ?? [],
    failedActivityIds: detail?.failedActivityIds ?? [],
    callbacks: viewerCallbacks,
    enableContextMenu: true,
    contextMenu: contextMenuOptions,
    enableHoverTooltip: true,
    hoverTooltip: hoverTooltipOptions
  });

  const hasCancelledActivities = (detail?.cancelledActivityIds?.length ?? 0) > 0;
  const hasFailedActivities = (detail?.failedActivityIds?.length ?? 0) > 0;

  const errorMessage = error
    ? describeError(error)
    : viewerError
      ? `The execution diagram could not be rendered. ${viewerError.message}`
      : null;

  return (
    <>
      <div className="workflow-execution-modal-header">
        <h2>{detail?.name ?? `Execution ${processInstanceId}`}</h2>
        {onClose && (
          <button type="button" className="btn btn-outline-secondary" onClick={onClose} title="Close">
            Close
          </button>
        )}
      </div>

      <ul className="nav nav-tabs workflow-execution-modal-tabs">
        <li className="nav-item">
          <a
            href="#workflow-execution-diagram-tab"
            onClick={(e) => {
              e.preventDefault();
              setTab("diagram");
            }}
            className={`nav-link ${tab === "diagram" ? "active" : ""}`}
          >
            Diagram
          </a>
        </li>
        <li className="nav-item">
          <a
            href="#workflow-execution-history-tab"
            onClick={(e) => {
              e.preventDefault();
              setTab("history");
            }}
            className={`nav-link ${tab === "history" ? "active" : ""}`}
          >
            History
          </a>
        </li>
        <li className="nav-item">
          <a
            href="#workflow-execution-log-tab"
            onClick={(e) => {
              e.preventDefault();
              setTab("log");
            }}
            className={`nav-link ${tab === "log" ? "active" : ""}`}
          >
            Execution Log
          </a>
        </li>
      </ul>

      <div className="workflow-execution-modal-body">
        <div className="tab-content panel rounded-0 p-3 m-0">
          {/* Diagram pane stays mounted so the BPMN viewer doesn't
              reinitialize on every tab switch — Bootstrap's tab-pane
              CSS hides the inactive pane via display:none. */}
          <div
            id="workflow-execution-diagram-tab"
            className={`tab-pane fade ${tab === "diagram" ? "active show" : ""}`}
          >
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
                  {hasFailedActivities && (
                    <span>
                      <span className="workflow-execution-swatch workflow-execution-swatch-failed"></span>{" "}
                      Errored
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
          </div>

          <div
            id="workflow-execution-history-tab"
            className={`tab-pane fade ${tab === "history" ? "active show" : ""}`}
          >
            {tab === "history" && <ExecutionHistory processInstanceId={processInstanceId} />}
          </div>

          <div
            id="workflow-execution-log-tab"
            className={`tab-pane fade ${tab === "log" ? "active show" : ""}`}
          >
            {tab === "log" && <ExecutionLog processInstanceId={processInstanceId} />}
          </div>
        </div>
      </div>

      {pendingTaskEdit?.kind === "reassign" && (
        <ReassignTaskModal
          taskLabel={pendingTaskEdit.taskLabel}
          currentAssignee={pendingTaskEdit.currentAssignee}
          busy={reassignTask.isPending}
          onCancel={() => setPendingTaskEdit(null)}
          onConfirm={async (assignee) => {
            try {
              await reassignTask.mutateAsync({
                taskId: pendingTaskEdit.taskId,
                assignee
              });
              setPendingTaskEdit(null);
              onTaskCompleted(`Reassigned task at step '${pendingTaskEdit.taskLabel}'.`);
            } catch (err) {
              setPendingTaskEdit(null);
              onError(describeError(err));
            }
          }}
        />
      )}

      {pendingTaskEdit?.kind === "due-date" && (
        <ChangeDueDateModal
          taskLabel={pendingTaskEdit.taskLabel}
          currentDueDate={pendingTaskEdit.currentDueDate}
          busy={updateTaskDueDate.isPending}
          onCancel={() => setPendingTaskEdit(null)}
          onConfirm={async (dueDateIso) => {
            try {
              await updateTaskDueDate.mutateAsync({
                taskId: pendingTaskEdit.taskId,
                dueDate: dueDateIso
              });
              setPendingTaskEdit(null);
              onTaskCompleted(`Updated due date for task at step '${pendingTaskEdit.taskLabel}'.`);
            } catch (err) {
              setPendingTaskEdit(null);
              onError(describeError(err));
            }
          }}
        />
      )}

      {pendingMove && (
        <ConfirmModal
          title="Move execution to this step?"
          message={
            <>
              <p>
                Move the running execution to{" "}
                <strong>{pendingMove.activityName ?? pendingMove.activityId}</strong>?
              </p>
              <p className="mb-0">
                Every currently active step on this run will be cancelled and
                a fresh execution token will start at the selected node. Process
                variables are preserved, but the new step may depend on values
                that aren't set yet — review variables after moving and adjust
                them as needed before this run continues.
              </p>
            </>
          }
          confirmLabel="Move execution"
          cancelLabel="Keep"
          variant="warning"
          busy={moveExecutionState.isPending}
          onCancel={() => setPendingMove(null)}
          onConfirm={async () => {
            const { activityId, activityName } = pendingMove;
            try {
              await moveExecutionState.mutateAsync(activityId);
              setPendingMove(null);
              onTaskCompleted(`Moved execution to '${activityName ?? activityId}'.`);
            } catch (err) {
              setPendingMove(null);
              onError(describeError(err));
            }
          }}
        />
      )}
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

type StatusStatCardProps = {
  color: string;
  icon: string;
  title: string;
  count: number;
};

function StatusStatCard({ color, icon, title, count }: StatusStatCardProps) {
  return (
    <div className="col-xl-3 col-md-6">
      <div className={`widget widget-stats workflow-executions-stat ${color}`}>
        <div className="workflow-executions-stat-left">
          <div className="stats-title">{title}</div>
          <div className="stats-icon stats-icon-lg">
            <i className={`fa ${icon} fa-fw`}></i>
          </div>
        </div>
        <div className="stats-number">{count.toLocaleString()}</div>
      </div>
    </div>
  );
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

// Re-exported from ./utils so callers (ExecutionPage, etc.) that already
// imported these from this file keep working without an import-path churn.
export const formatTimestamp = formatTimestampUtil;
export const describeError = describeErrorUtil;

// Builds the data the BPMN hover tooltip shows on a node. Resolution order,
// in priority:
//   1. Errored — the activityId is in errorMessagesByActivityId. Render
//      Status: Errored + Error: <message or "(no message captured)">.
//      Wins over userTask state because an errored node is the most
//      actionable info on the diagram.
//   2. Active runtime tasks (taskDefinitionKey match) — userTask only.
//   3. Most-recent historic record for the activity — userTask only.
//   4. Design-time BPMN attributes — userTask only.
function buildActivityHoverInfo(args: {
  activityId: string;
  activityName: string | null;
  bpmn: { assignee: string | null; dueDate: string | null };
  tasks: readonly FlowableTaskSummary[];
  history: readonly WorkflowExecutionHistoryEvent[];
  errorMessagesByActivityId: Record<string, string>;
  resolveAssigneeLabel: (raw: string | null | undefined) => string;
}): UserTaskHoverInfo {
  const {
    activityId,
    activityName,
    bpmn,
    tasks,
    history,
    errorMessagesByActivityId,
    resolveAssigneeLabel
  } = args;

  const title = activityName ?? activityId;
  const rows: Array<{ label: string; value: string }> = [];

  // 1. Errored branch
  const erroredMessage = Object.prototype.hasOwnProperty.call(errorMessagesByActivityId, activityId)
    ? errorMessagesByActivityId[activityId]
    : null;
  const isErrored = erroredMessage !== null
    || history.some((e) => e.activityId === activityId && e.isErrored === true);
  if (isErrored) {
    rows.push({ label: "Status", value: "Errored" });
    rows.push({
      label: "Error",
      value:
        typeof erroredMessage === "string" && erroredMessage.length > 0
          ? erroredMessage
          : "(no message captured)"
    });
    return { title, rows };
  }

  // 2-4. Existing userTask resolution (unchanged).
  const activeMatches = tasks.filter((t) => t.taskDefinitionKey === activityId);
  if (activeMatches.length > 0) {
    if (activeMatches.length === 1) {
      const t = activeMatches[0];
      rows.push({ label: "Assignee", value: resolveAssigneeLabel(t.assignee) });
      rows.push({ label: "Due", value: formatAbsoluteDueDate(t.dueDate) });
    } else {
      rows.push({
        label: "Assignees",
        value: activeMatches.map((t) => resolveAssigneeLabel(t.assignee)).join(", ")
      });
      const dueValues = activeMatches
        .map((t) => formatAbsoluteDueDate(t.dueDate))
        .filter((v, i, arr) => arr.indexOf(v) === i);
      rows.push({ label: "Due", value: dueValues.join(", ") });
    }
    rows.push({ label: "Status", value: "In progress" });
    return { title, rows };
  }

  const historicMatches = history
    .filter((e) => e.activityId === activityId && e.activityType === "userTask" && e.endedAtUtc)
    .sort((a, b) => (b.endedAtUtc ?? "").localeCompare(a.endedAtUtc ?? ""));
  if (historicMatches.length > 0) {
    const latest = historicMatches[0];
    rows.push({ label: "Assignee", value: resolveAssigneeLabel(latest.assignee) });
    rows.push({ label: "Completed", value: formatAbsoluteDueDate(latest.endedAtUtc) });
    return { title, rows };
  }

  rows.push({ label: "Assignee", value: resolveAssigneeLabel(bpmn.assignee) });
  rows.push({ label: "Due", value: formatBpmnDueDate(bpmn.dueDate) });
  return { title, rows };
}

function formatAbsoluteDueDate(iso: string | null | undefined): string {
  if (!iso) return "No due date";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString();
}

// Renders a BPMN-design-time dueDate string. Recognized shapes, in priority:
//   - AutoNate helper expression "${dueDateHelper.fromProcessStart(execution,
//     N)}" — N days from process start (see DueDateHelper.java in the
//     flowable-extension). Rendered as "+N days from process start".
//   - ISO 8601 duration ("P3D", "PT3H", "P1W") — relative to task start.
//     Rendered as "+3 days from task start".
//   - Plain date/datetime — formatted via toLocaleDateString.
//   - Any other ${expression} — passed through verbatim; the runtime will
//     evaluate it at task creation time.
function formatBpmnDueDate(raw: string | null | undefined): string {
  if (!raw) return "No due date";
  const trimmed = raw.trim();
  if (!trimmed) return "No due date";

  const helperLabel = parseDueDateHelperExpression(trimmed);
  if (helperLabel) return helperLabel;

  const relative = parseIso8601DurationLabel(trimmed);
  if (relative) return `${relative} from task start`;

  if (trimmed.startsWith("${")) return trimmed;

  const d = new Date(trimmed);
  if (!Number.isNaN(d.getTime())) return d.toLocaleDateString();

  return trimmed;
}

// Parses ${dueDateHelper.fromProcessStart(execution, N)} (with optional
// whitespace) into a "+N days from process start" label. Returns null when
// the expression doesn't match, so the caller can fall through to other
// formats. Currently the helper only exposes fromProcessStart; future helper
// methods would need their own clauses here.
function parseDueDateHelperExpression(raw: string): string | null {
  const match = /^\$\{\s*dueDateHelper\.fromProcessStart\s*\(\s*execution\s*,\s*(\d+)\s*\)\s*\}$/i.exec(raw);
  if (!match) return null;
  const days = Number(match[1]);
  return `+${days} day${days === 1 ? "" : "s"} from process start`;
}

// Parses an ISO 8601 duration into a "+N units" label without any "from X"
// suffix — the caller adds the appropriate anchor wording. Returns null when
// the string isn't a recognized duration.
function parseIso8601DurationLabel(raw: string): string | null {
  const match = /^P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?)?$/i.exec(raw);
  if (!match) return null;

  const weeks = match[1] ? Number(match[1]) : 0;
  const days = match[2] ? Number(match[2]) : 0;
  const hours = match[3] ? Number(match[3]) : 0;
  const minutes = match[4] ? Number(match[4]) : 0;

  const totalDays = weeks * 7 + days;
  const parts: string[] = [];
  if (totalDays > 0) parts.push(`${totalDays} day${totalDays === 1 ? "" : "s"}`);
  if (hours > 0) parts.push(`${hours} hour${hours === 1 ? "" : "s"}`);
  if (minutes > 0) parts.push(`${minutes} minute${minutes === 1 ? "" : "s"}`);
  if (parts.length === 0) return null;

  return `+${parts.join(" ")}`;
}
