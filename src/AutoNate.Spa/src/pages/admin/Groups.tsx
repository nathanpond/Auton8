import { useState } from "react";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Card,
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
      <PageHeader
        title="Groups"
        description="Group users together so role assignments and permissions can target many people at once."
      />

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <Grid>
        <Grid.Col span={{ base: 12, lg: 5 }}>
          <Card withBorder shadow="sm">
            <Title order={5} mb="md">
              All groups
            </Title>

            <Box component="form" onSubmit={submit} mb="md">
              <Group gap="xs">
                <TextInput
                  placeholder="New group name"
                  value={newName}
                  onChange={(e) => setNewName(e.currentTarget.value)}
                  style={{ flex: 1 }}
                />
                <Button
                  type="submit"
                  disabled={!newName.trim()}
                  loading={create.isPending}
                >
                  Create
                </Button>
              </Group>
            </Box>

            {isLoading && <Text>Loading…</Text>}
            {!isLoading && groups.length === 0 && <Text c="dimmed">No groups yet.</Text>}

            <Stack gap={0}>
              {groups.map((g) => {
                const isActive = selected === g.id;
                return (
                  <UnstyledButton
                    key={g.id}
                    onClick={() => setSelected(g.id)}
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
                      <strong>{g.name}</strong>
                      {g.description && (
                        <Text
                          size="sm"
                          c={isActive ? "var(--mantine-primary-color-contrast)" : "dimmed"}
                          component="div"
                        >
                          {g.description}
                        </Text>
                      )}
                    </span>
                    <ActionIcon
                      variant="outline"
                      color={isActive ? "white" : "red"}
                      size="sm"
                      onClick={(e) => {
                        e.stopPropagation();
                        if (confirm(`Delete group '${g.name}'?`)) {
                          void remove.mutateAsync(g.id);
                          if (selected === g.id) setSelected(null);
                        }
                      }}
                      aria-label={`Delete ${g.name}`}
                    >
                      <i className="fa fa-trash" />
                    </ActionIcon>
                  </UnstyledButton>
                );
              })}
            </Stack>
          </Card>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 7 }}>
          {selected ? (
            <MembersPanel groupId={selected} />
          ) : (
            <Card withBorder shadow="sm">
              <Text c="dimmed">Select a group to manage members.</Text>
            </Card>
          )}
        </Grid.Col>
      </Grid>
    </>
  );
}

function MembersPanel({ groupId }: { groupId: string }) {
  const { data: members = [] } = useGroupMembers(groupId);
  const { data: users = [] } = useUsers();
  const add = useAddGroupMember();
  const remove = useRemoveGroupMember();
  const [pickedUserId, setPickedUserId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const memberIds = new Set(members.map((m) => m.userId));
  const candidates = users.filter((u) => !memberIds.has(u.userId));

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!pickedUserId) return;
    try {
      await add.mutateAsync({ groupId, userId: pickedUserId });
      setPickedUserId(null);
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <Card withBorder shadow="sm">
      <Title order={5} mb="md">
        Members
      </Title>

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      <Box component="form" onSubmit={submit} mb="md">
        <Grid>
          <Grid.Col span={{ base: 12, sm: 9 }}>
            <Select
              value={pickedUserId}
              onChange={setPickedUserId}
              placeholder="— pick a user to add —"
              data={candidates.map((u) => ({ value: u.userId, label: u.username }))}
              searchable
              clearable
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 3 }}>
            <Button type="submit" fullWidth disabled={!pickedUserId} loading={add.isPending}>
              Add
            </Button>
          </Grid.Col>
        </Grid>
      </Box>

      {members.length === 0 ? (
        <Text c="dimmed">No members.</Text>
      ) : (
        <Table verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th>Added</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {members.map((m) => {
              const user = users.find((u) => u.userId === m.userId);
              return (
                <Table.Tr key={m.userId}>
                  <Table.Td>{user ? user.username : m.userId}</Table.Td>
                  <Table.Td>{new Date(m.addedAtUtc).toLocaleString()}</Table.Td>
                  <Table.Td>
                    <Button
                      size="xs"
                      variant="outline"
                      color="red"
                      onClick={() =>
                        confirm("Remove member?") &&
                        void remove.mutateAsync({ groupId, userId: m.userId })
                      }
                    >
                      Remove
                    </Button>
                  </Table.Td>
                </Table.Tr>
              );
            })}
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
