import { Routes, Route } from "react-router-dom";

import AppShell from "./shell/AppShell";
import AuthShell from "./shell/AuthShell";
import ProtectedRoute from "./shell/ProtectedRoute";
import Login from "./pages/login/Login";
import DynamicPageRoute from "./pages/dynamic-page/DynamicPageRoute";
import MantineSmokePage from "./pages/_mantineSmoke/MantineSmokePage";
import { renderAppRoutes } from "./routes/appRoutes";

export default function Router() {
  return (
    <Routes>
      <Route element={<AuthShell />}>
        <Route index element={<Login />} />
      </Route>

      <Route path="__mantine_test" element={<MantineSmokePage />} />

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
