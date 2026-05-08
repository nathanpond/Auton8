import { MouseEvent } from "react";
import { useAgentSidebar } from "./AgentSidebarContext";

// Header icon that toggles the chat sidebar. Mirrors the menu-item / menu-link
// styling used by NotificationBell so it sits cleanly alongside the other
// header icons.
export default function AgentChatTrigger() {
  const { isOpen, toggle } = useAgentSidebar();

  const onClick = (e: MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    toggle();
  };

  return (
    <div className="menu-item">
      <a
        href="#"
        className={`menu-link menu-link-tight ${isOpen ? "active" : ""}`}
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
    </div>
  );
}
