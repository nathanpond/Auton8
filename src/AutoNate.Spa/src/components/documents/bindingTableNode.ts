import { Fragment } from "prosemirror-model";
import type { Node as PmNode, Schema } from "prosemirror-model";
import type { EditorView } from "prosemirror-view";
import type { Transaction } from "prosemirror-state";
import type {
  AqlTableResolvedValue,
  DocumentBindingDto
} from "@/api/documentBindings";

// Phase 10b: render aql-table bindings as real Word tables instead of a
// `{{binding:UUID}}` text placeholder + a "📊 N rows" decoration chip.
// The table is a first-class PM `table` node, so it renders inline,
// round-trips to OOXML natively, and serializes its values for export +
// RAG. Because docx-editor's schema has no block-level content-control
// node to WRAP the table (sdt is inline-only — see plan §9e spike), we
// can't tag the table itself with the binding id. Instead each bound
// table is preceded by a one-paragraph MARKER: a `field` node whose
// instruction encodes the binding id + the resolved-at timestamp, and
// whose displayText is the binding label (a visible caption). Refresh
// finds the marker, compares the timestamp, and replaces the following
// table when the data changed.

// Pipe-delimited so the binding id (no pipes) and the ISO timestamp
// (contains colons, but no pipes) parse cleanly. Distinct from the
// record-field marker (`AUTONATE_BINDING <uuid>`, space-delimited) so
// the two never collide.
const TABLE_MARKER_HEAD = "AUTONATE_TABLE_BINDING";

const BINDING_PATTERN =
  /\{\{binding:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\}\}/gi;

// Cell styling — kept in sync with markdownToPmNodes' table styling by
// convention (duplicated rather than imported to avoid coupling the
// binding path to the markdown converter's internals). Half-point gray
// border on all sides; light gray fill + bold on header cells.
const CELL_BORDER = { style: "single", size: 4, color: { rgb: "999999" } };
const CELL_BORDERS = {
  top: CELL_BORDER,
  bottom: CELL_BORDER,
  left: CELL_BORDER,
  right: CELL_BORDER
};
const HEADER_BG = "F2F2F2";

export function tableMarkerInstruction(
  bindingId: string,
  resolvedAtUtc: string | null
): string {
  return `${TABLE_MARKER_HEAD}|${bindingId}|${resolvedAtUtc ?? ""}`;
}

type ParsedTableMarker = { bindingId: string; resolvedAt: string };

export function parseTableMarkerInstruction(
  instruction: unknown
): ParsedTableMarker | null {
  if (typeof instruction !== "string") return null;
  const parts = instruction.split("|");
  if (parts.length < 2 || parts[0] !== TABLE_MARKER_HEAD) return null;
  const bindingId = parts[1]?.trim();
  if (!bindingId) return null;
  return { bindingId, resolvedAt: parts[2] ?? "" };
}

function decodeAqlValue(binding: DocumentBindingDto): AqlTableResolvedValue | null {
  if (!binding.lastResolvedValueJsonb) return null;
  try {
    return JSON.parse(binding.lastResolvedValueJsonb) as AqlTableResolvedValue;
  } catch {
    return null;
  }
}

function cellText(value: unknown): string {
  if (value === null || value === undefined) return "";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

// Build a styled PM table from a resolved aql-table value. Header row
// from columns, body rows from the row records keyed by column name.
// Returns null when the schema lacks table nodes (then the caller falls
// back to a plain caption paragraph so nothing is silently dropped).
export function buildAqlTableNode(
  schema: Schema,
  value: AqlTableResolvedValue
): PmNode | null {
  const tableType = schema.nodes.table;
  const rowType = schema.nodes.tableRow;
  const cellType = schema.nodes.tableCell;
  const headerType = schema.nodes.tableHeader ?? cellType;
  const paragraphType = schema.nodes.paragraph;
  if (!tableType || !rowType || !cellType || !paragraphType) return null;

  const makeCell = (
    type: typeof cellType,
    text: string,
    isHeader: boolean
  ): PmNode | null => {
    const marks = isHeader && schema.marks.bold ? [schema.marks.bold.create()] : undefined;
    const para = paragraphType.create(
      null,
      text.length > 0 ? schema.text(text, marks) : null
    );
    const attrs: Record<string, unknown> = { borders: CELL_BORDERS };
    if (isHeader) attrs.backgroundColor = HEADER_BG;
    return type.createAndFill(attrs, Fragment.from(para));
  };

  const rows: PmNode[] = [];

  // Header row.
  const headerCells: PmNode[] = [];
  for (const col of value.columns) {
    const c = makeCell(headerType, col.name, true);
    if (c) headerCells.push(c);
  }
  if (headerCells.length > 0) {
    rows.push(rowType.create({ isHeader: true }, Fragment.fromArray(headerCells)));
  }

  // Body rows.
  for (const row of value.rows) {
    const cells: PmNode[] = [];
    for (const col of value.columns) {
      const c = makeCell(cellType, cellText(row[col.name]), false);
      if (c) cells.push(c);
    }
    if (cells.length > 0) {
      rows.push(rowType.create(null, Fragment.fromArray(cells)));
    }
  }

  if (rows.length === 0) return null;
  return tableType.create(null, Fragment.fromArray(rows));
}

// Build the [markerParagraph, table] block pair for an aql-table binding.
// The marker paragraph holds a `field` node (caption = label) carrying
// the binding id + resolved timestamp. Falls back to a caption-only
// paragraph if the table can't be built (no schema table nodes / no
// resolved value yet).
export function buildAqlTableBlocks(
  schema: Schema,
  binding: DocumentBindingDto
): PmNode[] {
  const paragraphType = schema.nodes.paragraph;
  const fieldType = schema.nodes.field;
  if (!paragraphType || !fieldType) return [];

  const caption = binding.label ?? "Query results";
  const markerField = fieldType.create({
    fieldType: "unknown",
    instruction: tableMarkerInstruction(binding.id, binding.lastResolvedAtUtc),
    displayText: caption,
    fieldKind: "begin",
    fldLock: false,
    dirty: false
  });
  const markerPara = paragraphType.create(null, Fragment.from(markerField));

  const value = decodeAqlValue(binding);
  const table = value ? buildAqlTableNode(schema, value) : null;
  if (!table) {
    // No data yet — just the caption; the refresh pass will add the
    // table once the binding resolves.
    return [markerPara];
  }
  return [markerPara, table];
}

// Insert an aql-table binding at the cursor. Block content can't live
// inside a paragraph, so we insert the [marker, table] blocks AFTER the
// top-level block that contains the cursor.
export function insertAqlTableBinding(
  view: EditorView,
  binding: DocumentBindingDto,
  markDirectEdit: (tr: Transaction) => void
): void {
  const blocks = buildAqlTableBlocks(view.state.schema, binding);
  if (blocks.length === 0) return;
  const { $from } = view.state.selection;
  // Position just after the depth-1 (top-level) ancestor of the cursor.
  const insertPos = $from.after(1);
  const tr = view.state.tr.insert(insertPos, Fragment.fromArray(blocks));
  markDirectEdit(tr);
  view.dispatch(tr);
  view.focus();
}

// Migrate + refresh aql-table bindings in one pass. Returns true if it
// dispatched. Two responsibilities:
//   1. MIGRATE legacy inline `{{binding:UUID}}` text (whose binding is
//      aql-table) → delete the placeholder + insert [marker, table]
//      after the containing top-level block.
//   2. REFRESH existing marked tables whose binding's resolved-at
//      timestamp changed → replace the following table + bump the
//      marker's stored timestamp.
//
// All edits collected then applied high-position-first so earlier
// positions stay valid. Marked direct-edit + kept out of undo history.
export function syncAqlTableNodes(
  view: EditorView,
  bindings: DocumentBindingDto[],
  markDirectEdit: (tr: Transaction) => void,
  bindingsLoaded: boolean
): boolean {
  const aqlById = new Map<string, DocumentBindingDto>();
  for (const b of bindings) {
    if (b.kind === "aql-table") aqlById.set(b.id, b);
  }

  const { schema, doc } = view.state;
  if (!schema.nodes.table || !schema.nodes.field) return false;

  type Op =
    | { kind: "replaceTable"; from: number; to: number; node: PmNode }
    | { kind: "setMarker"; pos: number; attrs: Record<string, unknown> }
    | { kind: "remove"; from: number; to: number }
    | { kind: "migrate"; delFrom: number; delTo: number; insertAt: number; blocks: PmNode[] };
  const ops: Op[] = [];

  doc.forEach((block, blockPos) => {
    // (2) marker paragraph → its first child is a field with our table
    // instruction. The following sibling block should be the table.
    if (block.type.name === "paragraph" && block.firstChild?.type.name === "field") {
      const field = block.firstChild;
      const parsed = parseTableMarkerInstruction(field.attrs.instruction);
      if (parsed) {
        const binding = aqlById.get(parsed.bindingId);
        if (binding) {
          const wantTs = binding.lastResolvedAtUtc ?? "";
          if (parsed.resolvedAt !== wantTs) {
            const value = decodeAqlValue(binding);
            const newTable = value ? buildAqlTableNode(schema, value) : null;
            // The table sits right after this paragraph block.
            const afterPara = blockPos + block.nodeSize;
            const nextNode = afterPara < doc.content.size ? doc.nodeAt(afterPara) : null;
            if (newTable && nextNode?.type.name === "table") {
              ops.push({
                kind: "replaceTable",
                from: afterPara,
                to: afterPara + nextNode.nodeSize,
                node: newTable
              });
            }
            // Bump the marker's stored timestamp regardless (so we don't
            // re-attempt every sync). The field node is at blockPos+1
            // (inside the paragraph).
            ops.push({
              kind: "setMarker",
              pos: blockPos + 1,
              attrs: {
                ...field.attrs,
                instruction: tableMarkerInstruction(binding.id, binding.lastResolvedAtUtc)
              }
            });
          }
        } else if (bindingsLoaded) {
          // (3) binding gone — remove the marker paragraph + the table
          // that follows it (if any). Covers the side-panel delete case.
          const afterPara = blockPos + block.nodeSize;
          const nextNode = afterPara < doc.content.size ? doc.nodeAt(afterPara) : null;
          const to =
            nextNode?.type.name === "table"
              ? afterPara + nextNode.nodeSize
              : blockPos + block.nodeSize;
          ops.push({ kind: "remove", from: blockPos, to });
        }
      }
      return;
    }

    // (1) legacy inline placeholder migration — scan this block's text
    // for an aql-table placeholder.
    block.descendants((node, relPos) => {
      if (!node.isText) return true;
      const text = node.text ?? "";
      BINDING_PATTERN.lastIndex = 0;
      let m: RegExpExecArray | null;
      while ((m = BINDING_PATTERN.exec(text)) !== null) {
        const binding = aqlById.get(m[1]);
        if (!binding) continue;
        // Absolute positions: blockPos+1 is the block's content start;
        // relPos is relative to the block content.
        const from = blockPos + 1 + relPos + m.index;
        const to = from + m[0].length;
        ops.push({
          kind: "migrate",
          delFrom: from,
          delTo: to,
          insertAt: blockPos + block.nodeSize,
          blocks: buildAqlTableBlocks(schema, binding)
        });
      }
      return false;
    });
  });

  if (ops.length === 0) return false;

  // Order: apply by descending primary position so earlier offsets stay
  // valid. For migrate ops, the insert (higher) and delete (lower) are
  // handled within one op below.
  const primary = (op: Op): number =>
    op.kind === "replaceTable"
      ? op.from
      : op.kind === "setMarker"
        ? op.pos
        : op.kind === "remove"
          ? op.from
          : op.insertAt;
  ops.sort((a, b) => primary(b) - primary(a));

  let tr = view.state.tr;
  for (const op of ops) {
    if (op.kind === "replaceTable") {
      tr = tr.replaceWith(op.from, op.to, op.node);
    } else if (op.kind === "setMarker") {
      tr = tr.setNodeMarkup(op.pos, undefined, op.attrs);
    } else if (op.kind === "remove") {
      tr = tr.delete(op.from, op.to);
    } else {
      // migrate: insert blocks after the block first (higher pos), then
      // delete the placeholder text (lower pos) — both within this op so
      // their relative order is correct.
      tr = tr.insert(op.insertAt, Fragment.fromArray(op.blocks));
      tr = tr.delete(op.delFrom, op.delTo);
    }
  }
  markDirectEdit(tr);
  tr.setMeta("addToHistory", false);
  view.dispatch(tr);
  return true;
}
