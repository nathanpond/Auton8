import { useCallback, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useMyAssignedRecords } from "@/hooks/useRecords";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import {
  ASSIGNED_TASKS_QUERY_KEY,
  useCompleteTask,
  useTasksVisibleToMe,
  VISIBLE_TASKS_QUERY_KEY
} from "@/hooks/useExecutions";
import { useBusConnection } from "@/hooks/useBusConnection";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { RecordModel, RecordType } from "@/types/records";
import { FlowableTaskSummary } from "@/types/flowable";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { findIcon, preferredStyle, stripFaPrefix } from "@/lib/faIcons";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";

const PAGE_SIZE = 10;

type TaskRow =
  | {
      kind: "record";
      id: string;
      sortKey: number;
      record: RecordModel;
      type: RecordType | null;
    }
  | {
      kind: "workflow";
      id: string;
      sortKey: number;
      task: FlowableTaskSummary;
    };

export default function MyTasksPanel() {
  const qc = useQueryClient();
  const params = useMemo(
    () => ({ page: 0, pageSize: PAGE_SIZE, sort: "updated_desc" as const, includeArchived: false }),
    []
  );
  const { data: recordPage, isLoading: recordsLoading, isError: recordsError } =
    useMyAssignedRecords(params);
  const { data: types = [] } = useRecordTypes(true);
  const { data: statusAppearance = [] } = useStatusAppearance();
  const {
    data: workflowTasks = [],
    isLoading: tasksLoading,
    isError: tasksError
  } = useTasksVisibleToMe();
  const completeTask = useCompleteTask();
  const [completingTaskId, setCompletingTaskId] = useState<string | null>(null);

  // Refetch on any record or workflow-execution bus event. The server-side
  // /assigned-to-me and /visible-to-me endpoints already filter by the current
  // user, so we don't need to inspect payloads to decide whether to act —
  // assignments and reassignments both flow through these topics.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      const topic = msg.topic ?? "";
      if (topic.startsWith("record.")) {
        qc.invalidateQueries({ queryKey: ["records", "assigned-to-me"] });
      } else if (topic.startsWith("workflow.execution")) {
        qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
        qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  // Whether the current actor can complete each visible task. Tasks the
  // actor sees only because they supervise the assignee return false here,
  // so we render those rows without the Complete button.
  const completeChecks = useMemo(
    () => workflowTasks.map((t) => ({
      kind: "workflowtask",
      action: "complete",
      id: t.id
    })),
    [workflowTasks]
  );
  const { data: completePermissions } = usePermissionChecks(completeChecks);

  const onCompleteTask = async (taskId: string) => {
    setCompletingTaskId(taskId);
    try {
      await completeTask.mutateAsync({ taskId });
    } finally {
      setCompletingTaskId(null);
    }
  };

  const typesById = useMemo(() => {
    const map = new Map<string, RecordType>();
    for (const t of types) map.set(t.id, t);
    return map;
  }, [types]);

  const recordItems = recordPage?.items ?? [];
  const totalRecordCount = recordPage?.totalCount ?? 0;

  const rows = useMemo<TaskRow[]>(() => {
    const recordRows: TaskRow[] = recordItems.map((rec) => ({
      kind: "record",
      id: `record:${rec.id}`,
      sortKey: parseTime(rec.updatedAtUtc),
      record: rec,
      type: typesById.get(rec.recordTypeId) ?? null
    }));
    const workflowRows: TaskRow[] = workflowTasks.map((task) => ({
      kind: "workflow",
      id: `workflow:${task.id}`,
      sortKey: parseTime(task.createdAtUtc),
      task
    }));
    return [...recordRows, ...workflowRows].sort((a, b) => b.sortKey - a.sortKey);
  }, [recordItems, workflowTasks, typesById]);

  const isLoading = recordsLoading || tasksLoading;
  const hasError = recordsError || tasksError;
  const empty = !isLoading && !hasError && rows.length === 0;

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">
          <i className="fa fa-user-check me-2"></i>My Tasks
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
                <th>Description</th>
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
              {!isLoading && hasError && (
                <tr>
                  <td colSpan={7} className="text-center text-danger p-4">
                    Failed to load tasks.
                  </td>
                </tr>
              )}
              {empty && (
                <tr>
                  <td colSpan={7} className="text-center text-body text-opacity-50 p-4">
                    Nothing is assigned to you right now.
                  </td>
                </tr>
              )}
              {!isLoading && !hasError && rows.map((row) =>
                row.kind === "record" ? (
                  <RecordRow
                    key={row.id}
                    record={row.record}
                    type={row.type}
                    statusAppearance={statusAppearance}
                  />
                ) : (
                  <WorkflowRow
                    key={row.id}
                    task={row.task}
                    onComplete={onCompleteTask}
                    isCompleting={completingTaskId === row.task.id}
                    canComplete={
                      completePermissions?.get(
                        permissionKey({ kind: "workflowtask", action: "complete", id: row.task.id })
                      ) ?? false
                    }
                    statusAppearance={statusAppearance}
                  />
                )
              )}
            </tbody>
          </table>
        </div>
        {totalRecordCount > recordItems.length && (
          <div className="text-body text-opacity-75 small mt-3">
            Showing {recordItems.length} of {totalRecordCount} assigned records.
          </div>
        )}
      </div>
    </div>
  );
}

function RecordRow({
  record,
  type,
  statusAppearance
}: {
  record: RecordModel;
  type: RecordType | null;
  statusAppearance: StatusAppearanceEntry[];
}) {
  const description = readDescription(record.values);
  return (
    <tr>
      <td>
        <Link to={`/record/${record.key}`} className="text-decoration-none">
          <code className="me-2">{record.key}</code>
          {record.name}
        </Link>
      </td>
      <td>
        {record.status ? (
          <span
            className="badge rounded-pill"
            style={statusBadgeStyle(record.status, statusAppearance)}
          >
            {record.status}
          </span>
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
      <td>
        {type ? (
          <>
            {type.icon ? (
              <i
                className={`${resolveIconClass(type.icon)} me-2`}
                style={type.color ? { color: type.color } : undefined}
                aria-hidden="true"
              ></i>
            ) : null}
            <span>{type.name}</span>
          </>
        ) : (
          <span className="text-body text-opacity-50 small">Unknown</span>
        )}
      </td>
      <td>
        {description ? (
          <span className="small">{description}</span>
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
      <td>
        {record.dueDate ? (
          formatDate(record.dueDate)
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
      <td>{formatWhen(record.updatedAtUtc)}</td>
      <td></td>
    </tr>
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

function WorkflowRow({
  task,
  onComplete,
  isCompleting,
  canComplete,
  statusAppearance
}: {
  task: FlowableTaskSummary;
  onComplete: (taskId: string) => void;
  isCompleting: boolean;
  canComplete: boolean;
  statusAppearance: StatusAppearanceEntry[];
}) {
  // Prefer the per-execution display name (set at start time) over the
  // process definition name. Falls back through definition name → id → task
  // fields so we always show *something*.
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
        <span className="text-body text-opacity-50">—</span>
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
        {canComplete ? (
          <button
            type="button"
            className="btn btn-sm btn-success"
            onClick={() => onComplete(task.id)}
            disabled={isCompleting}
          >
            Complete
          </button>
        ) : (
          <span className="text-body text-opacity-50 small">View only</span>
        )}
      </td>
    </tr>
  );
}

function resolveIconClass(icon: string): string {
  const found = findIcon(icon);
  if (found) return `${preferredStyle(found)} fa-${found.name}`;
  const name = stripFaPrefix(icon);
  return `fa-solid fa-${name}`;
}

function readDescription(values: Record<string, unknown>): string | null {
  const raw = values?.description ?? values?.Description;
  if (typeof raw !== "string") return null;
  const trimmed = raw.trim();
  return trimmed.length === 0 ? null : trimmed;
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

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}
