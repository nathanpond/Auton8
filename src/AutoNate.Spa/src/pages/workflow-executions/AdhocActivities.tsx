import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Group, Stack, Text, Title } from "@mantine/core";
import { completeAdhocSubProcess, startAdhocActivity } from "@/api/executions";
import type { AdhocSubProcessState } from "@/api/executions";
import { adhocQueryKey, executionTasksQueryKey } from "@/hooks/useExecutions";

// #163. An ad-hoc subprocess is BPMN's case-management construct: the process
// defines what CAN be done and a person decides what happens next, in whatever
// order makes sense, until the completion condition is satisfied.
//
// The same activity may be started more than once — that repeatability is the
// property distinguishing it from a parallel subprocess, so nothing here removes
// an activity from the list after it is started.
export default function AdhocActivities({
  processInstanceId,
  subProcesses,
  onError,
  onStarted
}: {
  processInstanceId: string;
  subProcesses: AdhocSubProcessState[];
  onError: (message: string) => void;
  onStarted: (message: string) => void;
}) {
  const qc = useQueryClient();
  const [busy, setBusy] = useState<string | null>(null);

  const refresh = () => {
    qc.invalidateQueries({ queryKey: adhocQueryKey(processInstanceId) });
    qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
  };

  const start = async (executionId: string, activityId: string, label: string) => {
    setBusy(`${executionId}:${activityId}`);
    try {
      await startAdhocActivity(processInstanceId, executionId, activityId);
      onStarted(`Started '${label}'.`);
      refresh();
    } catch (error) {
      onError(describeAdhocError(error, `start '${label}'`));
    } finally {
      setBusy(null);
    }
  };

  const complete = async (executionId: string, label: string) => {
    setBusy(`${executionId}:complete`);
    try {
      await completeAdhocSubProcess(processInstanceId, executionId);
      onStarted(`Completed '${label}'.`);
      refresh();
    } catch (error) {
      onError(describeAdhocError(error, `complete '${label}'`));
    } finally {
      setBusy(null);
    }
  };

  if (subProcesses.length === 0) {
    // An in-page Alert, not a toast: this is a condition of the page and is
    // still true after a reload.
    return (
      <Alert color="gray" variant="light">
        Nothing in this execution is waiting on a decision about what to do next.
      </Alert>
    );
  }

  return (
    <Stack gap="lg">
      {subProcesses.map((subProcess) => (
        <section key={subProcess.executionId} aria-labelledby={`adhoc-${subProcess.executionId}`}>
          <Group justify="space-between" align="center" mb="xs">
            <Title order={5} id={`adhoc-${subProcess.executionId}`} m={0}>
              {subProcess.activityId}
            </Title>
            <Button
              variant="default"
              size="xs"
              loading={busy === `${subProcess.executionId}:complete`}
              disabled={busy !== null}
              onClick={() => complete(subProcess.executionId, subProcess.activityId)}
            >
              Finish this section
            </Button>
          </Group>

          {subProcess.enabledActivities.length === 0 ? (
            <Text size="sm" c="dimmed">
              No activities are available to start right now.
            </Text>
          ) : (
            // A real list, so a screen reader announces how many choices there
            // are before reading them. Each control is a button with the
            // activity's own name — "Start" alone would read identically for
            // every row out of context.
            <ul className="workflow-adhoc-list">
              {subProcess.enabledActivities.map((activity) => {
                const label = activity.name ?? activity.id;
                const key = `${subProcess.executionId}:${activity.id}`;
                return (
                  <li key={activity.id}>
                    <Group justify="space-between" align="center" wrap="nowrap">
                      <Text size="sm">{label}</Text>
                      <Button
                        size="xs"
                        loading={busy === key}
                        disabled={busy !== null}
                        onClick={() => start(subProcess.executionId, activity.id, label)}
                      >
                        Start {label}
                      </Button>
                    </Group>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      ))}
    </Stack>
  );
}

// The engine's own message is the useful part — "activity is not enabled" tells
// an author exactly what happened — so it is surfaced rather than replaced.
function describeAdhocError(error: unknown, what: string): string {
  const response = (error as { response?: { data?: { message?: string } } })?.response;
  const message = response?.data?.message;
  return message ? `Could not ${what}: ${message}` : `Could not ${what}.`;
}
