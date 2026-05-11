import { Alert, Badge, Box, Group, Paper, Stack, Text, Title } from "@mantine/core";
import { ComponentHealth, ConnectionHealth, HealthStatus } from "@/api/health";
import { useSystemHealth } from "@/hooks/useSystemHealth";
import "./SystemHealth.css";

export default function SystemHealth() {
  const { data, isLoading, isError, error, dataUpdatedAt, isFetching } = useSystemHealth();

  if (isLoading) {
    return (
      <Box py="md">
        <Title order={1} mb="sm">
          System Health
        </Title>
        <Text c="dimmed">Probing components…</Text>
      </Box>
    );
  }

  if (isError || !data) {
    return (
      <Box py="md">
        <Title order={1} mb="sm">
          System Health
        </Title>
        <Alert color="red" variant="light">
          {error instanceof Error ? error.message : "Failed to load system health."}
        </Alert>
      </Box>
    );
  }

  const summary = summarize(data.components, data.connections);
  const componentsById = new Map(data.components.map((c) => [c.id, c]));

  return (
    <Box py="md">
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="md" mb="lg">
        <Stack gap={4}>
          <Title order={1}>System Health</Title>
          <Text size="sm" c="dimmed" maw={680}>
            Live status of every component and the connections between them. Refreshes
            automatically every 5 seconds.
          </Text>
        </Stack>
        <Stack gap={6} align="flex-end">
          <Text size="xs" c="dimmed">
            Last checked: <strong>{new Date(dataUpdatedAt).toLocaleTimeString()}</strong>
            {isFetching && <i className="fa fa-rotate fa-spin" aria-hidden style={{ marginLeft: 8 }} />}
          </Text>
          <Group gap="xs">
            <SummaryPill label="Services" up={summary.componentsUp} total={summary.componentsTotal} />
            <SummaryPill
              label="Connections"
              up={summary.connectionsUp}
              total={summary.connectionsTotal}
            />
          </Group>
        </Stack>
      </Group>

      <div className="system-health-grid">
        <Paper withBorder radius="md" className="system-health-services">
          <Group gap="xs" px="md" py="sm" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
            <i className="fa fa-cubes" aria-hidden />
            <Text fw={600}>Services</Text>
          </Group>
          <ul className="system-health-service-list">
            {data.components.map((component) => (
              <ServiceRow key={component.id} component={component} />
            ))}
          </ul>
        </Paper>

        <Paper withBorder radius="md" className="system-health-connections-panel">
          <Group gap="xs" px="md" py="sm" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
            <i className="fa fa-link" aria-hidden />
            <Text fw={600}>Connections</Text>
          </Group>
          <ul className="system-health-connection-list">
            {data.connections.map((connection, index) => (
              <ConnectionLink
                key={`${connection.from}->${connection.to}-${index}`}
                connection={connection}
                componentsById={componentsById}
              />
            ))}
          </ul>
        </Paper>
      </div>
    </Box>
  );
}

function ServiceRow({ component }: { component: ComponentHealth }) {
  return (
    <li
      className={`system-health-service-row status-${component.status.toLowerCase()}`}
      title={component.message ?? component.status}
    >
      <span className="system-health-dot" aria-hidden="true" />
      <i className={`fa ${kindIcon(component.kind)} system-health-service-icon`} aria-hidden="true" />
      <span className="system-health-service-name">{component.name}</span>
      <span className="system-health-service-status">{component.status}</span>
    </li>
  );
}

function ConnectionLink({
  connection,
  componentsById
}: {
  connection: ConnectionHealth;
  componentsById: Map<string, ComponentHealth>;
}) {
  const fromName = componentsById.get(connection.from)?.name ?? connection.from;
  const toName = componentsById.get(connection.to)?.name ?? connection.to;
  const lineClass = `system-health-line status-${connection.status.toLowerCase()}`;
  const latencyText = connection.latencyMs != null ? `${connection.latencyMs} ms` : null;
  const detailLines = [connection.label, latencyText].filter(Boolean).join(" · ");

  return (
    <li
      className={`system-health-connection status-${connection.status.toLowerCase()}`}
      title={connection.message ?? connection.status}
    >
      <span className="system-health-endpoint left">{fromName}</span>
      <div className="system-health-line-wrap">
        <span className="system-health-line-label">{detailLines}</span>
        <span className={lineClass} aria-hidden="true">
          <span className="system-health-line-track" />
          <span className="system-health-line-arrow">
            <i className="fa fa-caret-right" aria-hidden="true" />
          </span>
        </span>
        {connection.message && (
          <span className={`system-health-line-message status-${connection.status.toLowerCase()}`}>
            {connection.message}
          </span>
        )}
      </div>
      <span className="system-health-endpoint right">{toName}</span>
    </li>
  );
}

function SummaryPill({ label, up, total }: { label: string; up: number; total: number }) {
  const allHealthy = up === total;
  const color = allHealthy ? "green" : up === 0 ? "red" : "yellow";
  return (
    <Badge color={color} radius="xl" variant="filled">
      {label}: {up}/{total}
    </Badge>
  );
}

function kindIcon(kind: string): string {
  switch (kind) {
    case "service":
      return "fa-server";
    case "database":
      return "fa-database";
    case "broker":
      return "fa-tower-broadcast";
    case "sidecar":
      return "fa-cube";
    case "component":
      return "fa-puzzle-piece";
    case "cache":
      return "fa-bolt";
    case "control-plane":
      return "fa-tower-cell";
    default:
      return "fa-circle-question";
  }
}

function summarize(components: ComponentHealth[], connections: ConnectionHealth[]) {
  return {
    componentsUp: components.filter((c) => c.status === "Up").length,
    componentsTotal: components.length,
    connectionsUp: connections.filter((c) => c.status === "Up").length,
    connectionsTotal: connections.length
  };
}

// Keep the unused HealthStatus import live for type narrowing in callers.
export type { HealthStatus };
