import { Fragment, ReactElement, ReactNode } from "react";
import { Navigate, Route, matchPath, useParams } from "react-router-dom";
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
import FormEditor from "@/pages/admin/config/forms/FormEditor";
import FormDevView from "@/pages/forms/FormDevView";
import FormPublicView from "@/pages/forms/FormPublicView";
import TaskFormPage from "@/pages/workflow-tasks/TaskFormPage";
import Notifications from "@/pages/notifications/Notifications";
import NotesPage from "@/pages/notes/NotesPage";
import DynamicPageRoute from "@/pages/dynamic-page/DynamicPageRoute";
import { PAGE_TEMPLATES } from "@/pageTemplates";

export type AppRoute = {
  path?: string;
  index?: boolean;
  element: ReactElement;
  children?: AppRoute[];
};

const protect = (node: ReactElement) => <ProtectedRoute>{node}</ProtectedRoute>;

// Substitutes :param tokens in `to` with the current route's matched params,
// then renders a replace-mode <Navigate>. Used to forward legacy URLs (e.g.
// /record-edge-types/:id → /record-relationship-types/:id) without losing
// the path parameter.
function RedirectWithParams({ to }: { to: string }) {
  const params = useParams();
  const target = to.replace(/:([A-Za-z0-9_]+)/g, (_, key: string) => params[key] ?? "");
  return <Navigate to={target} replace />;
}

// (path, templateKey) pairs that mount a built-in page template at a fixed
// URL inside `admin/config`. Each entry contributes one APP_ROUTE child below
// AND lets the menu-item validator recognize that placing a template menu item
// at that path is the design — not a route collision (see `findCollidingAppRoute`).
const CONFIG_TEMPLATE_ANCHORS: readonly { path: string; templateKey: string }[] = [
  { path: "general", templateKey: "configGeneral" },
  { path: "features", templateKey: "configFeatures" },
  { path: "appearance", templateKey: "configAppearance" },
  { path: "status-appearance", templateKey: "configStatusAppearance" },
  { path: "external-connections", templateKey: "configExternalConnections" },
  { path: "pages-menus", templateKey: "configPagesMenus" },
  { path: "bus-watcher", templateKey: "configBusWatcher" },
  { path: "events", templateKey: "configEvents" },
  { path: "system-health", templateKey: "configSystemHealth" },
  { path: "users", templateKey: "configSecurityUsers" },
  { path: "groups", templateKey: "configSecurityGroups" },
  { path: "roles", templateKey: "configSecurityRoles" },
  { path: "permissions", templateKey: "configSecurityPermissions" },
  { path: "permission-checker", templateKey: "configSecurityPermissionChecker" },
  { path: "plugins", templateKey: "configPlugins" },
  { path: "plugins/documentation", templateKey: "configPluginDocumentation" },
  { path: "forms", templateKey: "configForms" },
  { path: "form-mappings", templateKey: "configFormMappings" },
  { path: "chatbot-settings", templateKey: "configChatbotSettings" },
  { path: "chatbot-models", templateKey: "configChatbotModels" }
];

// Absolute-path → templateKey index, materialized once for the validator.
const TEMPLATE_ANCHOR_PATHS = new Map<string, string>(
  CONFIG_TEMPLATE_ANCHORS.map((a) => [`/admin/config/${a.path}`, a.templateKey])
);

// templateKey → absolute anchor path, so the edit-menu UI can snap the path
// field to the new template's canonical URL when the user swaps templates.
const TEMPLATE_KEY_TO_ANCHOR_PATH = new Map<string, string>(
  CONFIG_TEMPLATE_ANCHORS.map((a) => [a.templateKey, `/admin/config/${a.path}`])
);

// Returns the canonical mount path for `templateKey` if one is hard-routed in
// APP_ROUTES (template-anchor route), otherwise null. Used by the menu-item
// editor to auto-fix the path when the admin switches templates.
export function anchorPathForTemplateKey(templateKey: string): string | null {
  return TEMPLATE_KEY_TO_ANCHOR_PATH.get(templateKey) ?? null;
}

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

  // Record-relationship-types domain (mirrors record-types). The route was
  // previously /record-edge-types — redirects below preserve old bookmarks
  // and any existing menu items still pointing at the legacy path.
  { path: "record-relationship-types", element: protect(<EdgeTypeList />) },
  { path: "record-relationship-types/:id", element: protect(<EdgeTypeEditor />) },
  { path: "record-edge-types", element: <Navigate to="/record-relationship-types" replace /> },
  {
    path: "record-edge-types/:id",
    element: <RedirectWithParams to="/record-relationship-types/:id" />
  },

  // Notifications inbox (per-user; not a configurable page template)
  { path: "notifications", element: protect(<Notifications />) },

  // Content hierarchy: project → cabinet → notebook → page → note. Full-bleed
  // layout (project picker + cabinet rail + explorer + tab strip + editors)
  // that fills <AppShell.Main>; the page itself cancels the shell's default
  // padding via `.app-shell-content-edge` on the outer container.
  //
  // Single splat route so NotesPage stays mounted across every /notes/...
  // URL. Three sibling routes (one per segment count) would unmount + remount
  // the page on every page-tab ↔ note-tab transition, throwing away state
  // and triggering nav cascades that React 19's "call ref on every change"
  // semantics turn into setState loops in Mantine's ref-merging utilities.
  // The first splat segment is the entity locator (any kind) and the optional
  // second segment is the page-scoped note index.
  { path: "notes/*", element: protect(<NotesPage />) },

  // Forms feature: dev preview (draft) and runtime render (published).
  // Both require an authenticated user; the runtime render is additionally
  // gated server-side by `site_available`. The "public" naming on the
  // FormPublicView component / `/api/forms/public/...` endpoint is legacy
  // and does NOT mean anonymous — see FormEndpoints.cs.
  { path: "formdev/:shortCode", element: protect(<FormDevView />) },
  { path: "form/:shortCode", element: protect(<FormPublicView />) },

  // Full-page workflow user-task form (mode="page" on the user task).
  // Modal-mode tasks render in place via TaskFormModal; this route is the
  // navigation target for tasks where the author chose Form Page.
  { path: "workflow-tasks/:taskId/form", element: protect(<TaskFormPage />) },

  // Site Configuration shell — children are page templates rendered inside the
  // layout's <Outlet />. Mounted at the templates' default_path so the URL
  // works whether reached through the site-config menu or directly.
  {
    path: "admin/config",
    element: protect(<ConfigLayout />),
    children: [
      { index: true, element: <ConfigIndex /> },
      ...CONFIG_TEMPLATE_ANCHORS.map((a) => ({
        path: a.path,
        element: template(a.templateKey)
      })),
      { path: "forms/:id", element: protect(<FormEditor />) },
      // Catch-all so menu items added by plugins under /admin/config/* render
      // inside ConfigLayout's sidebar shell. The dynamic page component reads
      // the menu_item config (path/content/contentType) and renders it.
      { path: "*", element: protect(<DynamicPageRoute />) }
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
    // Skip splat catch-alls ("*") — they exist as fallbacks for admin-authored
    // pages, so the paths they "match" are exactly the paths we want to allow.
    if (r.path === "*") continue;
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
//
// Template-anchor routes (e.g. /admin/config/general) are intentional mount
// points for their associated template — placing a template menu item at that
// path with the matching `templateKey` is the design, not a collision. Pass
// the picked templateKey in `opts.templateKey` to suppress the false positive;
// callers that don't know (or are validating a non-template item) omit it and
// get the original strict behavior.
export function findCollidingAppRoute(
  path: string,
  opts?: { templateKey?: string }
): string | null {
  for (const pattern of flattenAppRoutes()) {
    if (!matchPath({ path: pattern, end: true }, path)) continue;
    const anchorTemplateKey = TEMPLATE_ANCHOR_PATHS.get(pattern);
    if (anchorTemplateKey && opts?.templateKey === anchorTemplateKey) {
      // Template-anchored route matching the same templateKey the admin
      // picked — not a collision, just the canonical mount point.
      continue;
    }
    return pattern;
  }
  return null;
}
