import { Outlet } from "react-router-dom";
import NavMenu from "./NavMenu";
import { AgentSidebar } from "@/agent/AgentSidebar";
import { AgentSidebarProvider } from "@/agent/AgentSidebarContext";
import { PageContextRegistryProvider } from "@/agent/pageContext/PageContextRegistry";
import { UserPreferencesProvider } from "@/preferences/UserPreferencesContext";
import PreferencesModal from "@/preferences/PreferencesModal";
import "./shell.css";

export default function AppShell() {
  return (
    <UserPreferencesProvider>
      <PageContextRegistryProvider>
        <AgentSidebarProvider>
          <div id="app" className="app app-without-header app-without-sidebar app-with-top-menu">
            <NavMenu />

            <div id="content" className="app-content">
              <Outlet />
            </div>

            <AgentSidebar />

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
          <PreferencesModal />
        </AgentSidebarProvider>
      </PageContextRegistryProvider>
    </UserPreferencesProvider>
  );
}
