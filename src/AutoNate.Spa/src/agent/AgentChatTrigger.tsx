import { UnstyledButton } from "@mantine/core";
import { useAgentSidebar } from "./AgentSidebarContext";
import {
  applyHeaderHover,
  clearHeaderHover,
  headerIconButtonStyle
} from "@/shell/headerStyles";

// Header icon that toggles the chat sidebar. Sits alongside the other header
// icons. Icon appearance is intentionally identical whether the chat is open
// or closed — same color, same row — so users can always find the toggle in
// the same spot.
//
// In overlay+over-header mode the chatbot panel covers this icon — that's
// the expected behavior of overlay mode (don't push anything). Users close
// the chatbot via the X button in the panel's top-right corner.
export default function AgentChatTrigger() {
  const { isOpen, toggle } = useAgentSidebar();

  return (
    <UnstyledButton
      aria-pressed={isOpen}
      aria-label={isOpen ? "Close Auton8 assistant" : "Open Auton8 assistant"}
      title={isOpen ? "Close assistant" : "Ask the assistant"}
      onClick={() => toggle()}
      style={headerIconButtonStyle}
      onMouseEnter={applyHeaderHover}
      onMouseLeave={clearHeaderHover}
    >
      <i className="fa fa-robot" />
    </UnstyledButton>
  );
}
