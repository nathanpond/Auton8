import { useMemo, useState } from "react";
import { useUsers } from "@/hooks/useUsers";
import { useExplainPermission, useRegistry, useRoles, useGroups } from "@/hooks/useAdmin";
import type { ExplainGrant, ExplainResult } from "@/api/admin";

// Effective permissions debugger. Pick a user, an action, a kind, and an
// optional target id; the server replays the evaluator and returns the
// final allow/deny along with every grant it considered. Useful both as a
// "why did Alice get a 403?" troubleshooter and as a sanity check that a
// new grant or role assignment had the expected effect.
export default function Explain() {
  const { data: users = [] } = useUsers();
  const { data: registry } = useRegistry();
  const { data: roles = [] } = useRoles();
  const { data: groups = [] } = useGroups();
  const explain = useExplainPermission();

  const [asUserId, setAsUserId] = useState("");
  const [targetKind, setTargetKind] = useState("record");
  const [action, setAction] = useState("view");
  const [targetId, setTargetId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ExplainResult | null>(null);

  const kinds = registry?.kinds ?? [];
  const currentKind = kinds.find((k) => k.kind === targetKind);
  const actionOptions = currentKind?.actions ?? [];

  const roleNamesById = useMemo(() => {
    const m = new Map<string, string>();
    for (const r of roles) m.set(r.id, r.name);
    return m;
  }, [roles]);

  const groupNamesById = useMemo(() => {
    const m = new Map<string, string>();
    for (const g of groups) m.set(g.id, g.name);
    return m;
  }, [groups]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setResult(null);

    if (!asUserId) {
      setError("Pick a user first.");
      return;
    }
    if (!targetKind || !action) {
      setError("Pick a kind and an action.");
      return;
    }

    try {
      const r = await explain.mutateAsync({
        asUserId,
        action,
        targetKind,
        targetId: targetId.trim() || null
      });
      setResult(r);
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Effective Permissions</h1>
        <p className="page-head-copy">
          Pick a user, action, and target. The evaluator replays each grant in
          isolation and shows you the matching trace, so you can see exactly
          which rule (or absence of one) drove the decision.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="panel panel-inverse">
        <div className="panel-heading">
          <h4 className="panel-title">Query</h4>
        </div>
        <div className="panel-body">
          <form onSubmit={submit} className="row g-2 align-items-end">
            <div className="col-md-3">
              <label className="form-label small mb-1">User</label>
              <select
                className="form-select form-select-sm"
                value={asUserId}
                onChange={(e) => setAsUserId(e.target.value)}
              >
                <option value="">— pick a user —</option>
                {users.map((u) => (
                  <option key={u.userId} value={u.userId}>
                    {u.username}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-2">
              <label className="form-label small mb-1">Kind</label>
              <select
                className="form-select form-select-sm"
                value={targetKind}
                onChange={(e) => {
                  setTargetKind(e.target.value);
                  setAction("");
                }}
              >
                <option value="">— pick a kind —</option>
                {kinds.map((k) => (
                  <option key={k.kind} value={k.kind}>
                    {k.kind}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-2">
              <label className="form-label small mb-1">Action</label>
              <select
                className="form-select form-select-sm"
                value={action}
                onChange={(e) => setAction(e.target.value)}
              >
                <option value="">— pick an action —</option>
                {actionOptions.map((a) => (
                  <option key={a} value={a}>
                    {a}
                  </option>
                ))}
              </select>
            </div>
            <div className="col-md-3">
              <label className="form-label small mb-1">
                Target id <span className="text-body text-opacity-50">(optional)</span>
              </label>
              <input
                type="text"
                className="form-control form-control-sm font-monospace"
                placeholder="leave blank for kind-level check"
                value={targetId}
                onChange={(e) => setTargetId(e.target.value)}
              />
            </div>
            <div className="col-md-2">
              <button type="submit" className="btn btn-sm btn-primary w-100" disabled={explain.isPending}>
                {explain.isPending ? "Evaluating…" : "Explain"}
              </button>
            </div>
          </form>
        </div>
      </div>

      {result && (
        <ExplanationView
          result={result}
          users={Object.fromEntries(users.map((u) => [u.userId, u.username]))}
          roleNamesById={roleNamesById}
          groupNamesById={groupNamesById}
        />
      )}
    </>
  );
}

function ExplanationView({
  result,
  users,
  roleNamesById,
  groupNamesById
}: {
  result: ExplainResult;
  users: Record<string, string>;
  roleNamesById: Map<string, string>;
  groupNamesById: Map<string, string>;
}) {
  const isAllow = result.effect === "allow";
  return (
    <>
      <div className={`alert ${isAllow ? "alert-success" : "alert-danger"} mt-3`}>
        <strong className="text-uppercase me-2">{result.effect}</strong>
        {result.reason}
        <div className="small mt-1 text-body text-opacity-75">
          User: <strong>{users[result.asUserId] ?? result.asUserId}</strong>
          {result.isSuperAdmin && <span className="badge bg-warning text-dark ms-2">SuperAdmin</span>}
          <span className="ms-3">Groups: {result.groupIds.length}</span>
          <span className="ms-3">Roles: {result.roleIds.length}</span>
        </div>
      </div>

      {!result.isSuperAdmin && (
        <div className="panel panel-inverse">
          <div className="panel-heading">
            <h4 className="panel-title">Grants considered</h4>
          </div>
          <div className="panel-body">
            {result.grants.length === 0 ? (
              <div className="text-body text-opacity-75">
                The user has no grants for this action via direct assignment, group membership, or role.
              </div>
            ) : (
              <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead>
                    <tr>
                      <th>Source</th>
                      <th>Action</th>
                      <th>Selector</th>
                      <th>Effect</th>
                      <th>Matched</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.grants.map((g, i) => (
                      <GrantRow
                        key={i}
                        grant={g}
                        userLabel={users[g.principalId]}
                        roleLabel={roleNamesById.get(g.principalId)}
                        groupLabel={groupNamesById.get(g.principalId)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
}

function GrantRow({
  grant,
  userLabel,
  roleLabel,
  groupLabel
}: {
  grant: ExplainGrant;
  userLabel?: string;
  roleLabel?: string;
  groupLabel?: string;
}) {
  const principalName =
    grant.principalName ??
    (grant.principalKind === "user"
      ? userLabel
      : grant.principalKind === "role"
      ? roleLabel
      : groupLabel) ??
    grant.principalId;

  let matchedCell: React.ReactNode;
  if (grant.matched === true) {
    matchedCell = <span className="badge bg-success">match</span>;
  } else if (grant.matched === false) {
    matchedCell = <span className="text-body text-opacity-50">no match</span>;
  } else {
    matchedCell = (
      <span className="text-warning" title={grant.error ?? "Not evaluated"}>
        n/a {grant.error ? `— ${grant.error}` : ""}
      </span>
    );
  }

  return (
    <tr>
      <td>
        <span className="badge bg-secondary me-2 text-uppercase">{grant.principalKind}</span>
        <strong>{principalName}</strong>
      </td>
      <td>
        <code>{grant.action}</code>
      </td>
      <td>
        <code className="font-monospace small">{grant.selectorString}</code>
      </td>
      <td>
        <span className={`badge ${grant.effect === "allow" ? "bg-info" : "bg-danger"}`}>
          {grant.effect}
        </span>
      </td>
      <td>{matchedCell}</td>
    </tr>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
