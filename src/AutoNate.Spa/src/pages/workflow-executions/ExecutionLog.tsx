import { useExecutionLog } from "@/hooks/useExecutions";
import { useUserDirectory, userFullDisplay } from "@/hooks/useUserDirectory";
import { WorkflowExecutionLogEntry, WorkflowExecutionLogKind } from "@/types/flowable";
import { describeError, formatTimestamp } from "./utils";

type Props = {
  processInstanceId: string;
};

type UserDirectory = ReturnType<typeof useUserDirectory>;

export default function ExecutionLog({ processInstanceId }: Props) {
  const { data: entries = [], isLoading, error } = useExecutionLog(processInstanceId);
  const directory = useUserDirectory();

  if (error) {
    return (
      <div className="alert alert-danger" role="alert">
        {describeError(error)}
      </div>
    );
  }

  if (isLoading) {
    return <p className="workflow-executions-loading">Loading execution log...</p>;
  }

  if (entries.length === 0) {
    return <p className="text-body text-opacity-50 mb-0">No log entries yet.</p>;
  }

  return (
    <ol className="list-unstyled mb-0 workflow-execution-log-list">
      {entries.map((entry, index) => (
        <li
          key={`${entry.kind}-${entry.occurredAtUtc ?? index}-${index}`}
          className="mb-3 pb-3 border-bottom workflow-execution-log-item"
        >
          {renderEntry(entry, directory)}
        </li>
      ))}
    </ol>
  );
}

function renderEntry(entry: WorkflowExecutionLogEntry, directory: UserDirectory) {
  if (entry.kind === "variable-update" && entry.variableUpdate) {
    return renderVariableUpdate(entry, entry.variableUpdate);
  }
  if (entry.kind === "error" && entry.error) {
    return renderError(entry, entry.error);
  }
  if (entry.task) {
    return renderTaskEvent(entry, entry.task, directory);
  }
  return null;
}

function renderError(
  entry: WorkflowExecutionLogEntry,
  err: NonNullable<WorkflowExecutionLogEntry["error"]>
) {
  return (
    <>
      <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
        <span className="badge text-bg-danger">error</span>
        <strong>{err.activityName ?? err.activityId}</strong>
        {err.rawFlowableEventType && (
          <span className="badge text-bg-light text-dark">{err.rawFlowableEventType}</span>
        )}
      </div>
      <div className="small text-body text-opacity-75">
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
      </div>
      {err.errorMessage && (
        <div className="small text-danger mt-1">
          <code className="text-danger">{err.errorMessage}</code>
        </div>
      )}
    </>
  );
}

function renderVariableUpdate(
  entry: WorkflowExecutionLogEntry,
  v: NonNullable<WorkflowExecutionLogEntry["variableUpdate"]>
) {
  return (
    <>
      <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
        <span className={kindBadgeClass(entry.kind)}>variable</span>
        <strong>{v.name}</strong>
        {v.type && <span className="badge text-bg-secondary">{v.type}</span>}
        {v.revision !== null && (
          <span className="text-body text-opacity-50 small">rev {v.revision}</span>
        )}
      </div>
      {v.value !== null && (
        <div className="small mb-1">
          <code className="text-body">{v.value}</code>
        </div>
      )}
      <div className="small text-body text-opacity-75">
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
        {v.taskId && (
          <>
            <span className="mx-2">·</span>
            <span>during task {v.taskId}</span>
          </>
        )}
      </div>
    </>
  );
}

function renderTaskEvent(
  entry: WorkflowExecutionLogEntry,
  t: NonNullable<WorkflowExecutionLogEntry["task"]>,
  directory: UserDirectory
) {
  return (
    <>
      <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
        <span className={kindBadgeClass(entry.kind)}>{kindLabel(entry.kind)}</span>
        <strong>{t.name ?? t.taskDefinitionKey ?? t.taskId}</strong>
        {t.deleteReason && entry.kind === "task-cancelled" && (
          <span className="badge text-bg-warning">{t.deleteReason}</span>
        )}
        {t.isOverride && entry.kind === "task-completed" && (
          <span className="badge text-bg-warning" title="Completed via admin override">
            override
          </span>
        )}
        {t.formKey && <span className="badge text-bg-light text-dark">form: {t.formKey}</span>}
        {t.priority !== null && t.priority !== 50 && (
          <span className="badge text-bg-light text-dark">priority {t.priority}</span>
        )}
      </div>
      <div className="small text-body text-opacity-75">
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
        {t.assignee && (
          <>
            <span className="mx-2">·</span>
            <span>Assignee: {userFullDisplay(directory.get(t.assignee), t.assignee)}</span>
          </>
        )}
        {t.owner && t.owner !== t.assignee && (
          <>
            <span className="mx-2">·</span>
            <span>Owner: {userFullDisplay(directory.get(t.owner), t.owner)}</span>
          </>
        )}
        {entry.kind === "task-completed" && t.completedByUserId && t.completedByUserId !== t.assignee && (
          <>
            <span className="mx-2">·</span>
            <span>
              Task Completed By: {userFullDisplay(directory.get(t.completedByUserId), t.completedByUserId)}
            </span>
          </>
        )}
        {t.dueAtUtc && (
          <>
            <span className="mx-2">·</span>
            <span>Due {formatTimestamp(t.dueAtUtc)}</span>
          </>
        )}
      </div>
    </>
  );
}

function kindBadgeClass(kind: WorkflowExecutionLogKind): string {
  switch (kind) {
    case "variable-update":
      return "badge text-bg-info";
    case "task-created":
      return "badge text-bg-secondary";
    case "task-claimed":
      return "badge text-bg-primary";
    case "task-completed":
      return "badge text-bg-success";
    case "task-cancelled":
      return "badge text-bg-warning";
    case "error":
      return "badge text-bg-danger";
    default:
      return "badge text-bg-secondary";
  }
}

function kindLabel(kind: WorkflowExecutionLogKind): string {
  switch (kind) {
    case "variable-update":
      return "variable";
    case "task-created":
      return "task created";
    case "task-claimed":
      return "task claimed";
    case "task-completed":
      return "task completed";
    case "task-cancelled":
      return "task cancelled";
    case "error":
      return "error";
    default:
      return kind;
  }
}
