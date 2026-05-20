import { useSyncExternalStore } from "react";
import type { BlockNoteEditor } from "@blocknote/core";

// Per-editor signal store. Each BlockNote editor instance carries an
// independent `{ pageId, editable }` state that the noteEmbed block's
// render function reads. Keyed by editor reference because we can't
// rely on React context propagating through Tiptap's ReactNodeViewRenderer
// — depending on Tiptap's render strategy at any given moment, the
// NodeView component can mount in a separate React root that doesn't
// inherit the parent tree's contexts.
//
// The render function receives the editor instance via its props, so it
// always has the right key to look up state — no extra plumbing through
// context. WeakMap so editor garbage collection cleans up entries
// automatically.
type EditorKey = BlockNoteEditor<any, any, any>;

export interface PageEditorSignalState {
  pageId: string;
  editable: boolean;
}

const stateByEditor = new WeakMap<EditorKey, PageEditorSignalState>();
const subscribersByEditor = new WeakMap<EditorKey, Set<() => void>>();

// Stable null sentinel — returned when an editor isn't registered yet
// (race between mount of the BlockNote editor and the YjsEditor effect
// that calls setPageEditorSignal). useSyncExternalStore requires the
// snapshot getter to return a stable reference when no change has
// occurred, otherwise it loops; reusing this constant satisfies that.
const NULL_STATE = null as PageEditorSignalState | null;

export function setPageEditorSignal(
  editor: EditorKey,
  state: PageEditorSignalState
): void {
  const prev = stateByEditor.get(editor);
  if (prev && prev.pageId === state.pageId && prev.editable === state.editable) {
    // No change — skip notifying subscribers. Avoids spurious embed
    // re-renders on every page re-render.
    return;
  }
  stateByEditor.set(editor, state);
  const subs = subscribersByEditor.get(editor);
  if (subs) for (const cb of subs) cb();
}

export function clearPageEditorSignal(editor: EditorKey): void {
  stateByEditor.delete(editor);
  const subs = subscribersByEditor.get(editor);
  if (subs) for (const cb of subs) cb();
}

export function usePageEditorSignal(
  editor: EditorKey
): PageEditorSignalState | null {
  return useSyncExternalStore(
    (cb) => {
      let subs = subscribersByEditor.get(editor);
      if (!subs) {
        subs = new Set();
        subscribersByEditor.set(editor, subs);
      }
      subs.add(cb);
      return () => {
        subs!.delete(cb);
      };
    },
    () => stateByEditor.get(editor) ?? NULL_STATE,
    () => stateByEditor.get(editor) ?? NULL_STATE
  );
}
