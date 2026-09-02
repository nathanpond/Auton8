import { ReactElement } from "react";

// Side-effect import: registers every shipped widget into the registry
// at module load. The dashboard template (registered below) relies on
// the registry having a populated map by the time it renders. Importing
// here (rather than from each widget consumer) means the registry is
// warm regardless of which template the user lands on first.
import "@/widgets";

import Dashboard from "@/pages/dashboard/Dashboard";
import Home from "@/pages/home/Home";
import QueryPage from "@/pages/query/QueryPage";
import UserProfile from "@/pages/user-profile/UserProfile";
import ManageUsers from "@/pages/manage-users/ManageUsers";
import BusWatcher from "@/pages/bus-watcher/BusWatcher";
import AdminRoles from "@/pages/admin/Roles";
import AdminGroups from "@/pages/admin/Groups";
import AdminGrants from "@/pages/admin/Grants";
import AdminHierarchy from "@/pages/admin/Hierarchy";
import AdminExplain from "@/pages/admin/Explain";
import AdminPlugins from "@/pages/admin/Plugins";
import AdminProjections from "@/pages/admin/Projections";
import StatusAppearance from "@/pages/admin/config/StatusAppearance";
import ModelCatalogPage from "@/pages/admin/config/chatbot/ModelCatalogPage";
import DataStoresPage from "@/pages/admin/datastores/DataStoresPage";
import DataConnectorsPage from "@/pages/admin/dataconnectors/DataConnectorsPage";
import DatasetsPage from "@/pages/admin/datasets/DatasetsPage";
import PipelinesPage from "@/pages/admin/pipelines/PipelinesPage";
import CodeTransformersPage from "@/pages/admin/code-transformers/CodeTransformersPage";
import SiteAppearancePage from "@/pages/admin/config/SiteAppearance";
import PagesMenus from "@/pages/admin/config/PagesMenus";
import Events from "@/pages/admin/config/Events";
import SystemHealth from "@/pages/admin/config/SystemHealth";
import SystemIssues from "@/pages/admin/config/SystemIssues";
import PluginDocumentation from "@/pages/admin/config/PluginDocumentation";
import FormsList from "@/pages/admin/config/forms/FormsList";
import {
  SitewideChatbotSettings,
  SitewideExternalConnections,
  SitewideFeatures,
  SitewideGeneral
} from "@/pages/admin/config/sections";

// Registry of every built-in page template the SPA ships. Keys must match the
// `key` column in the page_templates table. A template is reachable only if a
// menu item somewhere references its key — DynamicPageRoute walks the page
// registry coming back from /api/pages and renders the corresponding entry
// here when a path resolves to itemType="template".
export const PAGE_TEMPLATES: Record<string, ReactElement> = {
  home: <Home />,
  userProfile: <UserProfile />,
  manageUsers: <ManageUsers />,
  busWatcher: <BusWatcher />,
  adminRoles: <AdminRoles />,
  adminGroups: <AdminGroups />,
  adminGrants: <AdminGrants />,
  adminHierarchy: <AdminHierarchy />,
  adminExplain: <AdminExplain />,
  adminPlugins: <AdminPlugins />,
  configGeneral: <SitewideGeneral />,
  configFeatures: <SitewideFeatures />,
  configAppearance: <SiteAppearancePage />,
  configStatusAppearance: <StatusAppearance />,
  configExternalConnections: <SitewideExternalConnections />,
  configPagesMenus: <PagesMenus />,
  configBusWatcher: <BusWatcher />,
  configEvents: <Events />,
  configSystemHealth: <SystemHealth />,
  configSystemIssues: <SystemIssues />,
  // The Site Configuration → Security menu is seeded against these keys, and
  // the seeded page_templates rows describe them as "mounted inside Site
  // Config" — the intent was always the real screens. They rendered "coming
  // soon" stubs instead, so the shipped user/role/permission admin was
  // unreachable from the one place the seed points an admin at (#42). Same
  // components as the manageUsers/adminRoles/… keys, mounted under the
  // Site Config keys.
  configSecurityUsers: <ManageUsers />,
  configSecurityGroups: <AdminGroups />,
  configSecurityRoles: <AdminRoles />,
  configSecurityPermissions: <AdminGrants />,
  configSecurityPermissionChecker: <AdminExplain />,
  configPlugins: <AdminPlugins />,
  configPluginDocumentation: <PluginDocumentation />,
  configProjections: <AdminProjections />,
  configForms: <FormsList />,
  configChatbotSettings: <SitewideChatbotSettings />,
  configChatbotModels: <ModelCatalogPage />,
  dataStores: <DataStoresPage />,
  dataConnectors: <DataConnectorsPage />,
  datasets: <DatasetsPage />,
  pipelines: <PipelinesPage />,
  codeTransformers: <CodeTransformersPage />,
  dashboard: <Dashboard />,
  query: <QueryPage />
};
