import { Fragment, ReactElement, ReactNode } from "react";
import { Route, matchPath } from "react-router-dom";
import ProtectedRoute from "@/shell/ProtectedRoute";

import WorkflowExecutions from "@/pages/workflow-executions/WorkflowExecutions";
import ExecutionPage from "@/pages/workflow-executions/ExecutionPage";
import WorkflowStudio from "@/pages/workflow/WorkflowStudio";
import RecordTypeList from "@/pages/record-types/RecordTypeList";
import RecordTypeEditor from "@/pages/record-types/RecordTypeEditor";
import RecordList from "@/pages/records/RecordList";
import RecordCreate from "@/pages/records/RecordCreate";
import RecordDetail from "@/pages/records/RecordDetail";
import EdgeTypeList from "@/pages/edge-types/EdgeTypeList";
import EdgeTypeEditor from "@/pages/edge-types/EdgeTypeEditor";
import ConfigLayout from "@/pages/admin/config/ConfigLayout";
import { ConfigIndex } from "@/pages/admin/config/sections";
import { PAGE_TEMPLATES } from "@/pageTemplates";

export type AppRoute = {
  path?: string;
  index?: boolean;
  element: ReactElement;
  children?: AppRoute[];
};

const protect = (node: ReactElement) => <ProtectedRoute>{node}</ProtectedRoute>;

// A single template wrapped in ProtectedRoute. Used to mount templates as
// hardcoded children of layout shells (today: ConfigLayout) so the section
// renders inside the shell while still living in the template registry.
const template = (key: string) => protect(PAGE_TEMPLATES[key]);

// APP_ROUTES is intentionally narrow: it covers only routes that can't be
// admin-configured menu items because they are parameterized (records,
// record-types, workflow executions) or are layout shells whose children are
// themselves templates (admin/config). Everything else lives in
// PAGE_TEMPLATES and is reachable only when an admin places it on a menu.
export const APP_ROUTES: AppRoute[] = [
  // Workflow domain (parameterized + tightly coupled siblings)
  { path: "workflow", element: protect(<WorkflowStudio />) },
  { path: "workflow-executions", element: protect(<WorkflowExecutions />) },
  { path: "executions/:id", element: protect(<ExecutionPage />) },

  // Record-types domain
  { path: "record-types", element: protect(<RecordTypeList />) },
  { path: "record-types/:id", element: protect(<RecordTypeEditor />) },

  // Records domain
  { path: "records/:typeShortCode/new", element: protect(<RecordCreate />) },
  { path: "records/:typeShortCode/:key", element: protect(<RecordDetail />) },
  { path: "record/:key", element: protect(<RecordDetail />) },
  { path: "records/:typeShortCode", element: protect(<RecordList />) },

  // Record edge-types domain (mirrors record-types)
  { path: "record-edge-types", element: protect(<EdgeTypeList />) },
  { path: "record-edge-types/:id", element: protect(<EdgeTypeEditor />) },

  // Site Configuration shell — children are page templates rendered inside the
  // layout's <Outlet />. Mounted at the templates' default_path so the URL
  // works whether reached through the site-config menu or directly.
  {
    path: "admin/config",
    element: protect(<ConfigLayout />),
    children: [
      { index: true, element: <ConfigIndex /> },
      { path: "general", element: template("configGeneral") },
      { path: "features", element: template("configFeatures") },
      { path: "appearance", element: template("configAppearance") },
      { path: "status-appearance", element: template("configStatusAppearance") },
      { path: "external-connections", element: template("configExternalConnections") },
      { path: "pages-menus", element: template("configPagesMenus") },
      { path: "bus-watcher", element: template("configBusWatcher") },
      { path: "events", element: template("configEvents") },
      { path: "system-health", element: template("configSystemHealth") },
      { path: "users", element: template("configSecurityUsers") },
      { path: "groups", element: template("configSecurityGroups") },
      { path: "roles", element: template("configSecurityRoles") },
      { path: "permissions", element: template("configSecurityPermissions") },
      { path: "permission-checker", element: template("configSecurityPermissionChecker") }
    ]
  }
];

export function renderAppRoutes(routes: AppRoute[] = APP_ROUTES): ReactNode {
  return (
    <Fragment>
      {routes.map((r, i) =>
        r.index ? (
          <Route key={`index-${i}`} index element={r.element} />
        ) : (
          <Route key={r.path} path={r.path} element={r.element}>
            {r.children && renderAppRoutes(r.children)}
          </Route>
        )
      )}
    </Fragment>
  );
}

// Flattens APP_ROUTES into absolute path patterns (with parameters intact like
// "/records/:typeShortCode") so admin-authored paths can be checked against
// the static routing table for collisions.
function flattenAppRoutes(
  routes: AppRoute[] = APP_ROUTES,
  prefix = ""
): string[] {
  const out: string[] = [];
  for (const r of routes) {
    if (r.index || !r.path) continue;
    const full = `/${prefix}/${r.path}`.replace(/\/+/g, "/");
    out.push(full);
    if (r.children) {
      const childPrefix = `${prefix}/${r.path}`.replace(/\/+/g, "/");
      out.push(...flattenAppRoutes(r.children, childPrefix));
    }
  }
  return out;
}

// Returns the colliding route pattern (e.g. "/records/:typeShortCode") if the
// given admin path would be shadowed by a built-in route, or null otherwise.
// Uses react-router's matcher so parameterized routes are evaluated correctly.
export function findCollidingAppRoute(path: string): string | null {
  for (const pattern of flattenAppRoutes()) {
    if (matchPath({ path: pattern, end: true }, path)) return pattern;
  }
  return null;
}
