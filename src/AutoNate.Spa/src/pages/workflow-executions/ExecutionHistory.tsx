import { useId, useState } from "react";
import { Alert, Badge, Button, Group, Progress, Text, VisuallyHidden } from "@mantine/core";
import { useExecutionActivityInstances, useExecutionHistory } from "@/hooks/useExecutions";
import { useUserDirectory, userFullDisplay } from "@/hooks/useUserDirectory";
import { summarizeMultiInstance } from "@/lib/multiInstanceProgress";
import type { WorkflowExecutionHistoryEvent } from "@/types/flowable";
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
            <span style={{ margin: "0 var(--mantine-spacing-xs)" }}>→</span>
            <span>{event.endedAtUtc ? formatTimestamp(event.endedAtUtc) : "in progress"}</span>
            {event.endedAtUtc && event.durationMs !== null && (
              <>
                <span style={{ margin: "0 var(--mantine-spacing-xs)" }}>·</span>
                <span>{formatDuration(event.durationMs)}</span>
              </>
            )}
            {event.assignee && (
              <>
                <span style={{ margin: "0 var(--mantine-spacing-xs)" }}>·</span>
                <span>Assignee: {userFullDisplay(directory.get(event.assignee), event.assignee)}</span>
              </>
            )}
            {event.completedByUserId && event.completedByUserId !== event.assignee && (
              <>
                <span style={{ margin: "0 var(--mantine-spacing-xs)" }}>·</span>
                <span>
                  Task Completed By: {userFullDisplay(directory.get(event.completedByUserId), event.completedByUserId)}
                </span>
              </>
            )}
          </div>
          {event.multiInstance && (
            <MultiInstanceRow
              processInstanceId={processInstanceId}
              event={event}
            />
          )}
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

type MultiInstanceRowProps = {
  processInstanceId: string;
  event: WorkflowExecutionHistoryEvent;
};

/**
 * #173. One multi-instance activity, collapsed to its progress and expandable.
 *
 * The server already folded the engine's rows into one; this shows what that
 * row says and fetches the instances only when asked. A process with 500
 * instances therefore costs one row here and one request if -- and only if --
 * somebody opens it.
 */
function MultiInstanceRow({ processInstanceId, event }: MultiInstanceRowProps) {
  const [expanded, setExpanded] = useState(false);
  const panelId = useId();
  const directory = useUserDirectory();

  const progress = event.multiInstance!;
  const label = event.activityName ?? event.activityId;
  const summary = summarizeMultiInstance(progress, label);

  const {
    data: instances = [],
    isLoading,
    error
  } = useExecutionActivityInstances(processInstanceId, event.activityId, expanded);

  return (
    <div style={{ marginTop: 8 }}>
      <Group gap="xs" wrap="wrap" align="center">
        {/*
          * The count is TEXT, and it is the fact. The bar beside it is
          * decoration and is hidden from the accessibility tree rather than
          * announced as a second, vaguer version of the same number.
          */}
        <Text size="sm" fw={500}>
          {summary.completionLabel}
        </Text>

        <Progress
          value={summary.percent}
          aria-hidden="true"
          style={{ width: 120 }}
          color={summary.attentionLabel ? "yellow" : "blue"}
        />

        {summary.sequentialLabel && (
          <Text size="sm" c="dimmed">
            {summary.sequentialLabel}
          </Text>
        )}

        {/*
          * Not colour alone: the badge carries the words. An operator scanning
          * the list reads "1 failed" whether or not they can tell yellow from
          * grey, which is the AC.
          */}
        {summary.attentionLabel && (
          <Badge color="yellow" variant="filled" leftSection={<i className="fa fa-triangle-exclamation" aria-hidden="true" />}>
            {summary.attentionLabel}
          </Badge>
        )}

        {/*
          * A real <button>, so it is in the tab order and answers Enter and
          * Space without anything being re-implemented.
          */}
        <Button
          variant="subtle"
          size="compact-xs"
          aria-expanded={expanded}
          aria-controls={panelId}
          onClick={() => setExpanded((v) => !v)}
        >
          {expanded ? "Hide instances" : `Show ${progress.total} instances`}
        </Button>
      </Group>

      {/*
        * The whole sentence, announced politely. Screen-reader users get no
        * badge layout, so the parts are read as prose rather than left to be
        * inferred from what happens to sit next to what.
        */}
      <VisuallyHidden role="status" aria-live="polite">
        {summary.announcement}
      </VisuallyHidden>

      {expanded && (
        <div id={panelId} style={{ marginTop: 8 }}>
          {error && (
            <Alert color="red" variant="light" role="alert">
              {describeError(error)}
            </Alert>
          )}
          {isLoading && (
            <Text size="sm" c="dimmed">
              Loading instances...
            </Text>
          )}
          {!isLoading && !error && instances.length === 0 && (
            <Text size="sm" c="dimmed">
              No instances recorded yet.
            </Text>
          )}
          {instances.length > 0 && (
            <ol style={{ listStyle: "none", margin: 0, padding: 0 }}>
              {instances.map((instance, index) => (
                <li
                  key={instance.executionId ?? instance.taskId ?? index}
                  style={{
                    display: "flex",
                    flexWrap: "wrap",
                    gap: 8,
                    padding: "4px 0",
                    fontSize: "0.875rem"
                  }}
                >
                  <Text size="sm" fw={500}>
                    {/*
                      * The collection item, which is the only thing that tells
                      * one instance from another. Numbered when the loop binds
                      * no item -- a bare cardinality hands out nothing to name.
                      */}
                    {instance.elementValue ?? `Instance ${index + 1}`}
                  </Text>
                  <Text size="sm" c="dimmed">
                    {instance.endedAtUtc ? "complete" : "in progress"}
                  </Text>
                  {instance.assignee && (
                    <Text size="sm" c="dimmed">
                      {userFullDisplay(directory.get(instance.assignee), instance.assignee)}
                    </Text>
                  )}
                  <Text size="sm" c="dimmed">
                    {formatTimestamp(instance.startedAtUtc)}
                  </Text>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </div>
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

