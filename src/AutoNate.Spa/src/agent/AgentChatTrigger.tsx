import { MouseEvent, useEffect, useLayoutEffect, useState } from "react";
import { createPortal } from "react-dom";
import { useAgentSidebar } from "./AgentSidebarContext";
import { useUserPreferences } from "@/preferences/UserPreferencesContext";

// Header icon that toggles the chat sidebar. Mirrors the menu-item / menu-link
// styling used by NotificationBell so it sits cleanly alongside the other
// header icons. Icon appearance is intentionally identical whether the chat
// is open or closed — same color, same row — so users can always find the
// toggle in the same spot.
//
// In "over-header" mode the sidebar covers the top nav at z-index 1030, and
// #top-menu's stacking context (z-index 1015) traps any child z-index, so
// the in-flow icon would be hidden. To keep the icon visible while
// preserving the over-header coverage everywhere else, we render a
// portal-mounted clone in the root stacking context, positioned just to
// the left of the sidebar so it sits in the still-visible portion of the
// header strip.
export default function AgentChatTrigger() {
  const { isOpen, toggle } = useAgentSidebar();
  const { chatbotOverHeader } = useUserPreferences();
  const [overlayLeft, setOverlayLeft] = useState<number | null>(null);
  const showOverlay = isOpen && chatbotOverHeader;

  useLayoutEffect(() => {
    if (!showOverlay) {
      setOverlayLeft(null);
      return;
    }
    const ICON_WIDTH = 40;
    const GAP = 4;
    const measure = () => {
      const sidebar = document.querySelector(".agent-sidebar");
      if (!sidebar) return;
      const rect = sidebar.getBoundingClientRect();
      setOverlayLeft(Math.max(0, rect.left - ICON_WIDTH - GAP));
    };
    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    // Re-measure after the sidebar's open transition (160ms width animation).
    const t = window.setTimeout(measure, 200);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
      window.clearTimeout(t);
    };
  }, [showOverlay]);

  useEffect(() => {
    if (!showOverlay) return;
    const sidebar = document.querySelector(".agent-sidebar");
    if (!sidebar) return;
    const ICON_WIDTH = 40;
    const GAP = 4;
    const ro = new ResizeObserver(() => {
      const rect = sidebar.getBoundingClientRect();
      setOverlayLeft(Math.max(0, rect.left - ICON_WIDTH - GAP));
    });
    ro.observe(sidebar);
    return () => ro.disconnect();
  }, [showOverlay]);

  const onClick = (e: MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    toggle();
  };

  const link = (
    <a
      href="#"
      className="menu-link menu-link-tight"
      role="button"
      aria-pressed={isOpen}
      aria-label={isOpen ? "Close AutoNate assistant" : "Open AutoNate assistant"}
      title={isOpen ? "Close assistant" : "Ask the assistant"}
      onClick={onClick}
    >
      <div className="menu-icon">
        <i className="fa fa-robot"></i>
      </div>
    </a>
  );

  return (
    <>
      <div className="menu-item">
        {link}
      </div>
      {showOverlay && overlayLeft !== null &&
        createPortal(
          <div
            className="menu-item agent-chat-trigger-overlay"
            style={{
              position: "fixed",
              top: 0,
              left: overlayLeft,
              width: 40,
              height: 40
            }}
          >
            {link}
          </div>,
          document.body
        )}
    </>
  );
}
