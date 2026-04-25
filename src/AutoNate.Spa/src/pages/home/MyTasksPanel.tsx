import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useMyAssignedRecords } from "@/hooks/useRecords";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { useCompleteTask, useMyAssignedTasks } from "@/hooks/useExecutions";

const PAGE_SIZE = 10;

export default function MyTasksPanel() {
  const params = useMemo(
    () => ({ page: 0, pageSize: PAGE_SIZE, sort: "updated_desc" as const, includeArchived: false }),
    []
  );
  const { data, isLoading, isError } = useMyAssignedRecords(params);
  const { data: types = [] } = useRecordTypes(true);
  const {
    data: workflowTasks = [],
    isLoading: tasksLoading,
    isError: tasksError
  } = useMyAssignedTasks();
  const completeTask = useCompleteTask();
  const [completingTaskId, setCompletingTaskId] = useState<string | null>(null);

  const typesById = useMemo(() => {
    const map = new Map<string, { shortCode: string; name: string }>();
    for (const t of types) map.set(t.id, { shortCode: t.shortCode, name: t.name });
    return map;
  }, [types]);

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  const onCompleteTask = async (taskId: string) => {
    setCompletingTaskId(taskId);
    try {
      await completeTask.mutateAsync({ taskId });
    } finally {
      setCompletingTaskId(null);
    }
  };

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
                <th style={{ width: "8rem" }}>Key</th>
                <th style={{ width: "10rem" }}>Type</th>
                <th>Name</th>
                <th style={{ width: "12rem" }}>Updated</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                    Loading...
                  </td>
                </tr>
              )}
              {!isLoading && isError && (
                <tr>
                  <td colSpan={4} className="text-center text-danger p-4">
                    Failed to load tasks.
                  </td>
                </tr>
              )}
              {!isLoading && !isError && items.length === 0 && (
                <tr>
                  <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                    Nothing is assigned to you right now.
                  </td>
                </tr>
              )}
              {items.map((rec) => {
                const type = typesById.get(rec.recordTypeId);
                return (
                  <tr key={rec.id}>
                    <td>
                      <Link to={`/record/${rec.key}`}>
                        <code>{rec.key}</code>
                      </Link>
                    </td>
                    <td>
                      {type ? (
                        <Link to={`/records/${type.shortCode}`} className="text-decoration-none">
                          <span className="badge bg-secondary me-1">{type.shortCode}</span>
                          <span className="small">{type.name}</span>
                        </Link>
                      ) : (
                        <span className="text-body text-opacity-50 small">Unknown</span>
                      )}
                    </td>
                    <td>
                      <Link to={`/record/${rec.key}`} className="text-decoration-none">
                        {rec.name}
                      </Link>
                    </td>
                    <td>{formatWhen(rec.updatedAtUtc)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        {totalCount > items.length && (
          <div className="text-body text-opacity-75 small mt-3">
            Showing {items.length} of {totalCount} assigned records.
          </div>
        )}

        <h5 className="mt-4 mb-2">
          <i className="fa fa-diagram-project me-2"></i>Workflow tasks
        </h5>
        <div className="table-responsive">
          <table className="table table-striped table-bordered align-middle mb-0">
            <thead>
              <tr>
                <th>Task</th>
                <th style={{ width: "16rem" }}>Workflow</th>
                <th style={{ width: "12rem" }}>Created</th>
                <th style={{ width: "8rem" }}></th>
              </tr>
            </thead>
            <tbody>
              {tasksLoading && (
                <tr>
                  <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                    Loading...
                  </td>
                </tr>
              )}
              {!tasksLoading && tasksError && (
                <tr>
                  <td colSpan={4} className="text-center text-danger p-4">
                    Failed to load workflow tasks.
                  </td>
                </tr>
              )}
              {!tasksLoading && !tasksError && workflowTasks.length === 0 && (
                <tr>
                  <td colSpan={4} className="text-center text-body text-opacity-50 p-4">
                    No workflow tasks are waiting for you.
                  </td>
                </tr>
              )}
              {workflowTasks.map((task) => (
                <tr key={task.id}>
                  <td>
                    {task.processInstanceId ? (
                      <Link
                        to={`/executions/${task.processInstanceId}`}
                        className="text-decoration-none"
                      >
                        {task.name || task.id}
                      </Link>
                    ) : (
                      <span>{task.name || task.id}</span>
                    )}
                  </td>
                  <td>
                    <span className="small">
                      {task.processDefinitionName ?? task.processDefinitionId ?? "—"}
                    </span>
                  </td>
                  <td>{formatWhen(task.createdAtUtc)}</td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-sm btn-success"
                      onClick={() => onCompleteTask(task.id)}
                      disabled={completingTaskId === task.id}
                    >
                      Complete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function formatWhen(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
