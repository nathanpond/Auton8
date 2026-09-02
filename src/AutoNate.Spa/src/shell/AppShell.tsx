import { useEffect, useState } from "react";
import { Outlet } from "react-router-dom";
import { AppShell as MantineAppShell } from "@mantine/core";
import NavMenu from "./NavMenu";
import { AgentSidebar } from "@/agent/AgentSidebar";
import { AgentSidebarProvider } from "@/agent/AgentSidebarContext";
import { PageContextRegistryProvider } from "@/agent/pageContext/PageContextRegistry";
import { UserPreferencesProvider } from "@/preferences/UserPreferencesContext";
import PreferencesModal from "@/preferences/PreferencesModal";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";
import { useRouteDocumentTitle } from "@/hooks/useRouteDocumentTitle";
import { useRouteFocus } from "@/hooks/useRouteFocus";
import "./shell.css";

export default function AppShell() {
  const isScrollable = usePageIsScrollable();
  // One place that keeps document.title in step with the route (archived-18).
  const { effectiveAppearance } = useSiteAppearance();
  useRouteDocumentTitle(effectiveAppearance.siteName);
  // Focus the main region on navigation so a screen reader announces the new
  // page and Tab resumes inside it rather than back in the header (archived-15).
  useRouteFocus();
  return (
    <UserPreferencesProvider>
      <PageContextRegistryProvider>
        <AgentSidebarProvider>
          {/* Keyboard users skip the full NavMenu on every page; the link is
              visually hidden until focus lands on it. Targets the #content
              wrapper below — it has tabIndex={-1} so focus can land on it. */}
          <a href="#content" className="skip-to-content">
            Skip to main content
          </a>
          <MantineAppShell header={{ height: 56 }} padding={0}>
            <MantineAppShell.Header withBorder={false}>
              <NavMenu />
            </MantineAppShell.Header>

            <MantineAppShell.Main>
              <div id="content" className="app-shell-content" tabIndex={-1}>
                <Outlet />
              </div>
            </MantineAppShell.Main>
          </MantineAppShell>

          <AgentSidebar />

          {isScrollable && (
            <a
              href="#"
              className="btn-scroll-to-top"
              onClick={(e) => {
                e.preventDefault();
                window.scrollTo({ top: 0, behavior: "smooth" });
              }}
              aria-label="Scroll to top"
            >
              <i className="fa fa-angle-up" />
            </a>
          )}

          <PreferencesModal />
        </AgentSidebarProvider>
      </PageContextRegistryProvider>
    </UserPreferencesProvider>
  );
}

// True when the document is tall enough to scroll AND the user has actually
// scrolled away from the top. Watches viewport resizes, content-size changes
// (route swaps, panels expanding, async data filling the page) via a
// ResizeObserver on <body>, and window scroll.
function usePageIsScrollable(): boolean {
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    const check = () => {
      const scrollable = document.documentElement.scrollHeight > window.innerHeight + 1;
      setVisible(scrollable && window.scrollY > 0);
    };
    check();
    window.addEventListener("resize", check);
    window.addEventListener("scroll", check, { passive: true });
    const observer = new ResizeObserver(check);
    observer.observe(document.body);
    return () => {
      window.removeEventListener("resize", check);
      window.removeEventListener("scroll", check);
      observer.disconnect();
    };
  }, []);
  return visible;
}
