import { useEffect, useMemo, useRef } from "react";
import * as Y from "yjs";
import type { HocuspocusProvider } from "@hocuspocus/provider";
import type {
  ExcalidrawImperativeAPI,
  ExcalidrawInitialDataState
} from "@excalidraw/excalidraw/types";

// Tag every local-originated transaction with this symbol. Our remote-
// change observers ignore transactions tagged with it, which prevents the
// "I wrote to the Y.Doc → observer fires → I call updateScene with my own
// data → Excalidraw fires onChange → I write to Y.Doc" feedback loop.
const LOCAL_ORIGIN = Symbol("yjs-excalidraw-local");

// Excalidraw's per-element shape. We treat each element as an arbitrary
// key/value bag; only `id` and `version` are load-bearing for our diff.
type ExcalidrawElement = {
  id: string;
  version?: number;
  [key: string]: unknown;
};

export interface UseYjsExcalidrawResult {
  // Hand this to Excalidraw's `initialData` prop. Reflects the Y.Doc state
  // captured at first hook-render. Subsequent changes flow through
  // `excalidrawAPI.updateScene` via the remote observer below.
  initialData: ExcalidrawInitialDataState;
  // Hand this to Excalidraw's `onChange` prop. Diffs the new state against
  // the Y.Doc and writes the minimal updates needed.
  onChange: (
    elements: readonly ExcalidrawElement[],
    appState: unknown,
    files: unknown
  ) => void;
}

// Bidirectional sync between an Excalidraw editor and a Y.Doc.
//
// Y.Doc layout:
//   doc.getArray("elements")  → one Y.Map per Excalidraw element, keyed
//                                internally by element id
//   doc.getMap("appState")    → slimmed appState (theme, grid, viewport,
//                                current-tool preferences) — see
//                                PERSISTED_APP_STATE_KEYS below
//
// Element ordering: Y.Array order mirrors the Excalidraw elements array.
// We don't currently re-order existing elements on remote-only z-order
// changes (Y.Array has no "move" operation; we'd have to delete+insert
// which is messy in CRDT terms). New elements append to the end. This is
// the documented Phase-4 limitation; users who reorder shapes may see
// short-lived stacking divergence until the next Excalidraw onChange
// re-synchronizes everything.
export function useYjsExcalidraw(args: {
  doc: Y.Doc;
  provider: HocuspocusProvider;
  excalidrawAPI: ExcalidrawImperativeAPI | null;
}): UseYjsExcalidrawResult {
  const { doc, provider, excalidrawAPI } = args;

  // Gate the local→Y.Doc writer on (a) Hocuspocus's initial sync AND
  // (b) the bootstrap effect having run. Without this, Excalidraw's
  // first onChange (fired with empty `elements: []` because
  // `initialData` was captured before Hocuspocus delivered the saved
  // state) gets interpreted by the diff below as "user deleted
  // everything", silently wiping the saved drawing on open.
  //
  // `provider.synced` is necessary but not sufficient: sync can finish
  // before Excalidraw mounts, and Excalidraw still fires its initial
  // empty onChange AFTER mount and BEFORE the bootstrap effect re-applies
  // the saved scene. `bootstrappedRef` closes that second window — the
  // bootstrap effect flips it true after calling updateScene (or after
  // confirming Y.Doc has nothing to bootstrap).
  const syncedRef = useRef<boolean>(provider.synced);
  const bootstrappedRef = useRef<boolean>(false);
  // Flag set while we're pushing Y.Doc state back to Excalidraw to recover
  // from the spurious-empty-onChange race; prevents the resulting echo
  // onChange from re-triggering the recovery and looping forever.
  const recoveringRef = useRef<boolean>(false);
  useEffect(() => {
    syncedRef.current = provider.synced;
    if (provider.synced) return;
    const onSynced = () => {
      syncedRef.current = true;
    };
    provider.on("synced", onSynced);
    return () => {
      provider.off("synced", onSynced);
    };
  }, [provider]);

  // Stable references to the shared containers — fetched once per Y.Doc.
  const elementsArray = useMemo(
    () => doc.getArray<Y.Map<unknown>>("elements"),
    [doc]
  );
  const appStateMap = useMemo(() => doc.getMap<unknown>("appState"), [doc]);

  // Captured at first hook-render. We deliberately do NOT recompute when
  // elementsArray/appStateMap change — Excalidraw only reads `initialData`
  // at mount time. Post-mount updates flow through the observer below.
   
  const initialData = useMemo<ExcalidrawInitialDataState>(() => {
    const elements = elementsArray.toArray().map(yMapToPojo);
    const appState = yMapToPojo(appStateMap);
    // Excalidraw's element/appState types are strictly typed discriminated
    // unions; we store + materialize them as opaque bags. Cast via
    // `unknown` so TS accepts the structural pass-through.
    return {
      elements: elements as unknown as ExcalidrawInitialDataState["elements"],
      appState: appState as unknown as ExcalidrawInitialDataState["appState"],
      files: {}
    };
  }, []);

  // Subscribe to remote changes. Both element edits and appState changes
  // bypass the local feedback loop by checking transaction.origin.
  useEffect(() => {
    if (!excalidrawAPI) return;

    const onElementsChange = (
      _events: Array<Y.YEvent<Y.Map<unknown>> | Y.YEvent<Y.Array<Y.Map<unknown>>>>,
      transaction: Y.Transaction
    ) => {
      if (transaction.origin === LOCAL_ORIGIN) return;
      const elements = elementsArray.toArray().map(yMapToPojo);
      excalidrawAPI.updateScene({
        elements: elements as unknown as Parameters<
          typeof excalidrawAPI.updateScene
        >[0]["elements"]
      });
    };
    const onAppStateChange = (
      _event: Y.YMapEvent<unknown>,
      transaction: Y.Transaction
    ) => {
      if (transaction.origin === LOCAL_ORIGIN) return;
      const appState = yMapToPojo(appStateMap);
      excalidrawAPI.updateScene({
        appState: appState as unknown as Parameters<
          typeof excalidrawAPI.updateScene
        >[0]["appState"]
      });
    };

    elementsArray.observeDeep(onElementsChange);
    appStateMap.observe(onAppStateChange);

    // Bootstrap the scene from whatever's currently in Y.Doc. Hocuspocus's
    // initial server-state sync is async, so it may have landed AFTER
    // Excalidraw mounted (when `initialData` was captured empty) but
    // BEFORE this observer is set up — neither path delivers the saved
    // content to Excalidraw, leaving the canvas blank on a saved
    // drawing. Applying once on observer-attach closes the race. The
    // bootstrappedRef flag also unlocks the local writer (see onChange
    // below) so an empty Excalidraw onChange that fires before this
    // runs can't wipe the saved scene.
    const currentElements = elementsArray.toArray().map(yMapToPojo);
    const currentAppState = yMapToPojo(appStateMap);
    if (currentElements.length > 0 || Object.keys(currentAppState).length > 0) {
      excalidrawAPI.updateScene({
        elements: currentElements as unknown as Parameters<
          typeof excalidrawAPI.updateScene
        >[0]["elements"],
        appState: currentAppState as unknown as Parameters<
          typeof excalidrawAPI.updateScene
        >[0]["appState"]
      });
    }
    bootstrappedRef.current = true;

    return () => {
      elementsArray.unobserveDeep(onElementsChange);
      appStateMap.unobserve(onAppStateChange);
    };
  }, [excalidrawAPI, elementsArray, appStateMap]);

  // Local-to-remote writer. Diffs incoming elements against the Y.Array
  // by id: existing elements update only if their `version` changed,
  // missing-from-incoming elements get deleted, new elements append.
  // All mutations wrap in a single doc.transact so observers see one
  // batched change rather than n element-level events.
  const onChange = (
    elements: readonly ExcalidrawElement[],
    appState: unknown
  ) => {
    // Refuse to write until BOTH conditions hold:
    //   1. Hocuspocus has completed its initial sync (server's saved
    //      state has been merged into the Y.Doc).
    //   2. The bootstrap effect has run (Excalidraw has been
    //      updateScene'd with whatever was in Y.Doc, so any incoming
    //      onChange reflects the real scene — not an empty mount
    //      state that hasn't seen the saved data yet).
    // Without (2), Excalidraw's first onChange post-mount fires with
    // `elements: []` AFTER `synced` becomes true but BEFORE the
    // bootstrap effect, and the diff below interprets that as "user
    // deleted everything" — silently wiping every saved element.
    if (!syncedRef.current || !bootstrappedRef.current) return;
    // Empty-onChange-against-non-empty-Y.Doc guard. Excalidraw can fire
    // a spurious empty onChange after a refresh (the visual "drawing
    // briefly shows, then flashes blank" the user sees) — interpreting
    // that as "user deleted everything" wipes the saved scene. Refuse
    // the destructive diff AND push the saved Y.Doc state back into
    // Excalidraw so the canvas re-renders the drawing the data layer
    // actually has. recoveringRef breaks the loop so the resulting
    // echo-onChange doesn't trigger another recovery push.
    if (elements.length === 0 && elementsArray.length > 0) {
      if (!recoveringRef.current) {
        recoveringRef.current = true;
        const recovered = elementsArray.toArray().map(yMapToPojo);
        const recoveredAppState = yMapToPojo(appStateMap);
        excalidrawAPI?.updateScene({
          elements: recovered as unknown as Parameters<
            NonNullable<typeof excalidrawAPI>["updateScene"]
          >[0]["elements"],
          appState: recoveredAppState as unknown as Parameters<
            NonNullable<typeof excalidrawAPI>["updateScene"]
          >[0]["appState"]
        });
        // Clear the flag a tick later — Excalidraw's echo onChange
        // fires synchronously after updateScene, so the flag has
        // already short-circuited it by the time this microtask runs.
        // The 250 ms slack catches any additional empty-onChange burst
        // from the same race without permanently blocking legitimate
        // user clears.
        setTimeout(() => {
          recoveringRef.current = false;
        }, 250);
      }
      return;
    }
    doc.transact(() => {
      const incomingIds = new Set<string>();
      const currentById = new Map<string, Y.Map<unknown>>();
      for (let i = 0; i < elementsArray.length; i++) {
        const yMap = elementsArray.get(i);
        const id = yMap.get("id");
        if (typeof id === "string") currentById.set(id, yMap);
      }

      for (const el of elements) {
        incomingIds.add(el.id);
        const existing = currentById.get(el.id);
        if (!existing) {
          const yMap = new Y.Map<unknown>();
          for (const [k, v] of Object.entries(el)) yMap.set(k, v);
          elementsArray.push([yMap]);
        } else {
          // version-gated update: Excalidraw bumps `version` on every
          // change, so we can skip the field writes when nothing changed.
          if (existing.get("version") !== el.version) {
            for (const [k, v] of Object.entries(el)) existing.set(k, v);
          }
        }
      }

      // Backward sweep so deletions don't shift the indexes we just
      // computed.
      for (let i = elementsArray.length - 1; i >= 0; i--) {
        const yMap = elementsArray.get(i);
        const id = yMap.get("id");
        if (typeof id === "string" && !incomingIds.has(id)) {
          elementsArray.delete(i, 1);
        }
      }

      // Slim appState before writing. Per-key compare so we don't churn
      // Y.Map ops for unchanged values.
      const slim = pickPersistedAppState(appState);
      for (const [k, v] of Object.entries(slim)) {
        if (appStateMap.get(k) !== v) appStateMap.set(k, v);
      }
    }, LOCAL_ORIGIN);
  };

  return { initialData, onChange };
}

// Excalidraw's appState is huge and includes ephemeral fields (cursor
// position, current-frame collaborators, selection, etc.). Persist only
// what matters for re-opening the document: canvas chrome, current-tool
// preferences, and viewport. Same key set the previous non-Yjs
// NapkinEditor wrote to contentJsonb.
function pickPersistedAppState(raw: unknown): Record<string, unknown> {
  if (!raw || typeof raw !== "object") return {};
  const state = raw as Record<string, unknown>;
  const out: Record<string, unknown> = {};
  for (const key of PERSISTED_APP_STATE_KEYS) {
    if (key in state) out[key] = state[key];
  }
  return out;
}

const PERSISTED_APP_STATE_KEYS: readonly string[] = [
  "viewBackgroundColor",
  "gridSize",
  "gridModeEnabled",
  "theme",
  "currentItemStrokeColor",
  "currentItemBackgroundColor",
  "currentItemFillStyle",
  "currentItemStrokeWidth",
  "currentItemStrokeStyle",
  "currentItemRoughness",
  "currentItemOpacity",
  "currentItemFontFamily",
  "currentItemFontSize",
  "currentItemTextAlign",
  "currentItemStartArrowhead",
  "currentItemEndArrowhead",
  "scrollX",
  "scrollY",
  "zoom"
];

function yMapToPojo(map: Y.Map<unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  map.forEach((value, key) => {
    out[key] = value;
  });
  return out;
}
