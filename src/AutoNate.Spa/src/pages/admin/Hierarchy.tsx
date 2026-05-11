import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import { Alert, NativeSelect, Text } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
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

  const columns = useMemo<DataTableColumn<LocalUser>[]>(
    () => [
      {
        id: "username",
        accessorKey: "username",
        header: "User",
        cell: ({ row }) => (
          <div>
            <strong>{row.original.username}</strong>
            {(row.original.firstName || row.original.lastName) && (
              <Text size="xs" c="dimmed" component="div">
                {`${row.original.firstName ?? ""} ${row.original.lastName ?? ""}`.trim()}
              </Text>
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
            <NativeSelect
              size="xs"
              value={currentId}
              disabled={saving}
              onClick={(e) => e.stopPropagation()}
              onChange={(e) => void onChange(user, e.currentTarget.value)}
              data={[
                { value: "", label: "— none —" },
                ...allUsers
                  .filter((other) => other.userId !== user.userId)
                  .map((other) => ({ value: other.userId, label: other.username }))
              ]}
            />
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
            return <Text component="span" c="dimmed">—</Text>;
          }
          return (
            <span>
              <strong>{currentUser.username}</strong>
              <Text component="span" size="xs" c="dimmed" ml={8}>
                {currentUser.userId}
              </Text>
            </span>
          );
        }
      }
    ],
    [allUsers, supervisorByUserId, userByGuid, savingFor]
  );

  return (
    <>
      <PageHeader
        title="Hierarchy"
        description={
          <>
            Declare who supervises whom. Each user has at most one supervisor; the edge drives
            selectors like <code>/record/*[assignee=user[supervisor=user]]</code> and{" "}
            <code>/workflowexecution/*[startedby=user[supervisor=user]]</code>.
          </>
        }
      />

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

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
