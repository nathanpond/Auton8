import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ColumnDef } from "@tanstack/react-table";
import { fetchSupervisorHierarchy, listUsers, listUsersPage } from "@/api/users";
import { useUsers, useSetUserSupervisor } from "@/hooks/useUsers";
import { LocalUser } from "@/types/flowable";
import {
  DataTable,
  DataTablePageRequest
} from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["28%", "32%", "40%"];

// Hierarchy management. One row per user with an inline dropdown to pick
// (or clear) their supervisor. Saves on change. The supervisor edges drive
// multi-hop selectors like /record/*[assignee=user[supervisor=user]].
export default function Hierarchy() {
  // useUsers() fetches the full user list; the supervisor dropdown needs every
  // user (not just the current page) so we can offer them all as options.
  const { data: allUsers = [] } = useUsers();
  const { data: pairs = [] } = useQuery({
    queryKey: ["hierarchy", "supervisors"],
    queryFn: ({ signal }) => fetchSupervisorHierarchy(signal)
  });
  const setSupervisor = useSetUserSupervisor();

  const [savingFor, setSavingFor] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const supervisorByUserId = useMemo(() => {
    const m = new Map<string, string>();
    for (const p of pairs) m.set(p.userId, p.supervisorUserId);
    return m;
  }, [pairs]);

  const userByGuid = useMemo(() => {
    const m = new Map<string, LocalUser>();
    for (const u of allUsers) m.set(u.userId, u);
    return m;
  }, [allUsers]);

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

  const loadPage = async (req: DataTablePageRequest) => {
    const result = await listUsersPage({
      page: req.page,
      pageSize: req.pageSize,
      search: req.search || undefined,
      sort: req.sort?.id,
      sortDir: req.sort ? (req.sort.desc ? "desc" : "asc") : undefined
    });
    return { items: result.items, totalCount: result.totalCount };
  };

  const columns = useMemo<ColumnDef<LocalUser>[]>(
    () => [
      {
        id: "username",
        accessorKey: "username",
        header: "User",
        cell: ({ row }) => (
          <div>
            <strong>{row.original.username}</strong>
            {(row.original.firstName || row.original.lastName) && (
              <small className="d-block text-body text-opacity-75">
                {`${row.original.firstName ?? ""} ${row.original.lastName ?? ""}`.trim()}
              </small>
            )}
          </div>
        )
      },
      {
        id: "supervisor",
        header: "Supervisor",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => {
          const user = row.original;
          const currentId = supervisorByUserId.get(user.userId) ?? "";
          const saving = savingFor === user.userId;
          return (
            <select
              className="form-select form-select-sm"
              value={currentId}
              disabled={saving}
              onClick={(e) => e.stopPropagation()}
              onChange={(e) => void onChange(user, e.target.value)}
            >
              <option value="">— none —</option>
              {allUsers
                .filter((other) => other.userId !== user.userId)
                .map((other) => (
                  <option key={other.userId} value={other.userId}>
                    {other.username}
                  </option>
                ))}
            </select>
          );
        }
      },
      {
        id: "currentlySetTo",
        header: "Currently set to",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => {
          const currentId = supervisorByUserId.get(row.original.userId) ?? "";
          const currentUser = currentId ? userByGuid.get(currentId) : null;
          if (!currentUser) {
            return <span className="text-body text-opacity-50">—</span>;
          }
          return (
            <span>
              <strong>{currentUser.username}</strong>
              <small className="ms-2 text-body text-opacity-50">{currentUser.userId}</small>
            </span>
          );
        }
      }
    ],
    [allUsers, supervisorByUserId, userByGuid, savingFor]
  );

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

      <DataTable<LocalUser>
        mode="auto"
        autoThreshold={1000}
        loadAll={() => listUsers()}
        loadPage={loadPage}
        queryKey={["users"]}
        columns={columns}
        rowKey={(u) => u.userId}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "username", desc: false }]}
        searchPlaceholder="Search users…"
        emptyMessage="No users match."
        loadingMessage="Loading users…"
        globalFilterFn={(u, search) => {
          const needle = search.toLowerCase();
          return `${u.username} ${u.firstName} ${u.lastName} ${u.email ?? ""}`
            .toLowerCase()
            .includes(needle);
        }}
      />
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
