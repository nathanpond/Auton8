import type { YjsConnectionStatus } from "./useYjsDocument";
import type { YjsRole } from "./ticket";

interface Props {
  status: YjsConnectionStatus;
  role?: YjsRole;
}

// Right-aligned status indicator that replaces the old "Saving… / Auto-saved"
// pill from the pre-Yjs autosave path. Edits are durable as soon as the
// HocuspocusProvider acks them (or, while offline, as soon as y-indexeddb
// writes them locally — they merge into Hocuspocus on reconnect via Yjs
// CRDT semantics). So there's no separate "saved" state to surface —
// only the connection lifecycle.
//
// When role is "viewer", the pill flips to a "View only" state regardless
// of connection state. Viewers' connections are server-side readOnly; the
// pill makes that visible so the user knows why their editor isn't
// editable.
export function ConnectionStatusPill({ status, role }: Props) {
  const { iconClass, label, color } =
    role === "viewer" ? viewerDisplay() : display(status);
  return (
    <span style={{ fontSize: 11, fontWeight: 600, color }}>
      <i className={iconClass} style={{ marginRight: 5 }} />
      {label}
    </span>
  );
}

function viewerDisplay(): { iconClass: string; label: string; color: string } {
  return {
    iconClass: "fa fa-eye",
    label: "View only",
    // Same green as "Live" — viewer is the correct state for the user's
    // permissions, not an error. Using red/orange would imply something
    // is broken.
    color: "var(--mantine-color-green-7, #2f9e44)"
  };
}

function display(status: YjsConnectionStatus): {
  iconClass: string;
  label: string;
  color: string;
} {
  switch (status) {
    case "connected":
      return {
        iconClass: "fa fa-circle-check",
        label: "Live",
        color: "var(--mantine-color-green-7, #2f9e44)"
      };
    case "connecting":
      return {
        iconClass: "fa fa-circle-notch fa-spin",
        label: "Connecting…",
        color: "var(--mantine-color-gray-6, #868e96)"
      };
    case "reconnecting":
      return {
        iconClass: "fa fa-rotate",
        label: "Reconnecting…",
        color: "var(--mantine-color-yellow-7, #f08c00)"
      };
    case "offline":
    default:
      // "Offline" here means "browser reports no network." Edits are still
      // captured locally by y-indexeddb and will sync once reconnected,
      // so we use the warning palette rather than danger.
      return {
        iconClass: "fa fa-cloud-arrow-down",
        label: "Offline · edits cached",
        color: "var(--mantine-color-orange-7, #e8590c)"
      };
  }
}
