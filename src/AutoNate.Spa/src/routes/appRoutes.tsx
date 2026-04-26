import { Fragment, ReactElement, ReactNode } from "react";
import { Route } from "react-router-dom";
import ProtectedRoute from "@/shell/ProtectedRoute";

import Home from "@/pages/home/Home";
import UserProfile from "@/pages/user-profile/UserProfile";
import BusWatcher from "@/pages/bus-watcher/BusWatcher";
import ManageUsers from "@/pages/manage-users/ManageUsers";
import WorkflowExecutions from "@/pages/workflow-executions/WorkflowExecutions";
import WorkflowStudio from "@/pages/workflow/WorkflowStudio";
import RecordTypeList from "@/pages/record-types/RecordTypeList";
import RecordTypeEditor from "@/pages/record-types/RecordTypeEditor";
import RecordList from "@/pages/records/RecordList";
import RecordCreate from "@/pages/records/RecordCreate";
import RecordDetail from "@/pages/records/RecordDetail";
import EdgeTypeList from "@/pages/edge-types/EdgeTypeList";
import EdgeTypeEditor from "@/pages/edge-types/EdgeTypeEditor";
import AdminRoles from "@/pages/admin/Roles";
import AdminGroups from "@/pages/admin/Groups";
import AdminGrants from "@/pages/admin/Grants";
import AdminHierarchy from "@/pages/admin/Hierarchy";
import AdminExplain from "@/pages/admin/Explain";
import ConfigLayout from "@/pages/admin/config/ConfigLayout";
import PagesMenus from "@/pages/admin/config/PagesMenus";
import StatusAppearance from "@/pages/admin/config/StatusAppearance";
import {
  ConfigIndex,
  SecurityManageGroups,
  SecurityManageRoles,
  SecurityManageUsers,
  SecurityPermissionChecker,
  SecuritySetPermissions,
  SitewideAppearance,
  SitewideExternalConnections,
  SitewideFeatures,
  SitewideGeneral
} from "@/pages/admin/config/sections";

export type AppRoute = {
  path?: string;
  index?: boolean;
  element: ReactElement;
  children?: AppRoute[];
};

const protect = (node: ReactElement) => <ProtectedRoute>{node}</ProtectedRoute>;

// Single source of truth for the app's protected routes mounted under
// AppShell. Used by router.tsx for normal rendering, and by DynamicPageRoute
// for alias resolution (rendering the target route's component at the alias
// URL via React Router's `<Routes location={target}>`).
export const APP_ROUTES: AppRoute[] = [
  { path: "home", element: protect(<Home />) },
  { path: "user-profile", element: protect(<UserProfile />) },
  { path: "bus-watcher", element: protect(<BusWatcher />) },
  { path: "manage-users", element: protect(<ManageUsers />) },
  { path: "workflow-executions", element: protect(<WorkflowExecutions />) },
  { path: "workflow", element: protect(<WorkflowStudio />) },
  { path: "record-types", element: protect(<RecordTypeList />) },
  { path: "record-types/:id", element: protect(<RecordTypeEditor />) },
  { path: "records/:typeShortCode/new", element: protect(<RecordCreate />) },
  { path: "records/:typeShortCode/:key", element: protect(<RecordDetail />) },
  { path: "record/:key", element: protect(<RecordDetail />) },
  { path: "records/:typeShortCode", element: protect(<RecordList />) },
  { path: "record-edge-types", element: protect(<EdgeTypeList />) },
  { path: "record-edge-types/:id", element: protect(<EdgeTypeEditor />) },
  { path: "admin/roles", element: protect(<AdminRoles />) },
  { path: "admin/groups", element: protect(<AdminGroups />) },
  { path: "admin/grants", element: protect(<AdminGrants />) },
  { path: "admin/hierarchy", element: protect(<AdminHierarchy />) },
  { path: "admin/explain", element: protect(<AdminExplain />) },
  {
    path: "admin/config",
    element: protect(<ConfigLayout />),
    children: [
      { index: true, element: <ConfigIndex /> },
      { path: "general", element: <SitewideGeneral /> },
      { path: "features", element: <SitewideFeatures /> },
      { path: "appearance", element: <SitewideAppearance /> },
      { path: "status-appearance", element: <StatusAppearance /> },
      { path: "external-connections", element: <SitewideExternalConnections /> },
      { path: "pages-menus", element: <PagesMenus /> },
      { path: "users", element: <SecurityManageUsers /> },
      { path: "groups", element: <SecurityManageGroups /> },
      { path: "roles", element: <SecurityManageRoles /> },
      { path: "permissions", element: <SecuritySetPermissions /> },
      { path: "permission-checker", element: <SecurityPermissionChecker /> }
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
