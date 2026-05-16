import { useEffect, useMemo } from "react";
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
  const { doc, excalidrawAPI } = args;

  // Stable references to the shared containers — fetched once per Y.Doc.
  const elementsArray = useMemo(
    () => doc.getArray<Y.Map<unknown>>("elements"),
    [doc]
  );
  const appStateMap = useMemo(() => doc.getMap<unknown>("appState"), [doc]);

  // Captured at first hook-render. We deliberately do NOT recompute when
  // elementsArray/appStateMap change — Excalidraw only reads `initialData`
  // at mount time. Post-mount updates flow through the observer below.
  // eslint-disable-next-line react-hooks/exhaustive-deps
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
