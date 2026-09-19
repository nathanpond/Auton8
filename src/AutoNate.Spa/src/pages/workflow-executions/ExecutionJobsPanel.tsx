import { useCallback, useMemo, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Code,
  Group,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip
} from "@mantine/core";
import {
  getJobExceptionStack,
  listExecutionJobs,
  rescheduleJob,
  retryJob,
  type WorkflowJob,
  type WorkflowJobQueue
} from "@/api/executions";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";
import { toast } from "@/components/notifications/toast";
import { describeError } from "@/lib/describeError";

/**
 * What is scheduled, what is retrying, and what is stuck (#172).
 *
 * The question this answers is "why hasn't this fired?", and the second question
 * is "make it fire now". Both were unanswerable in Auton8 before this: thirty-odd
 * methods on the Flowable client and not one touched jobs.
 */
export function ExecutionJobsPanel({ processInstanceId }: { processInstanceId: string }) {
  const qc = useQueryClient();
  const [expanded, setExpanded] = useState<string | null>(null);
  const [dueAt, setDueAt] = useState<Record<string, string>>({});

  const queryKey = useMemo(
    () => ["executions", "jobs", processInstanceId] as const,
    [processInstanceId]
  );

  const {
    data: jobs = [],
    isLoading,
    error
  } = useQuery<WorkflowJob[]>({
    queryKey,
    queryFn: ({ signal }) => listExecutionJobs(processInstanceId, signal)
  });

  // Kind-and-instance checks, so the controls a user cannot use are not offered.
  // The server gate is the real one; this only avoids handing someone a button
  // that will refuse them.
  const checks = useMemo(
    () => [
      { kind: "workflowexecution", action: "retryjob", id: processInstanceId },
      { kind: "workflowexecution", action: "reschedulejob", id: processInstanceId }
    ],
    [processInstanceId]
  );
  const { data: perms } = usePermissionChecks(checks);
  const mayRetry =
    perms?.get(
      permissionKey({ kind: "workflowexecution", action: "retryjob", id: processInstanceId })
    ) ?? false;
  const mayReschedule =
    perms?.get(
      permissionKey({ kind: "workflowexecution", action: "reschedulejob", id: processInstanceId })
    ) ?? false;

  const invalidate = useCallback(() => {
    qc.invalidateQueries({ queryKey });
  }, [qc, queryKey]);

  const retry = useMutation({
    mutationFn: ({ id, queue }: { id: string; queue: WorkflowJobQueue }) =>
      retryJob(processInstanceId, id, queue),
    onSuccess: () => {
      // Worded as what was asked for, not as what happened. Retrying a
      // dead-lettered job is a MOVE back to the executable queue; whether the
      // step then succeeds is a separate thing, and saying "retried" here would
      // be claiming an outcome this call cannot know.
      toast.success("Job queued to run again.");
      invalidate();
    },
    onError: (err) => toast.error(`Failed to retry the job. ${describeError(err)}`)
  });

  const reschedule = useMutation({
    mutationFn: ({ id, when }: { id: string; when: string }) =>
      rescheduleJob(processInstanceId, id, when),
    onSuccess: () => {
      toast.success("Timer rescheduled.");
      invalidate();
    },
    onError: (err) => toast.error(`Failed to reschedule the timer. ${describeError(err)}`)
  });

  const { data: stack, isFetching: stackLoading } = useQuery<string | null>({
    queryKey: ["executions", "jobs", processInstanceId, "stack", expanded],
    queryFn: ({ signal }) => {
      const job = jobs.find((j) => j.id === expanded);
      if (!job) return Promise.resolve(null);
      return getJobExceptionStack(processInstanceId, job.id, job.queue, signal);
    },
    enabled: expanded !== null
  });

  // A named region, so a screen-reader user can jump to it and knows what they
  // landed in. It also scopes the assertions in JobsPanelAccessibilityTests --
  // this page carries several Alerts, and a role check against the whole page
  // would be asserting about all of them.
  const region = (body: ReactNode) => (
    <div role="region" aria-label="Jobs and timers">
      {body}
    </div>
  );

  if (isLoading) {
    return region(
      <Text size="sm" c="dimmed">
        Loading jobs…
      </Text>
    );
  }

  // An unreachable engine must not look like a healthy execution with nothing
  // scheduled (#172's AC). These read live from Flowable precisely so they are
  // current, which means the failure has to be said out loud rather than
  // rendered as an empty list.
  if (error) {
    return region(
      // role="alert" is RIGHT here, and it is the only place on this panel it is.
      // An operator must not read past "we could not ask".
      <Alert color="red" variant="light" role="alert" title="Jobs could not be read">
        The workflow engine did not answer, so it is not known whether this
        execution has scheduled or stuck work. This is <strong>not</strong> the
        same as having none. {describeError(error)}
      </Alert>
    );
  }

  if (jobs.length === 0) {
    return region(
      // NOT role="alert", which is Mantine's default for Alert. A healthy
      // execution is a normal state, and announcing it assertively interrupts a
      // screen-reader user to tell them everything is fine -- the same defect
      // #597 shipped and had to fix on the freshness indicator.
      <Alert color="gray" variant="light" role="presentation" title="Nothing scheduled or stuck">
        This execution has no timers waiting, nothing retrying in the background,
        and nothing in the dead-letter queue.
      </Alert>
    );
  }

  return region(
    <Stack gap="sm">
      <Table striped highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th scope="col">Step</Table.Th>
            <Table.Th scope="col">State</Table.Th>
            <Table.Th scope="col">Retries left</Table.Th>
            <Table.Th scope="col">Due</Table.Th>
            <Table.Th scope="col">Failure</Table.Th>
            <Table.Th scope="col">Actions</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {jobs.map((job) => {
            const stuck = job.queue === "DeadLetter";
            const isOpen = expanded === job.id;
            return (
              <>
                <Table.Tr key={job.id}>
                  <Table.Td>
                    <Text size="sm">{job.elementName ?? job.elementId ?? "(unnamed step)"}</Text>
                    {job.elementId && job.elementName && (
                      <Text size="xs" c="dimmed">
                        {job.elementId}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Badge color={queueColor(job.queue)} variant="light">
                      {queueLabel(job.queue)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{job.retries}</Table.Td>
                  <Table.Td>
                    <Text size="sm">{job.dueAtUtc ? new Date(job.dueAtUtc).toLocaleString() : "—"}</Text>
                  </Table.Td>
                  <Table.Td>
                    {job.exceptionMessage ? (
                      <Group gap="xs" wrap="nowrap" align="center">
                        <Text size="sm" lineClamp={1} style={{ maxWidth: "22rem" }}>
                          {job.exceptionMessage}
                        </Text>
                        {/* The stack is behind one click on purpose: an operator
                            who cannot see why it failed cannot decide whether
                            retrying is sensible, but a stack in every row makes
                            the list unscannable. */}
                        <Tooltip label={isOpen ? "Hide the stack" : "Show the stack"}>
                          <ActionIcon
                            variant="subtle"
                            aria-label={
                              isOpen
                                ? `Hide the failure detail for ${job.elementName ?? job.id}`
                                : `Show the failure detail for ${job.elementName ?? job.id}`
                            }
                            aria-expanded={isOpen}
                            onClick={() => setExpanded(isOpen ? null : job.id)}
                          >
                            <i className={`fa ${isOpen ? "fa-chevron-up" : "fa-chevron-down"}`} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    ) : (
                      <Text size="sm" c="dimmed">
                        —
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      {(stuck || job.queue === "Executable") && mayRetry && (
                        <Button
                          size="xs"
                          variant="light"
                          loading={retry.isPending}
                          onClick={() => retry.mutate({ id: job.id, queue: job.queue })}
                        >
                          Run again
                        </Button>
                      )}
                      {job.queue === "Timer" && mayReschedule && (
                        <Group gap={4} wrap="nowrap">
                          <TextInput
                            size="xs"
                            type="datetime-local"
                            aria-label={`New time for ${job.elementName ?? job.id}`}
                            value={dueAt[job.id] ?? ""}
                            onChange={(e) =>
                              setDueAt((prev) => ({ ...prev, [job.id]: e.target.value }))
                            }
                          />
                          <Button
                            size="xs"
                            variant="light"
                            disabled={!dueAt[job.id]}
                            loading={reschedule.isPending}
                            onClick={() =>
                              reschedule.mutate({
                                id: job.id,
                                // The control is local-time; the API is UTC.
                                // Converting here rather than at the boundary
                                // would leave the server guessing a timezone.
                                when: new Date(dueAt[job.id]).toISOString()
                              })
                            }
                          >
                            Reschedule
                          </Button>
                        </Group>
                      )}
                    </Group>
                  </Table.Td>
                </Table.Tr>
                {isOpen && (
                  <Table.Tr key={`${job.id}-stack`}>
                    <Table.Td colSpan={6}>
                      <>
                        {stackLoading ? (
                          <Text size="sm" c="dimmed">
                            Loading the failure detail…
                          </Text>
                        ) : stack ? (
                          <Code block style={{ maxHeight: "18rem", overflow: "auto" }}>
                            {stack}
                          </Code>
                        ) : (
                          <Text size="sm" c="dimmed">
                            The engine kept no stack for this job — the message above is
                            everything it recorded.
                          </Text>
                        )}
                      </>
                    </Table.Td>
                  </Table.Tr>
                )}
              </>
            );
          })}
        </Table.Tbody>
      </Table>
    </Stack>
  );
}

function queueLabel(queue: WorkflowJobQueue): string {
  switch (queue) {
    case "DeadLetter":
      return "Stuck";
    case "Timer":
      return "Scheduled";
    case "Executable":
      return "Retrying";
    case "Suspended":
      return "Paused";
    default:
      return queue;
  }
}

function queueColor(queue: WorkflowJobQueue): string {
  switch (queue) {
    case "DeadLetter":
      return "red";
    case "Timer":
      return "blue";
    case "Executable":
      return "yellow";
    default:
      return "gray";
  }
}
