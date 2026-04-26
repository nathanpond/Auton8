import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchSupervisorHierarchy } from "@/api/users";
import { useUsers, useSetUserSupervisor } from "@/hooks/useUsers";
import { LocalUser } from "@/types/flowable";

// Hierarchy management. One row per user with an inline dropdown to pick
// (or clear) their supervisor. Saves on change. The supervisor edges drive
// multi-hop selectors like /record/*[assignee=user[supervisor=user]].
export default function Hierarchy() {
  const { data: users = [], isLoading: usersLoading } = useUsers();
  const { data: pairs = [], isLoading: pairsLoading } = useQuery({
    queryKey: ["hierarchy", "supervisors"],
    queryFn: ({ signal }) => fetchSupervisorHierarchy(signal)
  });
  const setSupervisor = useSetUserSupervisor();

  const [savingFor, setSavingFor] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState("");

  const supervisorByUserId = useMemo(() => {
    const m = new Map<string, string>();
    for (const p of pairs) m.set(p.userId, p.supervisorUserId);
    return m;
  }, [pairs]);

  const userByGuid = useMemo(() => {
    const m = new Map<string, LocalUser>();
    for (const u of users) m.set(u.userId, u);
    return m;
  }, [users]);

  const visibleUsers = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) return users;
    return users.filter((u) =>
      `${u.username} ${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(needle)
    );
  }, [users, filter]);

  const onChange = async (user: LocalUser, value: string) => {
    setError(null);
    setSavingFor(user.userId);
    try {
      const supervisorUserId = value === "" ? null : value;
      if (supervisorUserId === user.userId) {
        setError("A user cannot supervise themselves.");
        return;
      }
      await setSupervisor.mutateAsync({ userId: user.userId, supervisorUserId });
    } catch (err) {
      setError(describeError(err));
    } finally {
      setSavingFor(null);
    }
  };

  const isLoading = usersLoading || pairsLoading;

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Hierarchy</h1>
        <p className="page-head-copy">
          Declare who supervises whom. Each user has at most one supervisor; the edge
          drives selectors like <code>/record/*[assignee=user[supervisor=user]]</code> and{" "}
          <code>/workflowexecution/*[startedby=user[supervisor=user]]</code>.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="panel panel-inverse">
        <div className="panel-heading d-flex justify-content-between align-items-center">
          <h4 className="panel-title mb-0">Users</h4>
          <input
            type="search"
            className="form-control form-control-sm"
            style={{ width: "16rem" }}
            placeholder="Search users…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
        </div>
        <div className="panel-body">
          {isLoading && <div>Loading…</div>}
          {!isLoading && visibleUsers.length === 0 && <div>No users match.</div>}
          {!isLoading && visibleUsers.length > 0 && (
            <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead>
                  <tr>
                    <th style={{ width: "20rem" }}>User</th>
                    <th style={{ width: "20rem" }}>Supervisor</th>
                    <th>Currently set to</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleUsers.map((user) => {
                    const currentId = supervisorByUserId.get(user.userId) ?? "";
                    const currentUser = currentId ? userByGuid.get(currentId) : null;
                    const saving = savingFor === user.userId;
                    return (
                      <tr key={user.userId}>
                        <td>
                          <strong>{user.username}</strong>
                          {(user.firstName || user.lastName) && (
                            <small className="d-block text-body text-opacity-75">
                              {`${user.firstName ?? ""} ${user.lastName ?? ""}`.trim()}
                            </small>
                          )}
                        </td>
                        <td>
                          <select
                            className="form-select form-select-sm"
                            value={currentId}
                            disabled={saving}
                            onChange={(e) => onChange(user, e.target.value)}
                          >
                            <option value="">— none —</option>
                            {users
                              .filter((other) => other.userId !== user.userId)
                              .map((other) => (
                                <option key={other.userId} value={other.userId}>
                                  {other.username}
                                </option>
                              ))}
                          </select>
                        </td>
                        <td>
                          {currentUser ? (
                            <span>
                              <strong>{currentUser.username}</strong>
                              <small className="ms-2 text-body text-opacity-50">
                                {currentUser.userId}
                              </small>
                            </span>
                          ) : (
                            <span className="text-body text-opacity-50">—</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
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
