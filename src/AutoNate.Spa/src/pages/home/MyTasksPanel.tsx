import { useCallback, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useMyAssignedRecords } from "@/hooks/useRecords";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import {
  ASSIGNED_TASKS_QUERY_KEY,
  TEAM_TASKS_QUERY_KEY,
  taskFormConfigQueryKey,
  useCompleteTask,
  useMyAssignedTasks
} from "@/hooks/useExecutions";
import { getTaskFormConfig, TaskFormConfig } from "@/api/executions";
import { useBusConnection } from "@/hooks/useBusConnection";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { RecordModel, RecordType } from "@/types/records";
import { FlowableTaskSummary } from "@/types/flowable";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { findIcon, preferredStyle, stripFaPrefix } from "@/lib/faIcons";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import SimpleCompleteTaskModal from "@/components/workflow/SimpleCompleteTaskModal";
import TaskFormModal from "@/components/workflow/TaskFormModal";

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
  } = useMyAssignedTasks();
  const completeTask = useCompleteTask();
  const navigate = useNavigate();
  const [openingTaskId, setOpeningTaskId] = useState<string | null>(null);
  const [activeTaskConfig, setActiveTaskConfig] = useState<TaskFormConfig | null>(null);
  const [openError, setOpenError] = useState<string | null>(null);

  // Refetch on any record or workflow-execution bus event. The server-side
  // /assigned-to-me endpoints already filter by the current user, so we don't
  // need to inspect payloads to decide whether to act — assignments and
  // reassignments both flow through these topics. Team Tasks is invalidated
  // too since reassignments may move work in or out of a supervisee's queue.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      const topic = msg.topic ?? "";
      if (topic.startsWith("record.")) {
        qc.invalidateQueries({ queryKey: ["records", "assigned-to-me"] });
      } else if (topic.startsWith("workflow.execution")) {
        qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
        qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  // Clicking Open dispatches on the task's userForm config (set in
  // Workflow Studio). Simple → confirm modal in place, Modal → form modal
  // in place, Page → navigate to the dedicated task-form route.
  const onOpenTask = async (taskId: string) => {
    setOpeningTaskId(taskId);
    setOpenError(null);
    try {
      const config = await qc.fetchQuery({
        queryKey: taskFormConfigQueryKey(taskId),
        queryFn: ({ signal }) => getTaskFormConfig(taskId, signal)
      });
      if (!config) {
        setOpenError("Task not found or already completed.");
        return;
      }
      if (config.mode === "page") {
        navigate(`/workflow-tasks/${encodeURIComponent(taskId)}/form`);
        return;
      }
      setActiveTaskConfig(config);
    } catch (err) {
      setOpenError(describeError(err));
    } finally {
      setOpeningTaskId(null);
    }
  };

  const closeActiveTask = () => setActiveTaskConfig(null);

  const completeFromModal = useCallback(
    async (taskId: string, variables?: Record<string, unknown>) => {
      await completeTask.mutateAsync({ taskId, variables });
    },
    [completeTask]
  );

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
                    onOpen={onOpenTask}
                    isOpening={openingTaskId === row.task.id}
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
        {openError && (
          <div className="alert alert-danger mt-3" role="alert">
            {openError}
          </div>
        )}
      </div>

      {activeTaskConfig?.mode === "simple" && (
        <SimpleCompleteTaskModal
          config={activeTaskConfig}
          onClose={closeActiveTask}
          onComplete={(taskId) => completeFromModal(taskId)}
        />
      )}
      {activeTaskConfig?.mode === "modal" && (
        <TaskFormModal
          config={activeTaskConfig}
          onClose={closeActiveTask}
          onComplete={(taskId, variables) => completeFromModal(taskId, variables)}
        />
      )}
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
  onOpen,
  isOpening,
  statusAppearance
}: {
  task: FlowableTaskSummary;
  onOpen: (taskId: string) => void;
  isOpening: boolean;
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
        <button
          type="button"
          className="btn btn-sm btn-primary"
          onClick={() => onOpen(task.id)}
          disabled={isOpening}
        >
          {isOpening ? (
            <>
              <i className="fa fa-spinner fa-spin me-1" />
              Opening…
            </>
          ) : (
            "Open"
          )}
        </button>
      </td>
    </tr>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
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
