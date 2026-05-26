import { Plugin, PluginKey } from "prosemirror-state";
import { Decoration, DecorationSet } from "prosemirror-view";
import type { Node as PmNode } from "prosemirror-model";
import type {
  AqlTableResolvedValue,
  DocumentBindingDto,
  RecordFieldResolvedValue
} from "@/api/documentBindings";
import {
  getBindingFromRegistry,
  subscribeToBindingsRegistry
} from "./bindingsRegistry";

// In-document rendering plugin for binding placeholders.
//
// Convention: an inserted binding leaves the literal text
// `{{binding:UUID}}` somewhere in the body. This plugin scans text
// nodes for that pattern and adds a widget decoration at the start of
// each match that renders the binding's current resolved value pulled
// from the registry.
//
// Phase 5 v1 limitation worth knowing: we ALSO tried hiding the literal
// placeholder text with an inline `display: none` decoration in the
// same pass, but docx-editor's page-layout renderer bypasses
// ProseMirror inline decorations entirely — it materializes a parallel
// page DOM from the doc model directly. The hide decoration creates an
// empty positioned div that floats off-screen, and the placeholder
// text stays visible alongside the chip. Hiding requires either (a)
// switching to docx-editor's `renderOverlay`/PluginHost API to
// position a chip OVER the text (deferred — needs wrapping the editor
// in PluginHost), or (b) replacing the placeholder text with a
// non-printing marker after insert (deferred — needs an end-to-end
// rethink of how bindings reference body positions). For v1, the
// chip + visible placeholder side-by-side is the accepted shape; the
// side panel is the canonical binding-management UI.
//
// Updates flow in two paths:
//   • Editor state change (user edits, Yjs sync) → ProseMirror calls
//     `decorations(state)` → we rebuild and the new pass picks up any
//     new placeholders.
//   • Registry change (React Query refetch / refresh) → we dispatch a
//     no-op transaction so ProseMirror re-runs `decorations(state)` →
//     widget DOM gets rebuilt with the fresh value.

const PLUGIN_KEY = new PluginKey<DecorationSet>("docx-bindings");
const BINDING_PATTERN = /\{\{binding:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\}\}/gi;

export function createBindingsPlugin(): Plugin {
  let unsubscribe: (() => void) | null = null;
  return new Plugin<DecorationSet>({
    key: PLUGIN_KEY,
    state: {
      init: (_config, state) => buildDecorations(state.doc),
      apply: (tr, oldSet, _oldState, newState) => {
        // Manual rebuild triggered by registry change.
        const refresh = tr.getMeta(PLUGIN_KEY);
        if (refresh === "refresh") return buildDecorations(newState.doc);
        // Doc changed → rebuild.
        if (tr.docChanged) return buildDecorations(newState.doc);
        return oldSet;
      }
    },
    props: {
      decorations(state) {
        return PLUGIN_KEY.getState(state) ?? DecorationSet.empty;
      }
    },
    view(view) {
      // Subscribe to registry updates on mount; dispatch a no-op
      // transaction with our refresh meta on each notification so the
      // apply hook above rebuilds the decoration set.
      unsubscribe = subscribeToBindingsRegistry(() => {
        const tr = view.state.tr.setMeta(PLUGIN_KEY, "refresh");
        view.dispatch(tr);
      });
      return {
        destroy() {
          unsubscribe?.();
          unsubscribe = null;
        }
      };
    }
  });
}

function buildDecorations(doc: PmNode): DecorationSet {
  const decos: Decoration[] = [];
  doc.descendants((node, pos) => {
    if (!node.isText) return true;
    const text = node.text ?? "";
    // Reset lastIndex so the global regex doesn't carry state across
    // text nodes (a sharp edge of /g RegExps).
    BINDING_PATTERN.lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = BINDING_PATTERN.exec(text)) !== null) {
      const bindingId = match[1];
      const from = pos + match.index;
      // Render the value widget at the placeholder's start position.
      // (We don't try to hide the literal placeholder text — docx-editor's
      // page renderer bypasses inline decorations. See the file-header
      // comment for the Phase 5 v1 limitation.)
      // The widget is created lazily by ProseMirror — we pass a
      // factory so DOM creation runs only when the decoration is
      // actually mounted.
      decos.push(
        Decoration.widget(from, () => renderBindingWidget(bindingId), {
          // `side: 0` keeps the widget inline (between adjacent text);
          // `key` lets ProseMirror reuse the DOM node across rebuilds.
          key: `binding-${bindingId}`,
          side: 0
        })
      );
    }
    return false; // text nodes have no children
  });
  return DecorationSet.create(doc, decos);
}

function renderBindingWidget(bindingId: string): HTMLElement {
  const span = document.createElement("span");
  span.classList.add("doc-binding");
  span.setAttribute("data-binding-id", bindingId);

  const binding = getBindingFromRegistry(bindingId);
  if (!binding) {
    // Placeholder existed in body but no metadata yet — either still
    // loading or the binding was deleted. Show a neutral chip.
    span.classList.add("doc-binding-missing");
    span.textContent = "{binding not loaded}";
    return span;
  }

  span.classList.add(`doc-binding-${binding.kind}`);
  span.setAttribute(
    "title",
    [
      binding.label ?? "(unlabelled)",
      binding.lastResolvedAtUtc
        ? `Refreshed ${new Date(binding.lastResolvedAtUtc).toLocaleString()}`
        : "Not yet resolved"
    ].join("\n")
  );

  const resolved = decodeResolved(binding);
  if (!resolved) {
    span.textContent = "(not yet resolved)";
    return span;
  }

  if (binding.kind === "record-field") {
    span.textContent = (resolved as RecordFieldResolvedValue).text;
  } else if (binding.kind === "aql-table") {
    // Inline rendering of a full table is too dense — render a chip
    // with row/col counts; the side panel shows the full data preview.
    // A future polish phase can render a real inline table here.
    const v = resolved as AqlTableResolvedValue;
    span.textContent = `📊 ${v.totalCount} rows × ${v.columns.length} cols${v.truncated ? " (truncated)" : ""}`;
  } else {
    span.textContent = "(unknown binding kind)";
  }

  return span;
}

function decodeResolved(
  binding: DocumentBindingDto
): RecordFieldResolvedValue | AqlTableResolvedValue | null {
  if (!binding.lastResolvedValueJsonb) return null;
  try {
    return JSON.parse(binding.lastResolvedValueJsonb);
  } catch {
    return null;
  }
}

/** Build the placeholder text the editor inserts when a binding is added. */
export function bindingPlaceholderText(bindingId: string): string {
  return `{{binding:${bindingId}}}`;
}
