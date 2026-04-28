import { ComponentHealth, ConnectionHealth, HealthStatus } from "@/api/health";
import { useSystemHealth } from "@/hooks/useSystemHealth";
import "./SystemHealth.css";

export default function SystemHealth() {
  const { data, isLoading, isError, error, dataUpdatedAt, isFetching } = useSystemHealth();

  if (isLoading) {
    return (
      <>
        <div className="page-head">
          <h1 className="page-header mb-1">System Health</h1>
        </div>
        <p className="text-muted">Probing components…</p>
      </>
    );
  }

  if (isError || !data) {
    return (
      <>
        <div className="page-head">
          <h1 className="page-header mb-1">System Health</h1>
        </div>
        <div className="alert alert-danger">
          {error instanceof Error ? error.message : "Failed to load system health."}
        </div>
      </>
    );
  }

  const summary = summarize(data.components, data.connections);
  const componentsById = new Map(data.components.map((c) => [c.id, c]));

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start gap-3">
        <div>
          <h1 className="page-header mb-1">System Health</h1>
          <p className="page-head-copy mb-0">
            Live status of every component and the connections between them. Refreshes
            automatically every 5 seconds.
          </p>
        </div>
        <div className="text-end small text-muted">
          <div>
            Last checked: <strong>{new Date(dataUpdatedAt).toLocaleTimeString()}</strong>
            {isFetching && <i className="fa fa-rotate fa-spin ms-2" aria-hidden="true" />}
          </div>
          <div className="mt-1">
            <SummaryPill label="Services" up={summary.componentsUp} total={summary.componentsTotal} />
            <SummaryPill
              label="Connections"
              up={summary.connectionsUp}
              total={summary.connectionsTotal}
            />
          </div>
        </div>
      </div>

      <div className="system-health-grid">
        <div className="panel panel-inverse system-health-services">
          <div className="panel-heading">
            <h4 className="panel-title">
              <i className="fa fa-cubes me-2" aria-hidden="true" />
              Services
            </h4>
          </div>
          <div className="panel-body p-0">
            <ul className="system-health-service-list">
              {data.components.map((component) => (
                <ServiceRow key={component.id} component={component} />
              ))}
            </ul>
          </div>
        </div>

        <div className="panel panel-inverse system-health-connections-panel">
          <div className="panel-heading">
            <h4 className="panel-title">
              <i className="fa fa-link me-2" aria-hidden="true" />
              Connections
            </h4>
          </div>
          <div className="panel-body">
            <ul className="system-health-connection-list">
              {data.connections.map((connection, index) => (
                <ConnectionLink
                  key={`${connection.from}->${connection.to}-${index}`}
                  connection={connection}
                  componentsById={componentsById}
                />
              ))}
            </ul>
          </div>
        </div>
      </div>
    </>
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
  const cls = allHealthy ? "bg-success" : up === 0 ? "bg-danger" : "bg-warning text-dark";
  return (
    <span className={`badge rounded-pill me-2 ${cls}`}>
      {label}: {up}/{total}
    </span>
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
