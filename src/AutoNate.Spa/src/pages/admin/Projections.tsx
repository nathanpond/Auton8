import { useCallback, useEffect, useState } from "react";
import { Alert, Badge, Button, Code, Group, Table, Text, Tooltip } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  ProjectionHealthSnapshot,
  listProjections,
  pauseProjection,
  resumeProjection,
  rebuildProjection,
} from "@/api/projections";

// Admin view of every registered projection's runtime health, with
// pause/resume/rebuild controls. Surfaces three things admins look at when a
// dashboard widget is showing stale data:
//   1. Has the projection applied recently? (LastAppliedAtUtc)
//   2. Are there recent failures? (LastFailureMessage)
//   3. Is it paused by another admin? (Paused)
//
// v1 uses a plain Mantine Table — the dataset is tiny (one row per
// projection, maybe a dozen for a full deploy) so we skip the heavier
// DataTable's pagination/search machinery. Re-fetches every 5s while open
// so admins watching a backfill don't need to mash refresh.
export default function Projections() {
  const [rows, setRows] = useState<ProjectionHealthSnapshot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyName, setBusyName] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const data = await listProjections();
      setRows(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    const id = window.setInterval(() => void refresh(), 5_000);
    return () => window.clearInterval(id);
  }, [refresh]);

  const runAction = useCallback(
    async (name: string, action: () => Promise<unknown>) => {
      setBusyName(name);
      setError(null);
      try {
        await action();
        await refresh();
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err));
      } finally {
        setBusyName(null);
      }
    },
    [refresh]
  );

  return (
    <>
      <PageHeader title="Projections" />
      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}
      {loading && rows.length === 0 ? (
        <Text c="dimmed">Loading…</Text>
      ) : rows.length === 0 ? (
        <Text c="dimmed">No projections registered.</Text>
      ) : (
        <Table striped withTableBorder withColumnBorders>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Projection</Table.Th>
              <Table.Th>Version</Table.Th>
              <Table.Th>State</Table.Th>
              <Table.Th>Events applied</Table.Th>
              <Table.Th>Failures</Table.Th>
              <Table.Th>Last apply</Table.Th>
              <Table.Th>Feeds</Table.Th>
              <Table.Th>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((p) => {
              const isBusy = busyName === p.name;
              return (
                <Table.Tr key={p.name}>
                  <Table.Td>
                    <Code>{p.name}</Code>
                    {p.lastFailureMessage && (
                      <Text size="xs" c="red" mt={4}>
                        Last error: {p.lastFailureMessage.slice(0, 120)}
                        {p.lastFailureMessage.length > 120 ? "…" : ""}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Code>v{p.version}</Code>
                  </Table.Td>
                  <Table.Td>
                    {p.paused ? (
                      <Badge color="orange">Paused</Badge>
                    ) : (
                      <Badge color="green">Running</Badge>
                    )}
                  </Table.Td>
                  <Table.Td>{p.eventsAppliedTotal.toLocaleString()}</Table.Td>
                  <Table.Td>
                    <Text c={p.applyFailuresTotal > 0 ? "red" : undefined}>
                      {p.applyFailuresTotal.toLocaleString()}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {p.lastAppliedAtUtc ? (
                      <Text size="sm" c="dimmed">
                        {new Date(p.lastAppliedAtUtc).toLocaleString()}
                      </Text>
                    ) : (
                      <Text size="sm" c="dimmed">
                        never
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {p.feeds.length === 0 ? (
                      <Text size="sm" c="dimmed">
                        none observed
                      </Text>
                    ) : (
                      <Tooltip
                        label={p.feeds
                          .map((f) => `${f.feedName}: ${f.eventsObservedTotal.toLocaleString()}`)
                          .join("\n")}
                        multiline
                        withinPortal
                      >
                        <Text size="sm" c="dimmed" style={{ cursor: "help" }}>
                          {p.feeds.length} feed{p.feeds.length === 1 ? "" : "s"}
                        </Text>
                      </Tooltip>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {p.paused ? (
                        <Button
                          size="xs"
                          color="green"
                          disabled={isBusy}
                          onClick={() => void runAction(p.name, () => resumeProjection(p.name))}
                        >
                          Resume
                        </Button>
                      ) : (
                        <Button
                          size="xs"
                          color="orange"
                          variant="outline"
                          disabled={isBusy}
                          onClick={() => void runAction(p.name, () => pauseProjection(p.name))}
                        >
                          Pause
                        </Button>
                      )}
                      <Tooltip label="Run a full backfill (requires a registered backfill source)">
                        <Button
                          size="xs"
                          variant="default"
                          disabled={isBusy}
                          onClick={() => void runAction(p.name, () => rebuildProjection(p.name))}
                        >
                          Rebuild
                        </Button>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      )}
    </>
  );
}
