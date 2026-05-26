import type { DocumentBindingDto } from "@/api/documentBindings";

// Module-level registry bridging React Query state into the ProseMirror
// decoration plugin. The plugin can't subscribe to React state directly —
// it's imperative code that runs inside an EditorView. So the React
// component (DocxDocumentEditor) pushes the latest bindings into this
// registry on every change, and the plugin reads them at decoration
// build time.
//
// Keyed by binding id so the plugin can do O(1) lookups when scanning
// document text for `{{binding:UUID}}` markers.
//
// This is a deliberate Phase 5 v1 simplification — the "proper" way is a
// ProseMirror plugin meta-transaction, but that requires more wiring and
// the registry approach gets us to working in-doc rendering with less
// risk. We can refactor in a polish pass if cross-tab / cross-view sync
// ever surfaces an issue.

type Listener = () => void;

const bindingsById: Map<string, DocumentBindingDto> = new Map();
const listeners: Set<Listener> = new Set();

/** Replace the registry's contents with the latest list from React Query. */
export function updateBindingsRegistry(
  documentId: string,
  bindings: DocumentBindingDto[]
): void {
  // We currently support one open editor per browser tab; scope by
  // documentId in the key so we don't accidentally read another doc's
  // bindings if two editors ever co-exist in the same page.
  // Strip + re-seed only the entries for THIS document.
  for (const [key, binding] of bindingsById.entries()) {
    if (binding.documentId === documentId) {
      bindingsById.delete(key);
    }
  }
  for (const binding of bindings) {
    bindingsById.set(binding.id, binding);
  }
  for (const fn of listeners) fn();
}

/** Imperative lookup by id — used by the ProseMirror decoration plugin. */
export function getBindingFromRegistry(
  bindingId: string
): DocumentBindingDto | undefined {
  return bindingsById.get(bindingId);
}

/** Subscribe to registry changes. Returns an unsubscribe fn. */
export function subscribeToBindingsRegistry(fn: Listener): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
