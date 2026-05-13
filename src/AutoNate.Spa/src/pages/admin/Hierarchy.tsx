import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  Alert,
  Combobox,
  InputBase,
  Loader,
  Text,
  useCombobox
} from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import PageHeader from "@/components/PageHeader";
import { fetchSupervisorHierarchy, listUsers, listUsersPage } from "@/api/users";
import { useUsers, useSetUserSupervisor } from "@/hooks/useUsers";
import { LocalUser } from "@/types/flowable";
import {
  DataTable,
  DataTablePageRequest
} from "@/components/data-table/DataTable";

const COLUMN_WIDTHS = ["28%", "32%", "40%"];

// Hierarchy management. One row per user with an inline picker to pick
// (or clear) their supervisor. Saves on change. The supervisor edges drive
// multi-hop selectors like /record/*[assignee=user[supervisor=user]].
export default function Hierarchy() {
  // useUsers() backs the "Currently set to" column's id→username lookup.
  // The supervisor picker itself searches users server-side on demand.
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
          const currentLabel = currentId ? userByGuid.get(currentId)?.username ?? null : null;
          return (
            <SupervisorPicker
              userId={user.userId}
              currentSupervisorId={currentId}
              currentSupervisorLabel={currentLabel}
              disabled={savingFor === user.userId}
              onSelect={(value) => void onChange(user, value)}
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
    [supervisorByUserId, userByGuid, savingFor]
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

type SupervisorPickerProps = {
  userId: string;
  currentSupervisorId: string;
  currentSupervisorLabel: string | null;
  disabled: boolean;
  onSelect: (value: string) => void;
};

// Server-search picker for a row's supervisor. The dropdown lazily calls
// /api/users/page with a debounced query so we never materialize the full
// user list per row — important when there are thousands of users.
function SupervisorPicker({
  userId,
  currentSupervisorId,
  currentSupervisorLabel,
  disabled,
  onSelect
}: SupervisorPickerProps) {
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
    onDropdownOpen: () => combobox.selectFirstOption()
  });

  const [search, setSearch] = useState("");
  const [debouncedSearch] = useDebouncedValue(search.trim(), 200);

  const { data, isFetching } = useQuery({
    queryKey: ["users", "page", "supervisorPicker", debouncedSearch],
    queryFn: ({ signal }) =>
      listUsersPage(
        {
          page: 0,
          pageSize: 20,
          search: debouncedSearch || undefined,
          sort: "username",
          sortDir: "asc"
        },
        signal
      ),
    enabled: combobox.dropdownOpened,
    staleTime: 30_000,
    placeholderData: (prev) => prev
  });

  const candidates = (data?.items ?? []).filter((u) => u.userId !== userId);

  return (
    <Combobox
      store={combobox}
      withinPortal
      onOptionSubmit={(val) => {
        onSelect(val);
        setSearch("");
        combobox.closeDropdown();
      }}
    >
      <Combobox.Target>
        <InputBase
          size="xs"
          disabled={disabled}
          value={search}
          placeholder={currentSupervisorLabel ?? "— none —"}
          rightSection={
            isFetching ? <Loader size={12} /> : <Combobox.Chevron />
          }
          rightSectionPointerEvents="none"
          onClick={(e) => {
            e.stopPropagation();
            combobox.openDropdown();
          }}
          onFocus={() => combobox.openDropdown()}
          onBlur={() => {
            combobox.closeDropdown();
            setSearch("");
          }}
          onChange={(e) => {
            combobox.openDropdown();
            combobox.updateSelectedOptionIndex();
            setSearch(e.currentTarget.value);
          }}
        />
      </Combobox.Target>
      <Combobox.Dropdown>
        <Combobox.Options>
          <Combobox.Option value="" key="__none" active={currentSupervisorId === ""}>
            <Text size="sm" c="dimmed">— none —</Text>
          </Combobox.Option>
          {candidates.map((u) => {
            const fullName = `${u.firstName ?? ""} ${u.lastName ?? ""}`.trim();
            return (
              <Combobox.Option
                value={u.userId}
                key={u.userId}
                active={u.userId === currentSupervisorId}
              >
                <Text size="sm" fw={u.userId === currentSupervisorId ? 600 : 400}>
                  {u.username}
                </Text>
                {fullName && (
                  <Text size="xs" c="dimmed">
                    {fullName}
                  </Text>
                )}
              </Combobox.Option>
            );
          })}
          {!isFetching && candidates.length === 0 && (
            <Combobox.Empty>No users found</Combobox.Empty>
          )}
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
