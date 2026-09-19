import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Alert, Anchor, Badge, Button, Group, Table, Text } from "@mantine/core";
import { Link } from "react-router-dom";
import { listStuckJobs, type WorkflowJob } from "@/api/executions";
import { describeError } from "@/lib/describeError";

/**
 * "What is stuck right now", across every execution the caller may see (#172).
 *
 * <b>A filter on the executions area, not a page of its own</b> (the story left
 * this to discretion). An operator arrives here from "something is wrong", and a
 * separate page would need a permanent navigation entry for a list that is empty
 * on a healthy system — an entry that says "stuck work" every day of the year
 * teaches people to stop reading it.
 *
 * So it is a control that reports its own count and opens on demand. Dead-lettered
 * only by default: a list that also carried every healthy scheduled timer would
 * bury the thing it exists to surface.
 */
export function StuckJobsPanel() {
  const [open, setOpen] = useState(false);

  const {
    data: jobs = [],
    isLoading,
    error
  } = useQuery<WorkflowJob[]>({
    queryKey: ["executions", "jobs", "stuck"],
    queryFn: ({ signal }) => listStuckJobs(false, signal),
    // Live, like the per-execution panel. A cached "nothing is stuck" is the one
    // answer an operator cannot act on and cannot tell from the truth.
    staleTime: 0
  });

  // An unreachable engine is NOT "nothing is stuck". Said out loud rather than
  // rendered as a reassuring zero.
  if (error) {
    return (
      // role="alert" IS right here, and it is the only place on this panel it is:
      // "we could not ask" is a failure the operator must not read past, where
      // "three things are stuck" is a condition they will come back to.
      <Alert color="red" variant="light" role="alert" title="Stuck work could not be read">
        The workflow engine did not answer, so it is not known whether anything is
        stuck. This is <strong>not</strong> the same as nothing being stuck.{" "}
        {describeError(error)}
      </Alert>
    );
  }

  if (isLoading || jobs.length === 0) {
    // Silent on a healthy system. The per-execution Jobs &amp; Timers tab is
    // where "is THIS one stuck?" gets asked and answered; this one exists to
    // interrupt, so it should not speak when it has nothing to say.
    return null;
  }

  return (
    // role="status", NOT Mantine's default role="alert".
    //
    // Stuck work is a standing CONDITION, not an event: it is true on every load
    // of this page until someone acts on it. An assertive region would interrupt
    // whatever a screen-reader user was reading, every single time they came
    // back — and ExecutionFreshnessIndicatorTests asserts no alert banner on this
    // page for precisely that reason. It caught this.
    //
    // The colour still says "red", which is how a sighted reader sees urgency;
    // the role says "announce when you get to it", which is how everyone else
    // should.
    <Alert
      color="red"
      variant="light"
      role="status"
      title={`${jobs.length} stuck ${jobs.length === 1 ? "job" : "jobs"}`}
    >
      <Group justify="space-between" align="center" wrap="nowrap">
        <Text size="sm">
          These steps have exhausted their retries. Nothing will run them until someone
          asks the engine to try again.
        </Text>
        <Button
          size="xs"
          variant="light"
          color="red"
          aria-expanded={open}
          onClick={() => setOpen((was) => !was)}
        >
          {open ? "Hide" : "Show"}
        </Button>
      </Group>

      {open && (
        <Table mt="sm" striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th scope="col">Execution</Table.Th>
              <Table.Th scope="col">Step</Table.Th>
              <Table.Th scope="col">State</Table.Th>
              <Table.Th scope="col">Failure</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {jobs.map((job) => (
              <Table.Tr key={job.id}>
                <Table.Td>
                  {job.processInstanceId ? (
                    // Links into the execution, where the controls are. This
                    // list says WHAT is stuck; acting on it belongs next to the
                    // diagram and history that explain why.
                    <Anchor
                      component={Link}
                      to={`/executions/${encodeURIComponent(job.processInstanceId)}`}
                      size="sm"
                    >
                      {job.processInstanceId}
                    </Anchor>
                  ) : (
                    <Text size="sm" c="dimmed">
                      —
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{job.elementName ?? job.elementId ?? "(unnamed step)"}</Text>
                </Table.Td>
                <Table.Td>
                  <Badge color="red" variant="light">
                    Stuck
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <Text size="sm" lineClamp={2}>
                    {job.exceptionMessage ?? "—"}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Alert>
  );
}
