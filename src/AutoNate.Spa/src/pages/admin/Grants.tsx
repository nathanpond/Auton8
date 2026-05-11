import { useMemo, useState } from "react";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Code,
  Grid,
  Group,
  Input,
  Select,
  Stack,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import SelectorBuilder from "@/components/SelectorBuilder";
import GrantsHelpModal from "./GrantsHelpModal";
import { useUsers } from "@/hooks/useUsers";
import {
  useCreatePermissionGrant,
  useDeletePermissionGrant,
  useGroups,
  useRoles
} from "@/hooks/useAdmin";
import { PermissionGrant, listPermissionGrants, listPermissionGrantsPage } from "@/api/admin";
import {
  DataTable,
  DataTableFilterOption,
  DataTablePageRequest
} from "@/components/data-table/DataTable";

type PrincipalKind = "user" | "group" | "role";

const COLUMN_WIDTHS = ["8%", "18%", "12%", "32%", "10%", "10%", "10%"];

const KIND_FILTERS: DataTableFilterOption<PermissionGrant>[] = [
  { id: "user", label: "Users", predicate: (g) => g.principalKind === "user" },
  { id: "group", label: "Groups", predicate: (g) => g.principalKind === "group" },
  { id: "role", label: "Roles", predicate: (g) => g.principalKind === "role" }
];

export default function Grants() {
  const { data: users = [] } = useUsers();
  const { data: groups = [] } = useGroups();
  const { data: roles = [] } = useRoles();
  const create = useCreatePermissionGrant();
  const remove = useDeletePermissionGrant();

  const [kind, setKind] = useState<PrincipalKind>("user");
  const [principalId, setPrincipalId] = useState<string | null>(null);
  const [action, setAction] = useState("view");
  const [selector, setSelector] = useState("/record/*");
  const [effect, setEffect] = useState<"allow" | "deny">("allow");
  const [priority, setPriority] = useState(0);
  const [error, setError] = useState<string | null>(null);
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
      setPrincipalId(null);
      setSelector("/record/*");
    } catch (err) {
      setError(describeError(err));
    }
  };

  const columns = useMemo<DataTableColumn<PermissionGrant>[]>(
    () => [
      {
        id: "principalKind",
        accessorKey: "principalKind",
        header: "Kind"
      },
      {
        id: "principal",
        header: "Principal",
        accessorFn: (g) => principalLabel(g),
        cell: ({ row }) => principalLabel(row.original)
      },
      {
        id: "action",
        accessorKey: "action",
        header: "Action"
      },
      {
        id: "selectorString",
        accessorKey: "selectorString",
        header: "Selector",
        cell: ({ row }) => <Code>{row.original.selectorString}</Code>
      },
      {
        id: "effect",
        accessorKey: "effect",
        header: "Effect",
        cell: ({ row }) => (
          <Badge color={row.original.effect === "allow" ? "green" : "red"} variant="filled">
            {row.original.effect}
          </Badge>
        )
      },
      {
        id: "priority",
        accessorKey: "priority",
        header: "Priority"
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Box>
            <ActionIcon
              variant="outline"
              color="red"
              size="sm"
              title="Revoke grant"
              aria-label="Revoke grant"
              onClick={(e) => {
                e.stopPropagation();
                if (confirm("Revoke this grant?")) void remove.mutateAsync(row.original.id);
              }}
            >
              <i className="fa fa-trash" />
            </ActionIcon>
          </Box>
        )
      }
    ],
    [principalLabel, remove]
  );

  const principalData =
    kind === "user"
      ? users.map((u) => ({ value: u.userId, label: u.username }))
      : kind === "group"
        ? groups.map((g) => ({ value: g.id, label: g.name }))
        : roles.map((r) => ({ value: r.id, label: r.isSystem ? `${r.name} (system)` : r.name }));

  return (
    <>
      <PageHeader
        title="Permissions"
        description={
          <>
            Attach a permission rule to a user, a group, or a role. Roles still act as bundles — you
            assign a role to users/groups on the Roles page, and the role&apos;s permissions live
            here.
          </>
        }
      />
      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <Stack gap="md">
        <Card withBorder shadow="sm">
          <Group justify="space-between" align="center" mb="md">
            <Title order={5} m={0}>
              Add grant
            </Title>
            <Anchor
              component="button"
              type="button"
              size="sm"
              onClick={() => setHelpOpen(true)}
              title="How permissions work"
              aria-label="How permissions work"
            >
              <i className="fa fa-circle-question" /> Help
            </Anchor>
          </Group>

          <Box component="form" onSubmit={submit}>
            <Stack gap="xs">
              <Grid align="flex-end">
                <Grid.Col span={{ base: 12, sm: 2 }}>
                  <Select
                    label="Principal kind"
                    size="xs"
                    value={kind}
                    onChange={(v) => {
                      setKind((v as PrincipalKind) ?? "user");
                      setPrincipalId(null);
                    }}
                    data={[
                      { value: "user", label: "user" },
                      { value: "group", label: "group" },
                      { value: "role", label: "role" }
                    ]}
                    allowDeselect={false}
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <Select
                    label="Principal"
                    size="xs"
                    value={principalId}
                    onChange={setPrincipalId}
                    placeholder={`— pick ${kind} —`}
                    data={principalData}
                    searchable
                    clearable
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 2 }}>
                  <TextInput
                    label="Action"
                    size="xs"
                    value={action}
                    onChange={(e) => setAction(e.currentTarget.value)}
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 2 }}>
                  <Select
                    label="Effect"
                    size="xs"
                    value={effect}
                    onChange={(v) => setEffect((v as "allow" | "deny") ?? "allow")}
                    data={[
                      { value: "allow", label: "allow" },
                      { value: "deny", label: "deny" }
                    ]}
                    allowDeselect={false}
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 1 }}>
                  <TextInput
                    label="Priority"
                    size="xs"
                    type="number"
                    value={priority}
                    onChange={(e) => setPriority(Number(e.currentTarget.value))}
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 1 }}>
                  <Button type="submit" size="xs" fullWidth loading={create.isPending}>
                    Add
                  </Button>
                </Grid.Col>
              </Grid>

              <Box
                p="xs"
                style={{
                  border: "1px solid var(--mantine-color-default-border)",
                  borderRadius: "var(--mantine-radius-default)"
                }}
              >
                <Input.Wrapper label="Selector" size="xs">
                  <SelectorBuilder value={selector} onChange={setSelector} />
                </Input.Wrapper>
              </Box>
            </Stack>
          </Box>
        </Card>

        <Box>
          <Title order={5} mb="md">
            Existing grants
          </Title>
          <DataTable<PermissionGrant>
            mode="auto"
            autoThreshold={1000}
            loadAll={() => listPermissionGrants()}
            loadPage={async (req: DataTablePageRequest) => {
              const r = await listPermissionGrantsPage({
                page: req.page,
                pageSize: req.pageSize,
                search: req.search || undefined,
                sort: req.sort?.id,
                sortDir: req.sort ? (req.sort.desc ? "desc" : "asc") : undefined,
                principalKind: (req.filter ?? undefined) as PrincipalKind | undefined
              });
              return { items: r.items, totalCount: r.totalCount };
            }}
            queryKey={["admin", "grants"]}
            columns={columns}
            rowKey={(g) => g.id}
            columnWidths={COLUMN_WIDTHS}
            initialSort={[{ id: "principalKind", desc: false }]}
            searchPlaceholder="Search grants…"
            emptyMessage="No grants."
            loadingMessage="Loading grants…"
            filters={KIND_FILTERS}
            globalFilterFn={(g, search) => {
              const needle = search.toLowerCase();
              return `${g.principalKind} ${principalLabel(g)} ${g.action} ${g.selectorString}`
                .toLowerCase()
                .includes(needle);
            }}
          />
        </Box>
      </Stack>

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
