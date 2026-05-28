import type { Node as PmNode, Schema } from "prosemirror-model";
import type { EditorView } from "prosemirror-view";
import type { Transaction } from "prosemirror-state";
import type {
  DocumentBindingDto,
  RecordFieldResolvedValue
} from "@/api/documentBindings";

// Phase 10a: render record-field bindings as docx-editor `field` nodes
// (Word's native field primitive) instead of `{{binding:UUID}}` text +
// a decoration chip. The field node renders the resolved value inline,
// carries the binding id in its `instruction` attr, and round-trips
// through OOXML as a real Word field — so exports + RAG serialization
// see the value, not the raw token, and the editor's paged renderer
// shows the value directly (no chip-beside-placeholder artifact).
//
// The instruction format is `AUTONATE_BINDING <uuid>`. Word field
// instructions are arbitrary text, so this survives a .docx round-trip.
// aql-table bindings stay on the text+decoration path until Phase 10b
// (no block-level content-control node exists in the schema to wrap a
// table — see plan §9e spike verdict).

const INSTRUCTION_PREFIX = "AUTONATE_BINDING ";

// Same placeholder pattern the decoration plugin scans for. Used by the
// migration pass to find legacy record-field placeholders to upgrade.
const BINDING_PATTERN =
  /\{\{binding:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\}\}/gi;

export function bindingInstruction(bindingId: string): string {
  return `${INSTRUCTION_PREFIX}${bindingId}`;
}

// Extract a binding id from a field node's instruction, or null if the
// instruction isn't one of ours.
export function bindingIdFromInstruction(
  instruction: unknown
): string | null {
  if (typeof instruction !== "string") return null;
  if (!instruction.startsWith(INSTRUCTION_PREFIX)) return null;
  const id = instruction.slice(INSTRUCTION_PREFIX.length).trim();
  return id.length > 0 ? id : null;
}

// Resolve the display text for a record-field binding. Falls back to a
// readable placeholder when the binding hasn't resolved yet (or the
// value can't be decoded) so the field is never visually empty.
export function recordFieldDisplayText(binding: DocumentBindingDto): string {
  if (binding.lastResolvedValueJsonb) {
    try {
      const v = JSON.parse(
        binding.lastResolvedValueJsonb
      ) as RecordFieldResolvedValue;
      if (typeof v.text === "string" && v.text.length > 0) return v.text;
      // Resolved but empty/missing/denied — surface the type so the
      // reader knows why it's blank rather than seeing nothing.
      if (v.type === "denied") return "(access denied)";
      if (v.type === "missing") return "(no value)";
      if (typeof v.text === "string") return v.text; // empty string ok
    } catch {
      /* fall through to placeholder */
    }
  }
  return binding.label ? `(${binding.label})` : "(unresolved binding)";
}

// Build a `field` node for a record-field binding. fieldType "unknown"
// renders as a generic field (verified in the spike); the binding id
// rides in `instruction`, the value in `displayText`.
export function buildRecordFieldNode(
  schema: Schema,
  bindingId: string,
  displayText: string
): PmNode {
  const fieldType = schema.nodes.field;
  if (!fieldType) {
    throw new Error("Editor schema has no 'field' node type.");
  }
  return fieldType.create({
    fieldType: "unknown",
    instruction: bindingInstruction(bindingId),
    displayText,
    fieldKind: "begin",
    fldLock: false,
    dirty: false
  });
}

// Unified migrate + refresh pass for record-field bindings. Idempotent —
// safe to call on mount (migration) and whenever the bindings list
// changes (refresh). Does two things in one transaction:
//
//   1. MIGRATE: replace any legacy `{{binding:UUID}}` text placeholder
//      whose binding is record-field with a field node carrying the
//      current resolved value.
//   2. REFRESH: update the displayText of any existing field node whose
//      binding's resolved value has changed.
//
// aql-table placeholders are left untouched (handled by the decoration
// plugin until Phase 10b). Returns true if it dispatched a transaction.
//
// All edits are collected then applied high-position-first so earlier
// positions stay valid without position mapping. The transaction is
// marked as a direct edit so it never becomes a tracked change, and is
// kept out of the undo history (it's a sync, not a user action).
export function syncRecordFieldNodes(
  view: EditorView,
  bindings: DocumentBindingDto[],
  markDirectEdit: (tr: Transaction) => void
): boolean {
  const recordFieldById = new Map<string, DocumentBindingDto>();
  for (const b of bindings) {
    if (b.kind === "record-field") recordFieldById.set(b.id, b);
  }
  if (recordFieldById.size === 0) return false;

  const { schema, doc } = view.state;
  if (!schema.nodes.field) return false;

  type Edit = { from: number; to: number; node: PmNode };
  const edits: Edit[] = [];

  doc.descendants((node, pos) => {
    // (2) existing field node — refresh stale displayText.
    if (node.type.name === "field") {
      const id = bindingIdFromInstruction(node.attrs.instruction);
      if (id) {
        const binding = recordFieldById.get(id);
        if (binding) {
          const want = recordFieldDisplayText(binding);
          if (node.attrs.displayText !== want) {
            edits.push({
              from: pos,
              to: pos + node.nodeSize,
              node: buildRecordFieldNode(schema, id, want)
            });
          }
        }
      }
      return false; // leaf
    }

    // (1) legacy text placeholder — migrate to a field node.
    if (node.isText) {
      const text = node.text ?? "";
      BINDING_PATTERN.lastIndex = 0;
      let match: RegExpExecArray | null;
      while ((match = BINDING_PATTERN.exec(text)) !== null) {
        const id = match[1];
        const binding = recordFieldById.get(id);
        if (!binding) continue; // not record-field (or unknown) — leave it
        const from = pos + match.index;
        const to = from + match[0].length;
        edits.push({
          from,
          to,
          node: buildRecordFieldNode(schema, id, recordFieldDisplayText(binding))
        });
      }
      return false;
    }

    return true;
  });

  if (edits.length === 0) return false;

  // Apply high-position-first so each replace doesn't shift the
  // positions of edits we haven't applied yet.
  edits.sort((a, b) => b.from - a.from);
  let tr = view.state.tr;
  for (const e of edits) {
    tr = tr.replaceWith(e.from, e.to, e.node);
  }
  markDirectEdit(tr);
  // Keep the sync out of undo history — it's derived state, not an edit
  // the user should be able to ctrl-Z into a half-migrated mess.
  tr.setMeta("addToHistory", false);
  view.dispatch(tr);
  return true;
}
