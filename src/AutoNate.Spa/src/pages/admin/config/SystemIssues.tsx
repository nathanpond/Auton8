import { useMemo, useState } from "react";
import { isAxiosError } from "axios";
import {
  SystemIssueCategory,
  SystemIssueModel,
  SystemIssueSeverity,
  SystemIssueState
} from "@/api/systemIssues";
import {
  useAcknowledgeSystemIssue,
  useResolveSystemIssue,
  useSystemIssue,
  useSystemIssues
} from "@/hooks/useSystemIssues";

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

export default function SystemIssues() {
  const [state, setState] = useState<SystemIssueState | "">("open");
  const [severity, setSeverity] = useState<SystemIssueSeverity | "">("");
  const [category, setCategory] = useState<SystemIssueCategory | "">("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const list = useSystemIssues({
    state,
    severity: severity || undefined,
    category: category || undefined
  });
  const detail = useSystemIssue(selectedId);

  const items = list.data?.items ?? [];

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start gap-3">
        <div>
          <h1 className="page-header mb-1">System Issues</h1>
          <p className="page-head-copy mb-0">
            Persistent log of issues detectors have surfaced. Refreshes every 15 seconds. Phase 1
            ships read-only; acknowledge / resolve / auto-remediate land in subsequent phases.
          </p>
        </div>
        {list.isFetching && (
          <div className="text-muted small">
            <i className="fa fa-rotate fa-spin me-1" aria-hidden="true" />
            Refreshing…
          </div>
        )}
      </div>

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Filters</h4>
        </div>
        <div className="panel-body">
          <div className="row g-2 align-items-end">
            <div className="col-sm-4">
              <label className="form-label small text-muted">State</label>
              <select
                className="form-select form-select-sm"
                value={state}
                onChange={(e) => setState(e.target.value as SystemIssueState | "")}
              >
                {STATE_OPTIONS.map((opt) => (
                  <option key={opt.value || "all"} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-sm-4">
              <label className="form-label small text-muted">Severity</label>
              <select
                className="form-select form-select-sm"
                value={severity}
                onChange={(e) => setSeverity(e.target.value as SystemIssueSeverity | "")}
              >
                {SEVERITY_OPTIONS.map((opt) => (
                  <option key={opt.value || "any"} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-sm-4">
              <label className="form-label small text-muted">Category</label>
              <select
                className="form-select form-select-sm"
                value={category}
                onChange={(e) => setCategory(e.target.value as SystemIssueCategory | "")}
              >
                {CATEGORY_OPTIONS.map((opt) => (
                  <option key={opt.value || "any"} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>
      </div>

      {list.isError && (
        <div className="alert alert-danger">
          {list.error instanceof Error ? list.error.message : "Failed to load system issues."}
        </div>
      )}

      <div className="panel panel-inverse">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title">Issues</h4>
          <span className="text-muted small">{items.length} shown</span>
        </div>
        <div className="panel-body p-0">
          {list.isLoading ? (
            <div className="p-3 text-muted">Loading…</div>
          ) : items.length === 0 ? (
            <div className="p-3 text-muted">
              No issues match the current filters. With the default <code>state=open</code>{" "}
              filter, an empty list means everything detectors can currently see is healthy.
            </div>
          ) : (
            <div className="table-responsive">
              <table className="table table-hover mb-0 align-middle">
                <thead>
                  <tr>
                    <th style={{ width: "90px" }}>Severity</th>
                    <th>Title</th>
                    <th style={{ width: "140px" }}>Category</th>
                    <th style={{ width: "180px" }}>Detector</th>
                    <th style={{ width: "70px" }}>Count</th>
                    <th style={{ width: "180px" }}>Last seen</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr
                      key={item.id}
                      style={{ cursor: "pointer" }}
                      onClick={() => setSelectedId(item.id)}
                    >
                      <td>
                        <SeverityBadge severity={item.severity} />
                      </td>
                      <td>
                        <div className="fw-semibold">{item.title}</div>
                        {item.summary && <div className="text-muted small">{item.summary}</div>}
                      </td>
                      <td className="small text-muted">{item.category}</td>
                      <td className="small text-muted">{item.detectorId}</td>
                      <td>{item.occurrenceCount}</td>
                      <td className="small text-muted">
                        {new Date(item.lastSeenAtUtc).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

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
