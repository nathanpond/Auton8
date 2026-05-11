import { Link } from "react-router-dom";
import { useState } from "react";
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Code,
  Grid,
  Group,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  UnstyledButton
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
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
      <PageHeader
        title="Roles"
        description={
          <>
            Roles are named handles you can attach permissions to. Manage the actual permissions a
            role conveys on the <Link to="/admin/grants">Permissions</Link> page; this page handles
            the role itself and who is assigned to it.
          </>
        }
      />

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <Grid>
        <Grid.Col span={{ base: 12, lg: 5 }}>
          <Card withBorder shadow="sm">
            <Group justify="space-between" align="center" mb="md">
              <Title order={5} m={0}>
                All roles
              </Title>
              <Anchor
                component="button"
                type="button"
                size="sm"
                onClick={() => setHelpOpen(true)}
                title="How roles work"
                aria-label="How roles work"
              >
                <i className="fa fa-circle-question" /> Help
              </Anchor>
            </Group>

            <Box component="form" onSubmit={submit} mb="md">
              <Group gap="xs">
                <TextInput
                  placeholder="New role name"
                  value={newName}
                  onChange={(e) => setNewName(e.currentTarget.value)}
                  style={{ flex: 1 }}
                />
                <Button type="submit" disabled={!newName.trim()} loading={create.isPending}>
                  Create
                </Button>
              </Group>
            </Box>

            {isLoading && <Text>Loading…</Text>}
            {!isLoading && roles.length === 0 && <Text c="dimmed">No roles yet.</Text>}

            <Stack gap={0}>
              {roles.map((r) => {
                const isActive = selectedRoleId === r.id;
                return (
                  <UnstyledButton
                    key={r.id}
                    onClick={() => setSelectedRoleId(r.id)}
                    p="sm"
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      gap: 8,
                      borderBottom: "1px solid var(--mantine-color-default-border)",
                      background: isActive ? "var(--mantine-primary-color-filled)" : "transparent",
                      color: isActive ? "var(--mantine-primary-color-contrast)" : undefined,
                      cursor: "pointer"
                    }}
                  >
                    <span>
                      <strong>{r.name}</strong>
                      {r.isSystem && (
                        <Badge color="gray" variant="filled" ml={8}>
                          system
                        </Badge>
                      )}
                      {r.description && (
                        <Text
                          size="sm"
                          c={isActive ? "var(--mantine-primary-color-contrast)" : "dimmed"}
                          component="div"
                        >
                          {r.description}
                        </Text>
                      )}
                    </span>
                    {!r.isSystem && (
                      <Button
                        size="xs"
                        variant="outline"
                        color={isActive ? "white" : "red"}
                        onClick={(e) => {
                          e.stopPropagation();
                          if (confirm(`Delete role '${r.name}'?`)) {
                            void remove.mutateAsync(r.id);
                            if (selectedRoleId === r.id) setSelectedRoleId(null);
                          }
                        }}
                      >
                        Delete
                      </Button>
                    )}
                  </UnstyledButton>
                );
              })}
            </Stack>
          </Card>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 7 }}>
          {selectedRoleId ? (
            <AssignmentsPanel roleId={selectedRoleId} />
          ) : (
            <Card withBorder shadow="sm">
              <Text c="dimmed">Select a role to manage assignments.</Text>
            </Card>
          )}
        </Grid.Col>
      </Grid>

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
  const [principalId, setPrincipalId] = useState<string | null>(null);
  const [scope, setScope] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!principalId) return;
    try {
      await add.mutateAsync({
        roleId,
        principalKind: kind,
        principalId,
        scopeString: scope.trim() || undefined
      });
      setPrincipalId(null);
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
    <Card withBorder shadow="sm">
      <Title order={5} mb="md">
        Assignments
      </Title>

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <Box component="form" onSubmit={submit} mb="md">
        <Grid>
          <Grid.Col span={{ base: 12, sm: 2 }}>
            <Select
              value={kind}
              onChange={(v) => {
                setKind((v as "user" | "group") ?? "user");
                setPrincipalId(null);
              }}
              data={[
                { value: "user", label: "user" },
                { value: "group", label: "group" }
              ]}
              allowDeselect={false}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 4 }}>
            <Select
              value={principalId}
              onChange={setPrincipalId}
              placeholder={`— pick ${kind} —`}
              data={
                kind === "user"
                  ? users.map((u) => ({ value: u.userId, label: u.username }))
                  : groups.map((g) => ({ value: g.id, label: g.name }))
              }
              searchable
              clearable
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 4 }}>
            <TextInput
              placeholder="optional scope selector"
              value={scope}
              onChange={(e) => setScope(e.currentTarget.value)}
              styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 2 }}>
            <Button type="submit" fullWidth disabled={!principalId} loading={add.isPending}>
              Assign
            </Button>
          </Grid.Col>
        </Grid>
      </Box>

      {assignments.length === 0 ? (
        <Text c="dimmed">No assignments.</Text>
      ) : (
        <Table verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Kind</Table.Th>
              <Table.Th>Principal</Table.Th>
              <Table.Th>Scope</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {assignments.map((a) => (
              <Table.Tr key={a.id}>
                <Table.Td>{a.principalKind}</Table.Td>
                <Table.Td>{lookup(a.principalKind, a.principalId)}</Table.Td>
                <Table.Td>
                  <Code>{a.scopeString ?? "—"}</Code>
                </Table.Td>
                <Table.Td>
                  <Button
                    size="xs"
                    variant="outline"
                    color="red"
                    onClick={() =>
                      confirm("Revoke assignment?") &&
                      void revoke.mutateAsync({ assignmentId: a.id, roleId })
                    }
                  >
                    Revoke
                  </Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Card>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
