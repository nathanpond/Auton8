import { Outlet } from "react-router-dom";
import { AppShell as MantineAppShell } from "@mantine/core";
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
          <MantineAppShell header={{ height: 56 }} padding={0}>
            <MantineAppShell.Header withBorder={false}>
              <NavMenu />
            </MantineAppShell.Header>

            <MantineAppShell.Main>
              <div id="content" className="app-shell-content">
                <Outlet />
              </div>
            </MantineAppShell.Main>
          </MantineAppShell>

          <AgentSidebar />

          <a
            href="#"
            className="btn-scroll-to-top"
            onClick={(e) => {
              e.preventDefault();
              window.scrollTo({ top: 0, behavior: "smooth" });
            }}
            aria-label="Scroll to top"
          >
            <i className="fa fa-angle-up"></i>
          </a>

          <PreferencesModal />
        </AgentSidebarProvider>
      </PageContextRegistryProvider>
    </UserPreferencesProvider>
  );
}
