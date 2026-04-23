import { Outlet } from "react-router-dom";
import NavMenu from "./NavMenu";
import "./shell.css";

export default function AppShell() {
  return (
    <div id="app" className="app app-without-header app-without-sidebar app-with-top-menu">
      <NavMenu />

      <div id="content" className="app-content">
        <Outlet />
      </div>

      <a
        href="#"
        className="btn btn-icon btn-circle btn-success btn-scroll-to-top"
        data-toggle="scroll-to-top"
        onClick={(e) => {
          e.preventDefault();
          window.scrollTo({ top: 0, behavior: "smooth" });
        }}
      >
        <i className="fa fa-angle-up"></i>
      </a>
    </div>
  );
}
