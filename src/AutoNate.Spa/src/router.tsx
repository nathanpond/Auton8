import { Routes, Route } from "react-router-dom";

import AppShell from "./shell/AppShell";
import AuthShell from "./shell/AuthShell";
import ProtectedRoute from "./shell/ProtectedRoute";

import Login from "./pages/login/Login";
import Home from "./pages/home/Home";
import UserProfile from "./pages/user-profile/UserProfile";
import BusWatcher from "./pages/bus-watcher/BusWatcher";
import ManageUsers from "./pages/manage-users/ManageUsers";
import WorkflowExecutions from "./pages/workflow-executions/WorkflowExecutions";
import WorkflowStudio from "./pages/workflow/WorkflowStudio";
import NotFound from "./pages/not-found/NotFound";

export default function Router() {
  return (
    <Routes>
      <Route element={<AuthShell />}>
        <Route index element={<Login />} />
      </Route>

      <Route element={<AppShell />}>
        <Route
          path="home"
          element={
            <ProtectedRoute>
              <Home />
            </ProtectedRoute>
          }
        />
        <Route
          path="user-profile"
          element={
            <ProtectedRoute>
              <UserProfile />
            </ProtectedRoute>
          }
        />
        <Route
          path="bus-watcher"
          element={
            <ProtectedRoute>
              <BusWatcher />
            </ProtectedRoute>
          }
        />
        <Route
          path="manage-users"
          element={
            <ProtectedRoute>
              <ManageUsers />
            </ProtectedRoute>
          }
        />
        <Route
          path="workflow-executions"
          element={
            <ProtectedRoute>
              <WorkflowExecutions />
            </ProtectedRoute>
          }
        />
        <Route
          path="workflow"
          element={
            <ProtectedRoute>
              <WorkflowStudio />
            </ProtectedRoute>
          }
        />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}
