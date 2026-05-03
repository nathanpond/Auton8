import { useCallback, useMemo } from "react";
import { Link } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import {
  ASSIGNED_TASKS_QUERY_KEY,
  TEAM_TASKS_QUERY_KEY,
  useTeamAssignedTasks
} from "@/hooks/useExecutions";
import { useBusConnection } from "@/hooks/useBusConnection";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { FlowableTaskSummary } from "@/types/flowable";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import UserBadge from "@/pages/records/UserBadge";

export default function TeamTasksPanel() {
  const qc = useQueryClient();
  const { data: statusAppearance = [] } = useStatusAppearance();
  const {
    data: tasks = [],
    isLoading,
    isError
  } = useTeamAssignedTasks();

  // Re-fetch on workflow-execution bus events. A reassignment can move a task
  // in or out of the team queue, and the actor's own queue, so both keys are
  // invalidated together.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      const topic = msg.topic ?? "";
      if (topic.startsWith("workflow.execution")) {
        qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
        qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  const sortedTasks = useMemo(
    () =>
      [...tasks].sort(
        (a, b) => parseTime(b.createdAtUtc) - parseTime(a.createdAtUtc)
      ),
    [tasks]
  );

  const empty = !isLoading && !isError && sortedTasks.length === 0;

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">
          <i className="fa fa-users me-2"></i>Team Tasks
        </h4>
      </div>
      <div className="panel-body">
        <div className="table-responsive">
          <table className="table table-striped table-bordered align-middle mb-0">
            <thead>
              <tr>
                <th>Name</th>
                <th style={{ width: "10rem" }}>Status</th>
                <th style={{ width: "14rem" }}>Type</th>
                <th style={{ width: "12rem" }}>Assignee</th>
                <th style={{ width: "8rem" }}>Due Date</th>
                <th style={{ width: "12rem" }}>Last Updated</th>
                <th style={{ width: "8rem" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={7} className="text-center text-body text-opacity-50 p-4">
                    Loading...
                  </td>
                </tr>
              )}
              {!isLoading && isError && (
                <tr>
                  <td colSpan={7} className="text-center text-danger p-4">
                    Failed to load team tasks.
                  </td>
                </tr>
              )}
              {empty && (
                <tr>
                  <td colSpan={7} className="text-center text-body text-opacity-50 p-4">
                    No tasks are assigned to anyone you supervise.
                  </td>
                </tr>
              )}
              {!isLoading && !isError && sortedTasks.map((task) => (
                <TeamWorkflowRow
                  key={task.id}
                  task={task}
                  statusAppearance={statusAppearance}
                />
              ))}
            </tbody>
          </table>
        </div>
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

function TeamWorkflowRow({
  task,
  statusAppearance
}: {
  task: FlowableTaskSummary;
  statusAppearance: StatusAppearanceEntry[];
}) {
  const workflowName =
    task.processInstanceName ??
    task.processDefinitionName ??
    task.processDefinitionId ??
    task.name ??
    task.id;
  const activeNode = task.name?.trim() ? task.name : task.taskDefinitionKey ?? null;
  return (
    <tr>
      <td>
        {task.processInstanceId ? (
          <Link to={`/executions/${task.processInstanceId}`} className="text-decoration-none">
            {workflowName}
          </Link>
        ) : (
          <span>{workflowName}</span>
        )}
      </td>
      <td>
        {activeNode ? (
          <span
            className="badge rounded-pill"
            style={statusBadgeStyle(activeNode, statusAppearance)}
          >
            {activeNode}
          </span>
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
      <td>
        <i className="fa fa-diagram-project me-2"></i>
        <span>{task.processDefinitionName ?? task.processDefinitionId ?? "Workflow"}</span>
      </td>
      <td>
        <UserBadge userId={task.assignee} />
      </td>
      <td>
        {task.dueDate ? (
          formatDateTime(task.dueDate)
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
      <td>{formatWhen(task.createdAtUtc)}</td>
      <td>
        {task.processInstanceId ? (
          <Link
            to={`/executions/${task.processInstanceId}`}
            className="btn btn-sm btn-outline-primary"
          >
            View
          </Link>
        ) : (
          <span className="text-body text-opacity-50 small">—</span>
        )}
      </td>
    </tr>
  );
}

function parseTime(iso: string | null | undefined): number {
  if (!iso) return 0;
  const t = new Date(iso).getTime();
  return Number.isNaN(t) ? 0 : t;
}

function formatWhen(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}
