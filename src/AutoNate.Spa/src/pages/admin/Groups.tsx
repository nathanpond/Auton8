import { useState } from "react";
import { useUsers } from "@/hooks/useUsers";
import {
  useAddGroupMember,
  useCreateGroup,
  useDeleteGroup,
  useGroupMembers,
  useGroups,
  useRemoveGroupMember
} from "@/hooks/useAdmin";

export default function Groups() {
  const { data: groups = [], isLoading } = useGroups();
  const create = useCreateGroup();
  const remove = useDeleteGroup();
  const [selected, setSelected] = useState<string | null>(null);
  const [newName, setNewName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!newName.trim()) return;
    try {
      await create.mutateAsync({ name: newName.trim() });
      setNewName("");
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Groups</h1>
        <p className="page-head-copy">
          Group users together so role assignments and permissions can target many people at once.
        </p>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="row g-3">
        <div className="col-lg-5">
          <div className="panel panel-inverse">
            <div className="panel-heading">
              <h4 className="panel-title">All groups</h4>
            </div>
            <div className="panel-body">
              <form onSubmit={submit} className="d-flex gap-2 mb-3">
                <input
                  className="form-control"
                  placeholder="New group name"
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
              {!isLoading && groups.length === 0 && <div>No groups yet.</div>}

              <ul className="list-group">
                {groups.map((g) => (
                  <li
                    key={g.id}
                    className={`list-group-item d-flex justify-content-between align-items-center ${
                      selected === g.id ? "active" : ""
                    }`}
                    style={{ cursor: "pointer" }}
                    onClick={() => setSelected(g.id)}
                  >
                    <span>
                      <strong>{g.name}</strong>
                      {g.description && <small className="d-block text-muted">{g.description}</small>}
                    </span>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-danger"
                      onClick={(e) => {
                        e.stopPropagation();
                        if (confirm(`Delete group '${g.name}'?`)) {
                          void remove.mutateAsync(g.id);
                          if (selected === g.id) setSelected(null);
                        }
                      }}
                    >
                      Delete
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>

        <div className="col-lg-7">
          {selected ? (
            <MembersPanel groupId={selected} />
          ) : (
            <div className="panel panel-inverse">
              <div className="panel-body text-muted">Select a group to manage members.</div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}

function MembersPanel({ groupId }: { groupId: string }) {
  const { data: members = [] } = useGroupMembers(groupId);
  const { data: users = [] } = useUsers();
  const add = useAddGroupMember();
  const remove = useRemoveGroupMember();
  const [pickedUserId, setPickedUserId] = useState("");
  const [error, setError] = useState<string | null>(null);

  const memberIds = new Set(members.map((m) => m.userId));
  const candidates = users.filter((u) => !memberIds.has(u.userId));

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!pickedUserId) return;
    try {
      await add.mutateAsync({ groupId, userId: pickedUserId });
      setPickedUserId("");
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title">Members</h4>
      </div>
      <div className="panel-body">
        {error && <div className="alert alert-danger">{error}</div>}

        <form onSubmit={submit} className="row g-2 mb-3">
          <div className="col-sm-9">
            <select
              className="form-select"
              value={pickedUserId}
              onChange={(e) => setPickedUserId(e.target.value)}
            >
              <option value="">— pick a user to add —</option>
              {candidates.map((u) => (
                <option key={u.userId} value={u.userId}>
                  {u.username}
                </option>
              ))}
            </select>
          </div>
          <div className="col-sm-3">
            <button
              type="submit"
              className="btn btn-primary w-100"
              disabled={add.isPending || !pickedUserId}
            >
              Add
            </button>
          </div>
        </form>

        {members.length === 0 ? (
          <div className="text-muted">No members.</div>
        ) : (
          <table className="table table-sm">
            <thead>
              <tr>
                <th>User</th>
                <th>Added</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {members.map((m) => {
                const user = users.find((u) => u.userId === m.userId);
                return (
                  <tr key={m.userId}>
                    <td>{user ? user.username : m.userId}</td>
                    <td>{new Date(m.addedAtUtc).toLocaleString()}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-danger"
                        onClick={() =>
                          confirm("Remove member?") &&
                          void remove.mutateAsync({ groupId, userId: m.userId })
                        }
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
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
