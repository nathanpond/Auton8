import { Routes, Route } from "react-router-dom";

import AppShell from "./shell/AppShell";
import AuthShell from "./shell/AuthShell";
import ProtectedRoute from "./shell/ProtectedRoute";
import Login from "./pages/login/Login";
import DynamicPageRoute from "./pages/dynamic-page/DynamicPageRoute";
import DocumentEditorPage from "./pages/documents/DocumentEditorPage";
import DocumentPreviewPage from "./pages/documents/DocumentPreviewPage";
import PublicSharedQueryPage from "./pages/query/PublicSharedQueryPage";
import { renderAppRoutes } from "./routes/appRoutes";

export default function Router() {
  return (
    <Routes>
      <Route element={<AuthShell />}>
        <Route index element={<Login />} />
      </Route>

      {/* Document editor lives OUTSIDE the AppShell so the editor has full
          bleed — no NavMenu, no footer, no shell padding. The folder/grid
          views opening into here use target="_blank", which combined with
          this route shape gives the Google-Docs-style "editor opens in a
          new tab without the app chrome" experience. */}
      <Route
        path="/documents/edit/:documentId"
        element={
          <ProtectedRoute>
            <DocumentEditorPage />
          </ProtectedRoute>
        }
      />

      {/* Read-only "populated output" preview (Phase 11) — same full-bleed,
          outside-the-shell treatment as the editor. */}
      <Route
        path="/documents/preview/:documentId"
        element={
          <ProtectedRoute>
            <DocumentPreviewPage />
          </ProtectedRoute>
        }
      />

      {/* Public share recipient surface (audit fix archived-9). NOT wrapped in
          ProtectedRoute — anonymous recipients land here via a link
          pasted into Slack / email / a wiki page. The page calls the
          /api/public/queries/share/{token} endpoint which authenticates
          via the share token rather than a session cookie. Outside the
          AppShell so there's no nav chrome or attempt to load the
          user's menus when no user is signed in. */}
      <Route path="/q/:token" element={<PublicSharedQueryPage />} />

      <Route element={<AppShell />}>
        {renderAppRoutes()}
        <Route
          path="*"
          element={
            <ProtectedRoute>
              <DynamicPageRoute />
            </ProtectedRoute>
          }
        />
      </Route>
    </Routes>
  );
}
