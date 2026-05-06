import { useState } from "react";
import { useExecutionHistory } from "@/hooks/useExecutions";
import { useUserDirectory, userFullDisplay } from "@/hooks/useUserDirectory";
import { describeError, formatTimestamp } from "./utils";

type Props = {
  processInstanceId: string;
};

export default function ExecutionHistory({ processInstanceId }: Props) {
  const { data: events = [], isLoading, error } = useExecutionHistory(processInstanceId);
  const directory = useUserDirectory();

  if (error) {
    return (
      <div className="alert alert-danger" role="alert">
        {describeError(error)}
      </div>
    );
  }

  if (isLoading) {
    return <p className="workflow-executions-loading">Loading history...</p>;
  }

  if (events.length === 0) {
    return <p className="text-body text-opacity-50 mb-0">No history yet.</p>;
  }

  return (
    <ol className="list-unstyled mb-0 workflow-execution-history-list">
      {events.map((event, index) => (
        <li
          key={`${event.activityId}-${event.startedAtUtc ?? index}`}
          className="mb-3 pb-3 border-bottom workflow-execution-history-item"
        >
          <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
            <strong>{event.activityName ?? event.activityId}</strong>
            {event.activityType && (
              <span className={activityTypeBadgeClass(event.activityType)}>
                {event.activityType}
              </span>
            )}
            {event.deleteReason && (
              <span className="badge text-bg-warning">{event.deleteReason}</span>
            )}
            {event.isOverride && (
              <span className="badge text-bg-warning" title="Completed via admin override">
                override
              </span>
            )}
            {event.isErrored && (
              <span
                className="badge text-bg-danger"
                title={event.errorMessage ?? "Activity failed"}
              >
                {event.errorCount && event.errorCount > 1
                  ? `errored × ${event.errorCount}`
                  : "errored"}
              </span>
            )}
          </div>
          <div className="small text-body text-opacity-75">
            <span>{formatTimestamp(event.startedAtUtc)}</span>
            <span className="mx-2">→</span>
            <span>{event.endedAtUtc ? formatTimestamp(event.endedAtUtc) : "in progress"}</span>
            {event.endedAtUtc && event.durationMs !== null && (
              <>
                <span className="mx-2">·</span>
                <span>{formatDuration(event.durationMs)}</span>
              </>
            )}
            {event.assignee && (
              <>
                <span className="mx-2">·</span>
                <span>Assignee: {userFullDisplay(directory.get(event.assignee), event.assignee)}</span>
              </>
            )}
            {event.completedByUserId && event.completedByUserId !== event.assignee && (
              <>
                <span className="mx-2">·</span>
                <span>
                  Task Completed By: {userFullDisplay(directory.get(event.completedByUserId), event.completedByUserId)}
                </span>
              </>
            )}
          </div>
          {event.errorMessage && (
            <ErrorDetails
              message={event.errorMessage}
              stackTrace={event.errorStackTrace}
            />
          )}
        </li>
      ))}
    </ol>
  );
}

function activityTypeBadgeClass(activityType: string): string {
  switch (activityType) {
    case "userTask":
      return "badge text-bg-primary";
    case "serviceTask":
    case "scriptTask":
      return "badge text-bg-info";
    case "startEvent":
      return "badge text-bg-success";
    case "endEvent":
      return "badge text-bg-dark";
    default:
      return "badge text-bg-secondary";
  }
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes < 60) {
    return seconds === 0 ? `${minutes}m` : `${minutes}m ${seconds}s`;
  }
  const hours = Math.floor(minutes / 60);
  const remMinutes = minutes % 60;
  return remMinutes === 0 ? `${hours}h` : `${hours}h ${remMinutes}m`;
}

type ErrorDetailsProps = {
  message: string;
  stackTrace: string | null;
};

function ErrorDetails({ message, stackTrace }: ErrorDetailsProps) {
  const [expanded, setExpanded] = useState(false);
  const hasStack = typeof stackTrace === "string" && stackTrace.length > 0;

  return (
    <div className="small text-danger mt-1">
      <code className="text-danger">{message}</code>
      {hasStack && (
        <>
          {" "}
          <button
            type="button"
            className="btn btn-link btn-sm p-0 align-baseline text-danger"
            aria-expanded={expanded}
            onClick={() => setExpanded((v) => !v)}
          >
            {expanded ? "Hide stack trace" : "Show stack trace"}
          </button>
          {expanded && (
            <pre className="workflow-execution-history-stack mt-1 mb-0 small">
              {stackTrace}
            </pre>
          )}
        </>
      )}
    </div>
  );
}

