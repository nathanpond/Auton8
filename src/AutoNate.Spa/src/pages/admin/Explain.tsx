import { useMemo, useState } from "react";
import {
  Alert,
  Badge,
  Box,
  Button,
  Card,
  Code,
  Grid,
  Group,
  Select,
  Table,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useUsers } from "@/hooks/useUsers";
import { useExplainPermission, useRegistry, useRoles, useGroups } from "@/hooks/useAdmin";
import type { ExplainGrant, ExplainResult } from "@/api/admin";

// Effective permissions debugger. Pick a user, an action, a kind, and an
// optional target id; the server replays the evaluator and returns the
// final allow/deny along with every grant it considered.
export default function Explain() {
  const { data: users = [] } = useUsers();
  const { data: registry } = useRegistry();
  const { data: roles = [] } = useRoles();
  const { data: groups = [] } = useGroups();
  const explain = useExplainPermission();

  const [asUserId, setAsUserId] = useState<string | null>(null);
  const [targetKind, setTargetKind] = useState<string | null>("record");
  const [action, setAction] = useState<string | null>("view");
  const [targetId, setTargetId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ExplainResult | null>(null);

  const kinds = registry?.kinds ?? [];
  const currentKind = kinds.find((k) => k.kind === targetKind);
  const actionOptions = currentKind?.actions ?? [];

  const roleNamesById = useMemo(() => {
    const m = new Map<string, string>();
    for (const r of roles) m.set(r.id, r.name);
    return m;
  }, [roles]);

  const groupNamesById = useMemo(() => {
    const m = new Map<string, string>();
    for (const g of groups) m.set(g.id, g.name);
    return m;
  }, [groups]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setResult(null);

    if (!asUserId) {
      setError("Pick a user first.");
      return;
    }
    if (!targetKind || !action) {
      setError("Pick a kind and an action.");
      return;
    }

    try {
      const r = await explain.mutateAsync({
        asUserId,
        action,
        targetKind,
        targetId: targetId.trim() || null
      });
      setResult(r);
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <>
      <PageHeader
        title="Effective Permissions"
        description="Pick a user, action, and target. The evaluator replays each grant in isolation and shows you the matching trace, so you can see exactly which rule (or absence of one) drove the decision."
      />

      {error && (
        <Alert color="red" variant="light" mb="sm">
          {error}
        </Alert>
      )}

      <Card withBorder shadow="sm">
        <Title order={5} mb="md">
          Query
        </Title>

        <Box component="form" onSubmit={submit}>
          <Grid align="flex-end">
            <Grid.Col span={{ base: 12, md: 3 }}>
              <Select
                label="User"
                size="xs"
                value={asUserId}
                onChange={setAsUserId}
                placeholder="— pick a user —"
                data={users.map((u) => ({ value: u.userId, label: u.username }))}
                searchable
                clearable
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 2 }}>
              <Select
                label="Kind"
                size="xs"
                value={targetKind}
                onChange={(v) => {
                  setTargetKind(v);
                  setAction(null);
                }}
                placeholder="— pick a kind —"
                data={kinds.map((k) => k.kind)}
                allowDeselect={false}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 2 }}>
              <Select
                label="Action"
                size="xs"
                value={action}
                onChange={setAction}
                placeholder="— pick an action —"
                data={actionOptions}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 3 }}>
              <TextInput
                label={
                  <>
                    Target id <Text component="span" c="dimmed" size="xs">(optional)</Text>
                  </>
                }
                size="xs"
                placeholder="leave blank for kind-level check"
                value={targetId}
                onChange={(e) => setTargetId(e.currentTarget.value)}
                styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 2 }}>
              <Button type="submit" size="xs" fullWidth loading={explain.isPending}>
                {explain.isPending ? "Evaluating…" : "Explain"}
              </Button>
            </Grid.Col>
          </Grid>
        </Box>
      </Card>

      {result && (
        <ExplanationView
          result={result}
          users={Object.fromEntries(users.map((u) => [u.userId, u.username]))}
          roleNamesById={roleNamesById}
          groupNamesById={groupNamesById}
        />
      )}
    </>
  );
}

function ExplanationView({
  result,
  users,
  roleNamesById,
  groupNamesById
}: {
  result: ExplainResult;
  users: Record<string, string>;
  roleNamesById: Map<string, string>;
  groupNamesById: Map<string, string>;
}) {
  const isAllow = result.effect === "allow";
  return (
    <>
      <Alert color={isAllow ? "green" : "red"} variant="light" mt="md">
        <Group gap="xs" align="center" wrap="wrap">
          <Text fw={700} tt="uppercase">
            {result.effect}
          </Text>
          <Text>{result.reason}</Text>
        </Group>
        <Group gap="md" mt="xs">
          <Text size="sm">
            User: <strong>{users[result.asUserId] ?? result.asUserId}</strong>
          </Text>
          {result.isSuperAdmin && (
            <Badge color="yellow" variant="filled">
              SuperAdmin
            </Badge>
          )}
          <Text size="sm">Groups: {result.groupIds.length}</Text>
          <Text size="sm">Roles: {result.roleIds.length}</Text>
        </Group>
      </Alert>

      {!result.isSuperAdmin && (
        <Card withBorder shadow="sm" mt="md">
          <Title order={5} mb="md">
            Grants considered
          </Title>
          {result.grants.length === 0 ? (
            <Text c="dimmed">
              The user has no grants for this action via direct assignment, group membership, or
              role.
            </Text>
          ) : (
            <Table verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Source</Table.Th>
                  <Table.Th>Action</Table.Th>
                  <Table.Th>Selector</Table.Th>
                  <Table.Th>Effect</Table.Th>
                  <Table.Th>Matched</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {result.grants.map((g, i) => (
                  <GrantRow
                    key={i}
                    grant={g}
                    userLabel={users[g.principalId]}
                    roleLabel={roleNamesById.get(g.principalId)}
                    groupLabel={groupNamesById.get(g.principalId)}
                  />
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Card>
      )}
    </>
  );
}

function GrantRow({
  grant,
  userLabel,
  roleLabel,
  groupLabel
}: {
  grant: ExplainGrant;
  userLabel?: string;
  roleLabel?: string;
  groupLabel?: string;
}) {
  const principalName =
    grant.principalName ??
    (grant.principalKind === "user"
      ? userLabel
      : grant.principalKind === "role"
      ? roleLabel
      : groupLabel) ??
    grant.principalId;

  let matchedCell: React.ReactNode;
  if (grant.matched === true) {
    matchedCell = (
      <Badge color="green" variant="filled">
        match
      </Badge>
    );
  } else if (grant.matched === false) {
    matchedCell = <Text c="dimmed">no match</Text>;
  } else {
    matchedCell = (
      <Text c="yellow" title={grant.error ?? "Not evaluated"}>
        n/a {grant.error ? `— ${grant.error}` : ""}
      </Text>
    );
  }

  return (
    <Table.Tr>
      <Table.Td>
        <Badge color="gray" variant="filled" mr={8} tt="uppercase">
          {grant.principalKind}
        </Badge>
        <strong>{principalName}</strong>
      </Table.Td>
      <Table.Td>
        <Code>{grant.action}</Code>
      </Table.Td>
      <Table.Td>
        <Code>{grant.selectorString}</Code>
      </Table.Td>
      <Table.Td>
        <Badge color={grant.effect === "allow" ? "cyan" : "red"} variant="filled">
          {grant.effect}
        </Badge>
      </Table.Td>
      <Table.Td>{matchedCell}</Table.Td>
    </Table.Tr>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
