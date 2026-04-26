import { Link } from "react-router-dom";
import { useState } from "react";
import RolesHelpModal from "./RolesHelpModal";
import { useUsers } from "@/hooks/useUsers";
import { useGroups } from "@/hooks/useAdmin";
import {
  useAddRoleAssignment,
  useCreateRole,
  useDeleteRole,
  useRevokeRoleAssignment,
  useRoleAssignments,
  useRoles
} from "@/hooks/useAdmin";

export default function Roles() {
  const { data: roles = [], isLoading } = useRoles();
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const create = useCreateRole();
  const remove = useDeleteRole();
  const [newName, setNewName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [helpOpen, setHelpOpen] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!newName.trim()) return;
    try {
      await create.mutateAsync({ name: newName.trim() });
      setNewName("");
    } catch (err: unknown) {
      setError(describeError(err));
    }
  };

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Roles</h1>
        <p className="page-head-copy">
          Roles are named handles you can attach permissions to. Manage the actual
          permissions a role conveys on the{" "}
          <Link to="/admin/grants">Permissions</Link> page; this page handles the
          role itself and who is assigned to it.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="row g-3">
        <div className="col-lg-5">
          <div className="panel panel-inverse">
            <div className="panel-heading d-flex justify-content-between align-items-center">
              <h4 className="panel-title mb-0">All roles</h4>
              <button
                type="button"
                className="btn btn-sm btn-link p-0 text-decoration-none"
                onClick={() => setHelpOpen(true)}
                title="How roles work"
                aria-label="How roles work"
              >
                <i className="fa fa-circle-question" /> Help
              </button>
            </div>
            <div className="panel-body">
              <form onSubmit={submit} className="d-flex gap-2 mb-3">
                <input
                  className="form-control"
                  placeholder="New role name"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                />
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={create.isPending || !newName.trim()}
                >
                  Create
                </button>
              </form>

              {isLoading && <div>Loading…</div>}
              {!isLoading && roles.length === 0 && <div>No roles yet.</div>}

              <ul className="list-group">
                {roles.map((r) => (
                  <li
                    key={r.id}
                    className={`list-group-item d-flex justify-content-between align-items-center ${
                      selectedRoleId === r.id ? "active" : ""
                    }`}
                    style={{ cursor: "pointer" }}
                    onClick={() => setSelectedRoleId(r.id)}
                  >
                    <span>
                      <strong>{r.name}</strong>
                      {r.isSystem && <span className="badge bg-secondary ms-2">system</span>}
                      {r.description && (
                        <small className="d-block text-muted">{r.description}</small>
                      )}
                    </span>
                    {!r.isSystem && (
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-danger"
                        onClick={(e) => {
                          e.stopPropagation();
                          if (confirm(`Delete role '${r.name}'?`)) {
                            void remove.mutateAsync(r.id);
                            if (selectedRoleId === r.id) setSelectedRoleId(null);
                          }
                        }}
                      >
                        Delete
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>

        <div className="col-lg-7">
          {selectedRoleId ? (
            <AssignmentsPanel roleId={selectedRoleId} />
          ) : (
            <div className="panel panel-inverse">
              <div className="panel-body text-muted">Select a role to manage assignments.</div>
            </div>
          )}
        </div>
      </div>

      {helpOpen && <RolesHelpModal onClose={() => setHelpOpen(false)} />}
    </>
  );
}

function AssignmentsPanel({ roleId }: { roleId: string }) {
  const { data: assignments = [] } = useRoleAssignments(roleId);
  const { data: users = [] } = useUsers();
  const { data: groups = [] } = useGroups();
  const add = useAddRoleAssignment();
  const revoke = useRevokeRoleAssignment();
  const [kind, setKind] = useState<"user" | "group">("user");
  const [principalId, setPrincipalId] = useState("");
  const [scope, setScope] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      await add.mutateAsync({
        roleId,
        principalKind: kind,
        principalId,
        scopeString: scope.trim() || undefined
      });
      setPrincipalId("");
      setScope("");
    } catch (err) {
      setError(describeError(err));
    }
  };

  const lookup = (kindOf: string, id: string) => {
    if (kindOf === "user") {
      const u = users.find((x) => x.userId === id);
      return u ? `${u.username}` : id;
    }
    const g = groups.find((x) => x.id === id);
    return g ? g.name : id;
  };

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">Assignments</h4>
      </div>
      <div className="panel-body">
        {error && <div className="alert alert-danger">{error}</div>}

        <form onSubmit={submit} className="row g-2 mb-3">
          <div className="col-sm-2">
            <select
              className="form-select"
              value={kind}
              onChange={(e) => {
                setKind(e.target.value as "user" | "group");
                setPrincipalId("");
              }}
            >
              <option value="user">user</option>
              <option value="group">group</option>
            </select>
          </div>
          <div className="col-sm-4">
            <select
              className="form-select"
              value={principalId}
              onChange={(e) => setPrincipalId(e.target.value)}
            >
              <option value="">— pick {kind} —</option>
              {kind === "user"
                ? users.map((u) => (
                    <option key={u.userId} value={u.userId}>
                      {u.username}
                    </option>
                  ))
                : groups.map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.name}
                    </option>
                  ))}
            </select>
          </div>
          <div className="col-sm-4">
            <input
              className="form-control font-monospace"
              placeholder="optional scope selector"
              value={scope}
              onChange={(e) => setScope(e.target.value)}
            />
          </div>
          <div className="col-sm-2">
            <button
              type="submit"
              className="btn btn-primary w-100"
              disabled={add.isPending || !principalId}
            >
              Assign
            </button>
          </div>
        </form>

        {assignments.length === 0 ? (
          <div className="text-muted">No assignments.</div>
        ) : (
          <table className="table table-sm">
            <thead>
              <tr>
                <th>Kind</th>
                <th>Principal</th>
                <th>Scope</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {assignments.map((a) => (
                <tr key={a.id}>
                  <td>{a.principalKind}</td>
                  <td>{lookup(a.principalKind, a.principalId)}</td>
                  <td className="font-monospace">{a.scopeString ?? "—"}</td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-danger"
                      onClick={() =>
                        confirm("Revoke assignment?") &&
                        void revoke.mutateAsync({ assignmentId: a.id, roleId })
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
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
