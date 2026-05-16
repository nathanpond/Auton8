import { useEffect } from "react";
import type { HocuspocusProvider } from "@hocuspocus/provider";
import type {
  ExcalidrawImperativeAPI,
  Collaborator,
  SocketId
} from "@excalidraw/excalidraw/types";
import { avatarUrl } from "./avatarUrl";

export interface ExcalidrawAwarenessUser {
  id: string;
  displayName: string;
  // Background hex for the cursor + selection chrome.
  color: string;
}

export interface UseExcalidrawAwarenessResult {
  // Pass to <Excalidraw onPointerUpdate>. Forwards local pointer state
  // into Yjs awareness so remote clients can render this user's cursor.
  onPointerUpdate: (payload: {
    pointer: { x: number; y: number; tool: "pointer" | "laser" };
    button: "down" | "up";
  }) => void;
}

// Plugs Yjs awareness into Excalidraw's collaborator API. Two paths:
//
//   Local → remote: setLocalStateField writes our pointer + user info
//     into the awareness Map. HocuspocusProvider broadcasts the diff to
//     every connected client over the same WebSocket the Y.Doc uses.
//
//   Remote → local: the awareness "change" event fires whenever another
//     client updates its state. We rebuild a Collaborators Map keyed by
//     clientID and push it through Excalidraw's imperative API.
//
// Cursors stay local-only (state is cleared when the tab closes), so
// stale cursors don't linger if a user disconnects ungracefully.
export function useExcalidrawAwareness(args: {
  provider: HocuspocusProvider;
  excalidrawAPI: ExcalidrawImperativeAPI | null;
  currentUser: ExcalidrawAwarenessUser;
}): UseExcalidrawAwarenessResult {
  const { provider, excalidrawAPI, currentUser } = args;

  // Publish local user identity once per (provider, user) — keeps the
  // user info present in our awareness slot even before any pointer
  // movement, so other clients see us in their collaborator list as
  // soon as we connect.
  useEffect(() => {
    const awareness = provider.awareness;
    if (!awareness) return;
    awareness.setLocalStateField("user", {
      id: currentUser.id,
      name: currentUser.displayName,
      color: currentUser.color
    });
    return () => {
      // Clearing on unmount makes the cursor disappear from peers
      // immediately rather than waiting for the WS close timeout.
      awareness.setLocalStateField("user", null);
      awareness.setLocalStateField("pointer", null);
      awareness.setLocalStateField("button", null);
    };
  }, [provider, currentUser.id, currentUser.displayName, currentUser.color]);

  // Subscribe to remote changes. Build the collaborators Map every time
  // any peer's state changes, then push it via updateScene. Excalidraw
  // handles the actual cursor render.
  useEffect(() => {
    const awareness = provider.awareness;
    if (!awareness || !excalidrawAPI) return;

    const onChange = () => {
      const collaborators = new Map<SocketId, Collaborator>();
      awareness.getStates().forEach((state, clientId) => {
        if (clientId === awareness.clientID) return; // skip self
        const u = state.user as
          | { id: string; name: string; color: string }
          | null
          | undefined;
        if (!u) return;
        collaborators.set(String(clientId) as SocketId, {
          id: u.id,
          username: u.name,
          color: { background: u.color, stroke: u.color },
          avatarUrl: avatarUrl(u.id, u.name),
          pointer: state.pointer as Collaborator["pointer"],
          button: state.button as Collaborator["button"]
        });
      });
      excalidrawAPI.updateScene({ collaborators });
    };

    awareness.on("change", onChange);
    onChange(); // initial sync — peers that connected before us
    return () => {
      awareness.off("change", onChange);
    };
  }, [provider, excalidrawAPI]);

  const onPointerUpdate: UseExcalidrawAwarenessResult["onPointerUpdate"] = (payload) => {
    const awareness = provider.awareness;
    if (!awareness) return;
    awareness.setLocalStateField("pointer", payload.pointer);
    awareness.setLocalStateField("button", payload.button);
  };

  return { onPointerUpdate };
}
