import { useMemo, useState } from "react";
import SelectorBuilder from "@/components/SelectorBuilder";
import GrantsHelpModal from "./GrantsHelpModal";
import { useUsers } from "@/hooks/useUsers";
import {
  useCreatePermissionGrant,
  useDeletePermissionGrant,
  useGroups,
  usePermissionGrants,
  useRoles
} from "@/hooks/useAdmin";

type PrincipalKind = "user" | "group" | "role";

// Single source of truth for permissions. Every grant — whether attached to a
// user, a group, or a role — lives in permission_grants. This page is the
// only place to author them.
export default function Grants() {
  const { data: grants = [], isLoading } = usePermissionGrants();
  const { data: users = [] } = useUsers();
  const { data: groups = [] } = useGroups();
  const { data: roles = [] } = useRoles();
  const create = useCreatePermissionGrant();
  const remove = useDeletePermissionGrant();

  const [kind, setKind] = useState<PrincipalKind>("user");
  const [principalId, setPrincipalId] = useState("");
  const [action, setAction] = useState("view");
  const [selector, setSelector] = useState("/record/*");
  const [effect, setEffect] = useState<"allow" | "deny">("allow");
  const [priority, setPriority] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<"all" | PrincipalKind>("all");
  const [helpOpen, setHelpOpen] = useState(false);

  const principalLabel = useMemo(() => {
    return (g: { principalKind: string; principalId: string }) => {
      if (g.principalKind === "user") {
        return users.find((u) => u.userId === g.principalId)?.username ?? g.principalId;
      }
      if (g.principalKind === "role") {
        return roles.find((r) => r.id === g.principalId)?.name ?? g.principalId;
      }
      return groups.find((x) => x.id === g.principalId)?.name ?? g.principalId;
    };
  }, [users, groups, roles]);

  const visible = useMemo(
    () => (filter === "all" ? grants : grants.filter((g) => g.principalKind === filter)),
    [grants, filter]
  );

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!principalId) {
      setError("Pick a principal first.");
      return;
    }
    try {
      await create.mutateAsync({
        principalKind: kind,
        principalId,
        action: action.trim(),
        selectorString: selector.trim(),
        effect,
        priority
      });
      setPrincipalId("");
      setSelector("/record/*");
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Permissions</h1>
        <p className="page-head-copy">
          Attach a permission rule to a user, a group, or a role. Roles still
          act as bundles — you assign a role to users/groups on the Roles page,
          and the role's permissions live here.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="row g-3">
        <div className="col-12">
          <div className="panel panel-inverse">
            <div className="panel-heading d-flex justify-content-between align-items-center">
              <h4 className="panel-title mb-0">Add grant</h4>
              <button
                type="button"
                className="btn btn-sm btn-link p-0 text-decoration-none"
                onClick={() => setHelpOpen(true)}
                title="How permissions work"
                aria-label="How permissions work"
              >
                <i className="fa fa-circle-question" /> Help
              </button>
            </div>
            <div className="panel-body">
              <form onSubmit={submit} className="d-flex flex-column gap-2">
                <div className="row g-2 align-items-end">
                  <div className="col-sm-2">
                    <label className="form-label small mb-1">Principal kind</label>
                    <select
                      className="form-select form-select-sm"
                      value={kind}
                      onChange={(e) => {
                        setKind(e.target.value as PrincipalKind);
                        setPrincipalId("");
                      }}
                    >
                      <option value="user">user</option>
                      <option value="group">group</option>
                      <option value="role">role</option>
                    </select>
                  </div>
                  <div className="col-sm-4">
                    <label className="form-label small mb-1">Principal</label>
                    <select
                      className="form-select form-select-sm"
                      value={principalId}
                      onChange={(e) => setPrincipalId(e.target.value)}
                    >
                      <option value="">— pick {kind} —</option>
                      {kind === "user" &&
                        users.map((u) => (
                          <option key={u.userId} value={u.userId}>
                            {u.username}
                          </option>
                        ))}
                      {kind === "group" &&
                        groups.map((g) => (
                          <option key={g.id} value={g.id}>
                            {g.name}
                          </option>
                        ))}
                      {kind === "role" &&
                        roles.map((r) => (
                          <option key={r.id} value={r.id}>
                            {r.name}
                            {r.isSystem ? " (system)" : ""}
                          </option>
                        ))}
                    </select>
                  </div>
                  <div className="col-sm-2">
                    <label className="form-label small mb-1">Action</label>
                    <input
                      className="form-control form-control-sm"
                      value={action}
                      onChange={(e) => setAction(e.target.value)}
                    />
                  </div>
                  <div className="col-sm-2">
                    <label className="form-label small mb-1">Effect</label>
                    <select
                      className="form-select form-select-sm"
                      value={effect}
                      onChange={(e) => setEffect(e.target.value as "allow" | "deny")}
                    >
                      <option value="allow">allow</option>
                      <option value="deny">deny</option>
                    </select>
                  </div>
                  <div className="col-sm-1">
                    <label className="form-label small mb-1">Priority</label>
                    <input
                      type="number"
                      className="form-control form-control-sm"
                      value={priority}
                      onChange={(e) => setPriority(Number(e.target.value))}
                    />
                  </div>
                  <div className="col-sm-1">
                    <button type="submit" className="btn btn-primary w-100" disabled={create.isPending}>
                      Add
                    </button>
                  </div>
                </div>

                <div className="border rounded p-2">
                  <label className="form-label small mb-1">Selector</label>
                  <SelectorBuilder value={selector} onChange={setSelector} />
                </div>
              </form>
            </div>
          </div>
        </div>

        <div className="col-12">
          <div className="panel panel-inverse">
            <div className="panel-heading d-flex justify-content-between align-items-center">
              <h4 className="panel-title mb-0">Existing grants</h4>
              <div className="btn-group btn-group-sm">
                <button
                  type="button"
                  className={`btn btn-outline-secondary ${filter === "all" ? "active" : ""}`}
                  onClick={() => setFilter("all")}
                >
                  All
                </button>
                <button
                  type="button"
                  className={`btn btn-outline-secondary ${filter === "user" ? "active" : ""}`}
                  onClick={() => setFilter("user")}
                >
                  Users
                </button>
                <button
                  type="button"
                  className={`btn btn-outline-secondary ${filter === "group" ? "active" : ""}`}
                  onClick={() => setFilter("group")}
                >
                  Groups
                </button>
                <button
                  type="button"
                  className={`btn btn-outline-secondary ${filter === "role" ? "active" : ""}`}
                  onClick={() => setFilter("role")}
                >
                  Roles
                </button>
              </div>
            </div>
            <div className="panel-body">
              {isLoading && <div>Loading…</div>}
              {!isLoading && visible.length === 0 && <div className="text-muted">No grants.</div>}
              {visible.length > 0 && (
                <table className="table table-sm">
                  <thead>
                    <tr>
                      <th>Kind</th>
                      <th>Principal</th>
                      <th>Action</th>
                      <th>Selector</th>
                      <th>Effect</th>
                      <th>Priority</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((g) => (
                      <tr key={g.id}>
                        <td>{g.principalKind}</td>
                        <td>{principalLabel(g)}</td>
                        <td>{g.action}</td>
                        <td className="font-monospace">{g.selectorString}</td>
                        <td>
                          <span className={`badge bg-${g.effect === "allow" ? "success" : "danger"}`}>
                            {g.effect}
                          </span>
                        </td>
                        <td>{g.priority}</td>
                        <td>
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            onClick={() =>
                              confirm("Revoke this grant?") && void remove.mutateAsync(g.id)
                            }
                          >
                            Revoke
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </div>
      </div>

      {helpOpen && <GrantsHelpModal onClose={() => setHelpOpen(false)} />}
    </>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
