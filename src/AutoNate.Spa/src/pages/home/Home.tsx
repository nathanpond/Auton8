import { Link } from "react-router-dom";
import MyTasksPanel from "./MyTasksPanel";

export default function Home() {
  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Automation Dashboard</h1>
          <p className="page-head-copy">
            ColorAdmin drives the application shell, navigation, and dashboard styling. Use the
            workflow tools below to model, inspect, and monitor automations.
          </p>
        </div>
      </div>

      <div className="row g-3 mb-4">
        <StatCard
          gradient="bg-gradient-blue"
          icon="fa-diagram-project"
          title="WORKFLOW DESIGN"
          big="Studio Ready"
          copy="Build, save, deploy, and launch BPMN flows"
        />
        <StatCard
          gradient="bg-gradient-green"
          icon="fa-list-check"
          title="EXECUTION TRACE"
          big="Live History"
          copy="Inspect active and completed workflow runs"
        />
        <StatCard
          gradient="bg-gradient-orange-red"
          icon="fa-tower-broadcast"
          title="EVENT STREAM"
          big="Bus Watcher"
          copy="Observe integration traffic in real time"
        />
        <StatCard
          gradient="bg-gradient-gray-dark"
          icon="fa-layer-group"
          title="THEME STATUS"
          big="ColorAdmin"
          copy="All runtime assets are served from the main app"
        />
      </div>

      <div className="row g-3 mb-4">
        <QuickLink to="/workflow" icon="fa-diagram-project text-primary" title="Open Workflow Studio">
          Create new process drafts, iterate on BPMN diagrams, and deploy directly to Flowable.
        </QuickLink>
        <QuickLink to="/workflow-executions" icon="fa-list-check text-success" title="Review Executions">
          Track the newest runs, inspect their current step, and drill into full diagram state.
        </QuickLink>
        <QuickLink to="/bus-watcher" icon="fa-tower-broadcast text-warning" title="Monitor Bus Traffic">
          Watch the app's workflow event stream and validate published payloads as they arrive.
        </QuickLink>
      </div>

      <div className="row g-3">
        <div className="col-12">
          <MyTasksPanel />
        </div>
      </div>
    </>
  );
}

type StatProps = { gradient: string; icon: string; title: string; big: string; copy: string };
function StatCard({ gradient, icon, title, big, copy }: StatProps) {
  return (
    <div className="col-xl-3 col-md-6">
      <div className={`widget widget-stats ${gradient}`}>
        <div className="stats-icon stats-icon-lg">
          <i className={`fa ${icon} fa-fw`}></i>
        </div>
        <div className="stats-content">
          <div className="stats-title">{title}</div>
          <div className="dashboard-stat-number">{big}</div>
          <div className="dashboard-stat-label">{copy}</div>
        </div>
      </div>
    </div>
  );
}

type LinkProps = { to: string; icon: string; title: string; children: React.ReactNode };
function QuickLink({ to, icon, title, children }: LinkProps) {
  return (
    <div className="col-xl-4">
      <Link className="quick-link-card" to={to}>
        <div className="quick-link-card-title">
          <i className={`fa ${icon}`}></i>
          <span>{title}</span>
        </div>
        <p className="quick-link-card-copy">{children}</p>
      </Link>
    </div>
  );
}
