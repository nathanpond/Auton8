import { useId, useState } from "react";
import { Alert, Badge, Button, Text } from "@mantine/core";
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
      <Alert color="red" variant="light" role="alert">
        {describeError(error)}
      </Alert>
    );
  }

  if (isLoading) {
    return (
      <Text size="sm" c="dimmed">
        Loading history...
      </Text>
    );
  }

  if (events.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        No history yet.
      </Text>
    );
  }

  return (
    <ol className="workflow-execution-history-list" style={{ listStyle: "none", margin: 0, padding: 0 }}>
      {events.map((event, index) => (
        <li
          key={`${event.activityId}-${event.startedAtUtc ?? index}`}
          className="workflow-execution-history-item"
          style={{ marginBottom: 16, paddingBottom: 16, borderBottom: "1px solid var(--mantine-color-default-border)" }}
        >
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8, marginBottom: 4 }}>
            <strong>{event.activityName ?? event.activityId}</strong>
            {event.activityType && (
              <Badge color={activityTypeBadgeColor(event.activityType)} variant="filled">
                {event.activityType}
              </Badge>
            )}
            {event.deleteReason && (
              <Badge color="yellow" variant="filled">
                {event.deleteReason}
              </Badge>
            )}
            {event.isOverride && (
              <Badge color="yellow" variant="filled" title="Completed via admin override">
                override
              </Badge>
            )}
            {event.isErrored && (
              <Badge color="red" variant="filled" title={event.errorMessage ?? "Activity failed"}>
                {event.errorCount && event.errorCount > 1
                  ? `errored × ${event.errorCount}`
                  : "errored"}
              </Badge>
            )}
          </div>
          <div style={{ fontSize: "0.875rem", color: "var(--mantine-color-dimmed)" }}>
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

function activityTypeBadgeColor(activityType: string): string {
  switch (activityType) {
    case "userTask":
      return "blue";
    case "serviceTask":
    case "scriptTask":
      return "cyan";
    case "startEvent":
      return "green";
    case "endEvent":
      return "dark";
    default:
      return "gray";
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
  const stackId = useId();
  const hasStack = typeof stackTrace === "string" && stackTrace.length > 0;

  return (
    <Text size="xs" c="red" mt={4}>
      <code style={{ color: "var(--mantine-color-red-7)" }}>{message}</code>
      {hasStack && (
        <>
          {" "}
          <Button
            variant="subtle"
            color="red"
            size="compact-xs"
            aria-expanded={expanded}
            aria-controls={stackId}
            onClick={() => setExpanded((v) => !v)}
          >
            {expanded ? "Hide stack trace" : "Show stack trace"}
          </Button>
          {expanded && (
            <pre
              id={stackId}
              tabIndex={0}
              className="workflow-execution-history-stack"
              style={{ marginTop: 4, marginBottom: 0, fontSize: 12 }}
            >
              {stackTrace}
            </pre>
          )}
        </>
      )}
    </Text>
  );
}

