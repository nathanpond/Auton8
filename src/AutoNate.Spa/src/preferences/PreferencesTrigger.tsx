import { MouseEvent } from "react";
import { useUserPreferences } from "./UserPreferencesContext";

// Header gear icon that opens the Preferences modal. Mirrors AgentChatTrigger
// so it sits cleanly alongside the other right-strip header icons in NavMenu.
export default function PreferencesTrigger() {
  const { isModalOpen, openModal } = useUserPreferences();

  const onClick = (e: MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    openModal();
  };

  return (
    <div className="menu-item">
      <a
        href="#"
        className={`menu-link menu-link-tight ${isModalOpen ? "active" : ""}`}
        role="button"
        aria-pressed={isModalOpen}
        aria-label="Open preferences"
        title="Preferences"
        onClick={onClick}
      >
        <div className="menu-icon">
          <i className="fa fa-gear"></i>
        </div>
      </a>
    </div>
  );
}
