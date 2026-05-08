import { useMemo, useState } from "react";
import { isAxiosError } from "axios";
import { ColumnDef } from "@tanstack/react-table";
import {
  SystemIssueCategory,
  SystemIssueModel,
  SystemIssueSeverity,
  SystemIssueState,
  listSystemIssues
} from "@/api/systemIssues";
import {
  useAcknowledgeSystemIssue,
  useResolveSystemIssue,
  useSystemIssue
} from "@/hooks/useSystemIssues";
import { DataTable } from "@/components/data-table/DataTable";

const STATE_OPTIONS: { value: SystemIssueState | ""; label: string }[] = [
  { value: "open", label: "Open" },
  { value: "acknowledged", label: "Acknowledged" },
  { value: "auto_resolved", label: "Auto-resolved" },
  { value: "resolved", label: "Resolved" },
  { value: "", label: "All states" }
];

const SEVERITY_OPTIONS: { value: SystemIssueSeverity | ""; label: string }[] = [
  { value: "", label: "Any severity" },
  { value: "critical", label: "Critical" },
  { value: "error", label: "Error" },
  { value: "warning", label: "Warning" },
  { value: "info", label: "Info" }
];

const CATEGORY_OPTIONS: { value: SystemIssueCategory | ""; label: string }[] = [
  { value: "", label: "Any category" },
  { value: "data_integrity", label: "Data integrity" },
  { value: "workflow", label: "Workflow" },
  { value: "bus", label: "Bus" },
  { value: "auth", label: "Auth" },
  { value: "config", label: "Config" },
  { value: "resource", label: "Resource" },
  { value: "plugin", label: "Plugin" }
];

const COLUMN_WIDTHS = ["10%", "38%", "13%", "16%", "8%", "15%"];

export default function SystemIssues() {
  const [state, setState] = useState<SystemIssueState | "">("open");
  const [severity, setSeverity] = useState<SystemIssueSeverity | "">("");
  const [category, setCategory] = useState<SystemIssueCategory | "">("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const detail = useSystemIssue(selectedId);

  const columns = useMemo<ColumnDef<SystemIssueModel>[]>(
    () => [
      {
        id: "severity",
        accessorKey: "severity",
        header: "Severity",
        cell: ({ row }) => <SeverityBadge severity={row.original.severity} />
      },
      {
        id: "title",
        accessorKey: "title",
        header: "Title",
        cell: ({ row }) => (
          <>
            <div className="fw-semibold">{row.original.title}</div>
            {row.original.summary && (
              <div className="text-muted small">{row.original.summary}</div>
            )}
          </>
        )
      },
      {
        id: "category",
        accessorKey: "category",
        header: "Category",
        cell: ({ row }) => <span className="small text-muted">{row.original.category}</span>
      },
      {
        id: "detectorId",
        accessorKey: "detectorId",
        header: "Detector",
        cell: ({ row }) => <span className="small text-muted">{row.original.detectorId}</span>
      },
      {
        id: "occurrenceCount",
        accessorKey: "occurrenceCount",
        header: "Count"
      },
      {
        id: "lastSeenAtUtc",
        accessorKey: "lastSeenAtUtc",
        header: "Last seen",
        cell: ({ row }) => (
          <span className="small text-muted">
            {new Date(row.original.lastSeenAtUtc).toLocaleString()}
          </span>
        )
      }
    ],
    []
  );

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">System Issues</h1>
        <p className="page-head-copy">
          Persistent log of issues detectors have surfaced. Refreshes every 15 seconds.
        </p>
      </div>

      <DataTable<SystemIssueModel>
        mode="client"
        loadAll={async () => {
          const r = await listSystemIssues({
            state,
            severity: severity || undefined,
            category: category || undefined
          });
          return r.items;
        }}
        // Filter selections are part of the backend request, so they go in
        // the queryKey so react-query refetches when any facet changes.
        queryKey={["system-issues", { state, severity, category }]}
        refetchInterval={15_000}
        columns={columns}
        rowKey={(i) => i.id}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "lastSeenAtUtc", desc: true }]}
        searchPlaceholder="Search issues…"
        emptyMessage="No issues match the current filters."
        loadingMessage="Loading issues…"
        onRowClick={(i) => setSelectedId(i.id)}
        getRowAriaLabel={(i) => `Open ${i.title}`}
        globalFilterFn={(i, search) => {
          const needle = search.toLowerCase();
          return `${i.title} ${i.summary ?? ""} ${i.category} ${i.detectorId}`
            .toLowerCase()
            .includes(needle);
        }}
        toolbarLeft={
          <div className="d-flex align-items-center gap-2">
            <select
              className="form-select form-select-sm"
              style={{ width: "auto" }}
              value={state}
              onChange={(e) => setState(e.target.value as SystemIssueState | "")}
              aria-label="Filter by state"
            >
              {STATE_OPTIONS.map((opt) => (
                <option key={opt.value || "all"} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
            <select
              className="form-select form-select-sm"
              style={{ width: "auto" }}
              value={severity}
              onChange={(e) => setSeverity(e.target.value as SystemIssueSeverity | "")}
              aria-label="Filter by severity"
            >
              {SEVERITY_OPTIONS.map((opt) => (
                <option key={opt.value || "any"} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
            <select
              className="form-select form-select-sm"
              style={{ width: "auto" }}
              value={category}
              onChange={(e) => setCategory(e.target.value as SystemIssueCategory | "")}
              aria-label="Filter by category"
            >
              {CATEGORY_OPTIONS.map((opt) => (
                <option key={opt.value || "any"} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
        }
      />

      {selectedId && (
        <IssueDetailDrawer
          issue={detail.data ?? null}
          isLoading={detail.isLoading}
          onClose={() => setSelectedId(null)}
        />
      )}
    </>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const cls =
    severity === "critical"
      ? "bg-danger"
      : severity === "error"
        ? "bg-danger"
        : severity === "warning"
          ? "bg-warning text-dark"
          : "bg-secondary";
  return <span className={`badge ${cls}`}>{severity}</span>;
}

function IssueDetailDrawer({
  issue,
  isLoading,
  onClose
}: {
  issue: SystemIssueModel | null;
  isLoading: boolean;
  onClose: () => void;
}) {
  const facts = useMemo(() => prettyJson(issue?.factsJson), [issue?.factsJson]);
  const acknowledge = useAcknowledgeSystemIssue();
  const resolve = useResolveSystemIssue();
  const [resolveNotes, setResolveNotes] = useState("");

  const isOpen = issue?.state === "open";
  const isOpenOrAcknowledged = issue?.state === "open" || issue?.state === "acknowledged";
  const lastError: unknown = acknowledge.error ?? resolve.error;

  return (
    <div className="modal show d-block" tabIndex={-1} style={{ background: "rgba(0,0,0,0.4)" }}>
      <div className="modal-dialog modal-lg modal-dialog-scrollable">
        <div className="modal-content">
          <div className="modal-header">
            <h5 className="modal-title">{issue?.title ?? (isLoading ? "Loading…" : "Issue")}</h5>
            <button type="button" className="btn-close" onClick={onClose} aria-label="Close" />
          </div>
          <div className="modal-body">
            {lastError ? (
              <div className="alert alert-danger py-2 small">{describeError(lastError)}</div>
            ) : null}
            {isLoading || !issue ? (
              <p className="text-muted">Loading…</p>
            ) : (
              <dl className="row mb-0">
                <dt className="col-sm-3">State</dt>
                <dd className="col-sm-9">{issue.state}</dd>

                <dt className="col-sm-3">Severity</dt>
                <dd className="col-sm-9">
                  <SeverityBadge severity={issue.severity} />
                </dd>

                <dt className="col-sm-3">Category</dt>
                <dd className="col-sm-9">{issue.category}</dd>

                <dt className="col-sm-3">Detector</dt>
                <dd className="col-sm-9">
                  <code>{issue.detectorId}</code>
                </dd>

                <dt className="col-sm-3">Fingerprint</dt>
                <dd className="col-sm-9">
                  <code className="small">{issue.fingerprint}</code>
                </dd>

                {issue.summary && (
                  <>
                    <dt className="col-sm-3">Summary</dt>
                    <dd className="col-sm-9" style={{ whiteSpace: "pre-wrap" }}>
                      {issue.summary}
                    </dd>
                  </>
                )}

                {issue.relatedEntityKind && (
                  <>
                    <dt className="col-sm-3">Related</dt>
                    <dd className="col-sm-9">
                      <code>
                        {issue.relatedEntityKind}/{issue.relatedEntityId ?? "?"}
                      </code>
                    </dd>
                  </>
                )}

                <dt className="col-sm-3">Occurrences</dt>
                <dd className="col-sm-9">{issue.occurrenceCount}</dd>

                <dt className="col-sm-3">First seen</dt>
                <dd className="col-sm-9 small">{new Date(issue.firstSeenAtUtc).toLocaleString()}</dd>

                <dt className="col-sm-3">Last seen</dt>
                <dd className="col-sm-9 small">{new Date(issue.lastSeenAtUtc).toLocaleString()}</dd>

                {issue.resolvedAtUtc && (
                  <>
                    <dt className="col-sm-3">Resolved</dt>
                    <dd className="col-sm-9 small">
                      {new Date(issue.resolvedAtUtc).toLocaleString()} ({issue.resolutionKind})
                    </dd>
                  </>
                )}

                {issue.autoRemediationLastError && (
                  <>
                    <dt className="col-sm-3">Last remediation error</dt>
                    <dd className="col-sm-9 small text-danger">{issue.autoRemediationLastError}</dd>
                  </>
                )}

                <dt className="col-sm-3">Facts</dt>
                <dd className="col-sm-9">
                  <pre className="bg-light p-2 small mb-0" style={{ maxHeight: "300px", overflow: "auto" }}>
                    {facts}
                  </pre>
                </dd>
              </dl>
            )}
          </div>
          <div className="modal-footer flex-wrap gap-2">
            {isOpenOrAcknowledged && (
              <div className="me-auto" style={{ minWidth: "260px", flex: "1 1 260px" }}>
                <input
                  type="text"
                  className="form-control form-control-sm"
                  placeholder="Resolution notes (optional)"
                  value={resolveNotes}
                  onChange={(e) => setResolveNotes(e.target.value)}
                  disabled={resolve.isPending}
                />
              </div>
            )}
            {isOpen && (
              <button
                type="button"
                className="btn btn-outline-primary"
                disabled={!issue || acknowledge.isPending}
                onClick={() => issue && acknowledge.mutate(issue.id)}
              >
                {acknowledge.isPending ? "Acknowledging…" : "Acknowledge"}
              </button>
            )}
            {isOpenOrAcknowledged && (
              <button
                type="button"
                className="btn btn-success"
                disabled={!issue || resolve.isPending}
                onClick={() => {
                  if (!issue) return;
                  resolve.mutate(
                    { id: issue.id, notes: resolveNotes.trim() || undefined },
                    { onSuccess: () => setResolveNotes("") }
                  );
                }}
              >
                {resolve.isPending ? "Resolving…" : "Resolve"}
              </button>
            )}
            <button type="button" className="btn btn-secondary" onClick={onClose}>
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function describeError(err: unknown): string {
  if (isAxiosError(err)) {
    const status = err.response?.status;
    if (status === 403) return "You don't have permission for that action.";
    if (status === 404) return "Issue no longer exists.";
    if (status === 409) {
      const reason = (err.response?.data as { reason?: string } | undefined)?.reason;
      if (reason === "not_open") return "Someone else changed this issue's state. Refresh to see the latest.";
      if (reason === "already_resolved") return "This issue is already resolved.";
      return "State conflict — refresh and try again.";
    }
    return err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}

function prettyJson(raw: string | null | undefined): string {
  if (!raw) return "{}";
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
