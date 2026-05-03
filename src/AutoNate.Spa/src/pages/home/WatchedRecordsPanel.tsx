import { useCallback, useMemo } from "react";
import { Link } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useWatchedRecords } from "@/hooks/useRecords";
import { useStatusAppearance } from "@/hooks/useStatusAppearance";
import { useBusConnection } from "@/hooks/useBusConnection";
import { WatchedRecord } from "@/api/records";
import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { badgeTextColor, resolveStatusBadgeColor } from "@/lib/statusAppearance";
import UserBadge from "@/pages/records/UserBadge";

const PAGE_SIZE = 10;

export default function WatchedRecordsPanel() {
  const qc = useQueryClient();
  const params = useMemo(() => ({ page: 0, pageSize: PAGE_SIZE }), []);
  const { data: page, isLoading, isError } = useWatchedRecords(params);
  const { data: statusAppearance = [] } = useStatusAppearance();

  // Refetch when records change so a watched record's status / due date /
  // name updates surface here without a manual reload.
  const onBusMessage = useCallback(
    (msg: { topic: string }) => {
      if ((msg.topic ?? "").startsWith("record.")) {
        qc.invalidateQueries({ queryKey: ["records", "watched-by-me"] });
      }
    },
    [qc]
  );
  useBusConnection({ onMessage: onBusMessage });

  const items = page?.items ?? [];
  const totalCount = page?.totalCount ?? 0;
  const empty = !isLoading && !isError && items.length === 0;

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">
          <i className="fa fa-eye me-2"></i>Watched Records
        </h4>
      </div>
      <div className="panel-body">
        <div className="table-responsive">
          <table className="table table-striped table-bordered align-middle mb-0">
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th style={{ width: "10rem" }}>Status</th>
                <th style={{ width: "12rem" }}>Assigned To</th>
                <th style={{ width: "8rem" }}>Due Date</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={5} className="text-center text-body text-opacity-50 p-4">
                    Loading...
                  </td>
                </tr>
              )}
              {!isLoading && isError && (
                <tr>
                  <td colSpan={5} className="text-center text-danger p-4">
                    Failed to load watched records.
                  </td>
                </tr>
              )}
              {empty && (
                <tr>
                  <td colSpan={5} className="text-center text-body text-opacity-50 p-4">
                    You aren't watching any records yet. Open a record and click "Watch" to add it here.
                  </td>
                </tr>
              )}
              {!isLoading && !isError && items.map((row) => (
                <WatchedRow key={row.id} record={row} statusAppearance={statusAppearance} />
              ))}
            </tbody>
          </table>
        </div>
        {totalCount > items.length && (
          <div className="text-body text-opacity-75 small mt-3">
            Showing {items.length} of {totalCount} watched records.
          </div>
        )}
      </div>
    </div>
  );
}

function WatchedRow({
  record,
  statusAppearance
}: {
  record: WatchedRecord;
  statusAppearance: StatusAppearanceEntry[];
}) {
  return (
    <tr>
      <td>
        <Link to={`/record/${record.key}`} className="text-decoration-none">
          <code className="me-2">{record.key}</code>
          {record.name}
          {record.isArchived && <span className="badge bg-secondary ms-2">Archived</span>}
        </Link>
      </td>
      <td>
        {record.description ? (
          <span className="small">{record.description}</span>
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
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
        {record.assigneeIds.length > 0 ? (
          <span className="d-inline-flex flex-wrap gap-1">
            {record.assigneeIds.map((id, i) => (
              <span key={id}>
                <UserBadge userId={id} />
                {i < record.assigneeIds.length - 1 ? "," : ""}
              </span>
            ))}
          </span>
        ) : (
          <span className="text-body text-opacity-50">Unassigned</span>
        )}
      </td>
      <td>
        {record.dueDate ? (
          formatDate(record.dueDate)
        ) : (
          <span className="text-body text-opacity-50">—</span>
        )}
      </td>
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

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}
