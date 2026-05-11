import { Alert, Badge, Text } from "@mantine/core";
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
      <Alert color="red" variant="light" role="alert">
        {describeError(error)}
      </Alert>
    );
  }

  if (isLoading) {
    return (
      <Text size="sm" c="dimmed">
        Loading execution log...
      </Text>
    );
  }

  if (entries.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        No log entries yet.
      </Text>
    );
  }

  return (
    <ol
      className="workflow-execution-log-list"
      style={{ listStyle: "none", margin: 0, padding: 0 }}
    >
      {entries.map((entry, index) => (
        <li
          key={`${entry.kind}-${entry.occurredAtUtc ?? index}-${index}`}
          className="workflow-execution-log-item"
          style={{
            marginBottom: 16,
            paddingBottom: 16,
            borderBottom: "1px solid var(--mantine-color-default-border)"
          }}
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
      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8, marginBottom: 4 }}>
        <Badge color="red" variant="filled">error</Badge>
        <strong>{err.activityName ?? err.activityId}</strong>
        {err.rawFlowableEventType && (
          <Badge color="gray" variant="light">{err.rawFlowableEventType}</Badge>
        )}
      </div>
      <div style={{ fontSize: "0.875rem", color: "var(--mantine-color-dimmed)" }}>
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
      </div>
      {err.errorMessage && (
        <div style={{ fontSize: "0.875rem", color: "var(--mantine-color-red-filled)", marginTop: 4 }}>
          <code style={{ color: "var(--mantine-color-red-filled)" }}>{err.errorMessage}</code>
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
      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8, marginBottom: 4 }}>
        <Badge color={kindBadgeColor(entry.kind)} variant="filled">variable</Badge>
        <strong>{v.name}</strong>
        {v.type && <Badge color="gray" variant="filled">{v.type}</Badge>}
        {v.revision !== null && (
          <span style={{ color: "var(--mantine-color-dimmed)", fontSize: "0.875rem" }}>rev {v.revision}</span>
        )}
      </div>
      {v.value !== null && (
        <div style={{ fontSize: "0.875rem", marginBottom: 4 }}>
          <code>{v.value}</code>
        </div>
      )}
      <div style={{ fontSize: "0.875rem", color: "var(--mantine-color-dimmed)" }}>
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
        {v.taskId && (
          <>
            <span style={{ margin: "0 8px" }}>·</span>
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
      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8, marginBottom: 4 }}>
        <Badge color={kindBadgeColor(entry.kind)} variant="filled">{kindLabel(entry.kind)}</Badge>
        <strong>{t.name ?? t.taskDefinitionKey ?? t.taskId}</strong>
        {t.deleteReason && entry.kind === "task-cancelled" && (
          <Badge color="yellow" variant="filled">{t.deleteReason}</Badge>
        )}
        {t.isOverride && entry.kind === "task-completed" && (
          <Badge color="yellow" variant="filled" title="Completed via admin override">override</Badge>
        )}
        {t.formKey && <Badge color="gray" variant="light">form: {t.formKey}</Badge>}
        {t.priority !== null && t.priority !== 50 && (
          <Badge color="gray" variant="light">priority {t.priority}</Badge>
        )}
      </div>
      <div style={{ fontSize: "0.875rem", color: "var(--mantine-color-dimmed)" }}>
        <span>{formatTimestamp(entry.occurredAtUtc)}</span>
        {t.assignee && (
          <>
            <span style={{ margin: "0 8px" }}>·</span>
            <span>Assignee: {userFullDisplay(directory.get(t.assignee), t.assignee)}</span>
          </>
        )}
        {t.owner && t.owner !== t.assignee && (
          <>
            <span style={{ margin: "0 8px" }}>·</span>
            <span>Owner: {userFullDisplay(directory.get(t.owner), t.owner)}</span>
          </>
        )}
        {entry.kind === "task-completed" && t.completedByUserId && t.completedByUserId !== t.assignee && (
          <>
            <span style={{ margin: "0 8px" }}>·</span>
            <span>
              Task Completed By: {userFullDisplay(directory.get(t.completedByUserId), t.completedByUserId)}
            </span>
          </>
        )}
        {t.dueAtUtc && (
          <>
            <span style={{ margin: "0 8px" }}>·</span>
            <span>Due {formatTimestamp(t.dueAtUtc)}</span>
          </>
        )}
      </div>
    </>
  );
}

function kindBadgeColor(kind: WorkflowExecutionLogKind): string {
  switch (kind) {
    case "variable-update":
      return "cyan";
    case "task-created":
      return "gray";
    case "task-claimed":
      return "blue";
    case "task-completed":
      return "green";
    case "task-cancelled":
      return "yellow";
    case "error":
      return "red";
    default:
      return "gray";
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
