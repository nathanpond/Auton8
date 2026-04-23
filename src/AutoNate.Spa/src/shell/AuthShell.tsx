import { Outlet } from "react-router-dom";

export default function AuthShell() {
  return (
    <div id="app" className="app">
      <Outlet />
    </div>
  );
}
