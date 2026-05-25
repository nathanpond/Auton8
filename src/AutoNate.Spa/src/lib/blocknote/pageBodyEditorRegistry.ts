import type { BlockNoteEditor } from "@blocknote/core";

// Reverse index of page-body BlockNote editors, keyed by pageId. Lets
// non-React code (the chatbot's page-context provider, mainly) look up the
// live editor instance for a given page so it can read the current document
// — including unsaved edits — without going through the Yjs persistence
// layer or hitting the REST API.
//
// Populated by YjsEditor at mount and cleared at unmount. There's typically
// only ever one entry (the page the user is viewing); the map is here so the
// registry survives switches between pages without a global singleton race.
const editorsByPageId = new Map<string, BlockNoteEditor<any, any, any>>();

export function registerPageBodyEditor(
  pageId: string,
  editor: BlockNoteEditor<any, any, any>
): void {
  editorsByPageId.set(pageId, editor);
}

export function unregisterPageBodyEditor(pageId: string, editor: BlockNoteEditor<any, any, any>): void {
  // Guard against a late unmount stomping a newer registration for the same
  // pageId (can happen during fast tab switches if React tears down the old
  // editor after the new one mounted).
  if (editorsByPageId.get(pageId) === editor) {
    editorsByPageId.delete(pageId);
  }
}

export function getPageBodyEditor(pageId: string): BlockNoteEditor<any, any, any> | null {
  return editorsByPageId.get(pageId) ?? null;
}
