import { marked } from "marked";
import { Fragment, Slice } from "prosemirror-model";
import type {
  Mark,
  MarkType,
  Node as PmNode,
  NodeType,
  Schema
} from "prosemirror-model";

// Convert chatbot-supplied markdown into a ProseMirror Slice the editor
// can insert. docx-editor uses a single `paragraph` node type with a
// `styleId` attribute (Word's model — Heading1, Heading2, ListBullet,
// ListNumber, Quote, …) rather than separate heading/list/blockquote
// nodes. Word's apply-style semantics are "copy the style's rPr to the
// run at apply time, AND tag the paragraph with the styleId" — see
// docx-editor's `setParagraphStyle` ref method, which resolves the
// document's style table and stamps `runFormatting` as marks before
// dispatch. Setting just the `styleId` attribute on a paragraph WITHOUT
// stamping the resolved rPr leaves the runs at body defaults — the
// rendering pipeline doesn't second-guess explicit runs by re-resolving
// styles at render time. So this converter takes the document's style
// table as input and copies the resolved rPr (bold, italic, fontSize,
// fontFamily, color, etc.) onto each text run as marks, exactly the
// same shape `commands.applyStyle` would produce.
//
// Mapping table (matches docx-editor's built-in Word styleIds; styles
// the host docx may not have defined silently fall back to the body
// default — equivalent to "normal" in Word):
//
//   <h1>          → paragraph styleId=Heading1
//   <h2>          → paragraph styleId=Heading2
//   <h3>          → paragraph styleId=Heading3
//   <h4>          → paragraph styleId=Heading4
//   <h5>          → paragraph styleId=Heading5
//   <h6>          → paragraph styleId=Heading6
//   <p>           → paragraph (no styleId — body default)
//   <ul><li>      → paragraph styleId=ListBullet
//   <ol><li>      → paragraph styleId=ListNumber
//   <blockquote>  → paragraph styleId=Quote
//   <pre><code>   → paragraph styleId=Quote (best-available approximation)
//
// Inline mark mapping:
//   <strong>/<b>  → bold
//   <em>/<i>      → italic
//   <s>/<del>     → strikethrough
//   <code>        → bold (best-available — most schemas lack inline code)
//   <a href>      → link mark if the schema has one, otherwise plain text
//
// Anything else collapses to its text content under the active mark stack.
// This keeps the converter resilient to markdown we haven't planned for —
// the worst case is "unformatted prose", never an exception.

marked.setOptions({ gfm: true, breaks: false });

const HEADING_STYLE_BY_TAG: Record<string, string> = {
  H1: "Heading1",
  H2: "Heading2",
  H3: "Heading3",
  H4: "Heading4",
  H5: "Heading5",
  H6: "Heading6"
};

export function markdownToProseMirrorSlice(
  markdown: string,
  schema: Schema,
  styleTable?: StyleTable | null
): Slice {
  const html = marked.parse(markdown, { async: false }) as string;
  const doc = new DOMParser().parseFromString(
    `<div>${html}</div>`,
    "text/html"
  );
  const root = doc.body.firstElementChild;
  if (!root) return Slice.empty;

  const paragraphType = schema.nodes.paragraph;
  if (!paragraphType) {
    // No 'paragraph' in the schema — abort cleanly, the caller's
    // insertion would fail anyway.
    return Slice.empty;
  }

  const ctx: BuildCtx = {
    schema,
    paragraphType,
    tableType: schema.nodes.table ?? null,
    tableRowType: schema.nodes.tableRow ?? null,
    tableCellType: schema.nodes.tableCell ?? null,
    tableHeaderType: schema.nodes.tableHeader ?? null,
    bold: schema.marks.bold ?? schema.marks.strong ?? null,
    italic: schema.marks.italic ?? schema.marks.em ?? null,
    underline: schema.marks.underline ?? null,
    strike: schema.marks.strike ?? schema.marks.strikethrough ?? null,
    // docx-editor's link mark is named `hyperlink`, with attrs
    // { href, tooltip?, rId? }. Fall back to `link` for other schemas.
    link: schema.marks.hyperlink ?? schema.marks.link ?? null,
    fontSize: schema.marks.fontSize ?? null,
    fontFamily: schema.marks.fontFamily ?? null,
    textColor: schema.marks.textColor ?? null,
    styleTable: styleTable ?? null
  };

  const blocks: PmNode[] = [];
  for (const child of Array.from(root.children)) {
    appendBlocks(child, blocks, ctx, /* listKind */ null);
  }

  if (blocks.length === 0) return Slice.empty;
  // openStart/openEnd = 0 → the slice is a clean sequence of full
  // paragraphs. Insertion at any position splits the cursor's
  // paragraph cleanly without merging.
  return new Slice(Fragment.fromArray(blocks), 0, 0);
}

type BuildCtx = {
  schema: Schema;
  paragraphType: NodeType;
  tableType: NodeType | null;
  tableRowType: NodeType | null;
  tableCellType: NodeType | null;
  tableHeaderType: NodeType | null;
  bold: MarkType | null;
  italic: MarkType | null;
  underline: MarkType | null;
  strike: MarkType | null;
  link: MarkType | null;
  fontSize: MarkType | null;
  fontFamily: MarkType | null;
  textColor: MarkType | null;
  styleTable: StyleTable | null;
};

// Bullet marker for `<ul><li>` items and numbered markers for `<ol><li>`
// items. We prepend the marker to the paragraph text (rather than relying
// on Word's numPr / numbering definitions) because empty documents
// usually don't carry a numbering definition in their package — without
// numPr-binding the layout engine has no way to compute the marker text,
// so the paragraphs visually collapse or overlap. Prepending into the
// run gives reliable rendering without the numPr scaffolding.
const BULLET_MARKER = "• ";
const LIST_INDENT_TWIPS = 360; // ~0.25 inch — matches Word's default level-1 indent

// Resolve a styleId to the run-formatting marks that Word would apply
// when you do "Apply Style → Heading 1" on a selection. Walks the
// `basedOn` chain so style inheritance (e.g. Heading1 basedOn Normal)
// composes correctly — Word's runtime does the same: a child style's
// rPr OVERRIDES individual fields from the parent's rPr, but doesn't
// erase them. Returns an empty array if no matching style entry exists
// — falling back to whatever the editor's docDefaults would apply.
function resolveStyleRunMarks(
  styleId: string,
  ctx: BuildCtx
): Mark[] {
  if (!ctx.styleTable?.styles) return [];
  // Walk basedOn chain from root (oldest ancestor) to leaf so leaf
  // values override ancestor values when we accumulate.
  const chain: StyleTableEntry[] = [];
  let current: string | undefined = styleId;
  const guard = new Set<string>();
  while (current && !guard.has(current)) {
    guard.add(current);
    const entry = ctx.styleTable.styles.find((s) => s.styleId === current);
    if (!entry) break;
    chain.unshift(entry); // root first
    current = entry.basedOn;
  }
  if (chain.length === 0) return [];

  // Accumulate rPr fields. Later entries (leaf descendants) win.
  const rPr: NonNullable<StyleTableEntry["rPr"]> = {};
  for (const entry of chain) {
    if (!entry.rPr) continue;
    Object.assign(rPr, entry.rPr);
  }

  const marks: Mark[] = [];
  if (rPr.bold && ctx.bold) marks.push(ctx.bold.create());
  if (rPr.italic && ctx.italic) marks.push(ctx.italic.create());
  if (rPr.strike && ctx.strike) marks.push(ctx.strike.create());
  if (rPr.underline && ctx.underline) {
    marks.push(ctx.underline.create({ style: rPr.underline.style ?? "single" }));
  }
  if (rPr.fontSize != null && ctx.fontSize) {
    marks.push(ctx.fontSize.create({ size: rPr.fontSize }));
  }
  if (rPr.fontFamily && ctx.fontFamily) {
    marks.push(
      ctx.fontFamily.create({
        ascii: rPr.fontFamily.ascii ?? null,
        hAnsi: rPr.fontFamily.hAnsi ?? rPr.fontFamily.ascii ?? null
      })
    );
  }
  if (rPr.color && ctx.textColor && (rPr.color.rgb || rPr.color.themeColor)) {
    marks.push(
      ctx.textColor.create({
        rgb: rPr.color.rgb ?? null,
        themeColor: rPr.color.themeColor ?? null
      })
    );
  }
  return marks;
}

// Subset of the style fields we read from the document's style table.
// Mirrors `Style` from @eigenpal/docx-editor-core but only the bits
// we care about — see ECMA-376 §17.7.4 for the full surface. We accept
// a duck-typed value so the converter doesn't depend on the editor
// package's type identity (helps with isolated unit tests too).
type StyleTableEntry = {
  styleId: string;
  basedOn?: string;
  rPr?: {
    bold?: boolean;
    italic?: boolean;
    underline?: { style?: string };
    strike?: boolean;
    fontSize?: number; // half-points (Word w:sz convention)
    fontFamily?: { ascii?: string; hAnsi?: string };
    color?: { rgb?: string; themeColor?: string };
  };
};

export type StyleTable = {
  styles?: StyleTableEntry[];
};

// Walk one block-level HTML element and append the resulting paragraphs
// to `out`. `listKind` tracks whether we're inside an active <ul> / <ol>
// so nested <li> emit the matching styleId.
function appendBlocks(
  el: Element,
  out: PmNode[],
  ctx: BuildCtx,
  listKind: "bullet" | "number" | null
): void {
  const tag = el.tagName.toUpperCase();

  if (HEADING_STYLE_BY_TAG[tag]) {
    // makeParagraph resolves the styleId to runFormatting marks via
    // resolveStyleRunMarks and applies them to every text run —
    // mirroring `commands.applyStyle`'s "stamp the resolved style"
    // semantics. The styleId is preserved on the paragraph so
    // future-edited-styles round-trip cleanly.
    out.push(makeParagraph(el, ctx, [], HEADING_STYLE_BY_TAG[tag]));
    return;
  }
  if (tag === "P") {
    out.push(makeParagraph(el, ctx, [], undefined));
    return;
  }
  if (tag === "UL") {
    let i = 0;
    for (const li of Array.from(el.children)) {
      if (li.tagName !== "LI") continue;
      appendListItem(li, out, ctx, { kind: "bullet", index: i++ });
    }
    return;
  }
  if (tag === "OL") {
    // <ol start="N"> sets the first ordinal. marked respects it; we
    // mirror that so `5. Item` continues from 5 instead of restarting.
    const startAttr = el.getAttribute("start");
    let n = startAttr ? parseInt(startAttr, 10) : 1;
    if (!Number.isFinite(n) || n < 1) n = 1;
    for (const li of Array.from(el.children)) {
      if (li.tagName !== "LI") continue;
      appendListItem(li, out, ctx, { kind: "number", index: n++ });
    }
    return;
  }
  if (tag === "LI") {
    appendListItem(el, out, ctx, { kind: listKind ?? "bullet", index: 1 });
    return;
  }
  if (tag === "BLOCKQUOTE") {
    // Each <p> inside the blockquote becomes a Quote-styled paragraph.
    // Bare text content (no nested <p>) is wrapped into one.
    let madeChildBlock = false;
    for (const child of Array.from(el.children)) {
      if (child.tagName === "P") {
        out.push(makeParagraph(child, ctx, [], "Quote"));
        madeChildBlock = true;
      } else {
        appendBlocks(child, out, ctx, listKind);
      }
    }
    if (!madeChildBlock && el.textContent?.trim()) {
      out.push(makeParagraph(el, ctx, [], "Quote"));
    }
    return;
  }
  if (tag === "PRE") {
    // Treat code blocks like a Quote-styled paragraph — best available
    // approximation when the schema lacks a code_block node type.
    out.push(makeParagraph(el, ctx, [], "Quote"));
    return;
  }
  if (tag === "HR") {
    // Horizontal rule has no clean paragraph-style mapping; emit an
    // empty paragraph so the visual break is at least preserved.
    out.push(ctx.paragraphType.create());
    return;
  }
  if (tag === "TABLE") {
    const tableNode = buildTableNode(el, ctx);
    if (tableNode) {
      out.push(tableNode);
    } else {
      // Schema lacks table support — fall back to flat paragraphs so
      // the content isn't lost. One paragraph per row, cells joined
      // with " | ".
      for (const row of Array.from(el.querySelectorAll("tr"))) {
        const cells = Array.from(row.querySelectorAll("th, td")).map(
          (c) => c.textContent?.trim() ?? ""
        );
        const text = cells.join(" | ");
        out.push(
          ctx.paragraphType.create(null, text ? ctx.schema.text(text) : null)
        );
      }
    }
    return;
  }

  // Anything else: walk children if they're block-like, otherwise wrap
  // the text into a default paragraph.
  if (el.children.length > 0) {
    for (const c of Array.from(el.children)) appendBlocks(c, out, ctx, listKind);
  } else {
    const text = el.textContent ?? "";
    if (text.trim()) {
      out.push(ctx.paragraphType.create(null, ctx.schema.text(text)));
    }
  }
}

function appendListItem(
  li: Element,
  out: PmNode[],
  ctx: BuildCtx,
  list: { kind: "bullet" | "number"; index: number }
): void {
  // Separate the <li>'s inline content from any nested lists. The
  // inline content (everything BEFORE the first nested ul/ol) becomes
  // this list item's paragraph; nested lists recurse independently.
  const inlineHost = li.ownerDocument.createElement("span");
  const nestedBlocks: Element[] = [];
  for (const child of Array.from(li.childNodes)) {
    if (
      child.nodeType === Node.ELEMENT_NODE &&
      ((child as Element).tagName === "UL" || (child as Element).tagName === "OL")
    ) {
      nestedBlocks.push(child as Element);
    } else {
      inlineHost.appendChild(child.cloneNode(true));
    }
  }

  // Prepend the marker into the inline content. A leading <span> with
  // the marker text reads through `collectInline` like any other
  // run, picking up the active mark stack (none, by default) — so
  // bullets/numbers don't bleed bold/italic from later text inside
  // the item.
  const marker = list.kind === "bullet" ? BULLET_MARKER : `${list.index}. `;
  const markerSpan = li.ownerDocument.createElement("span");
  markerSpan.appendChild(li.ownerDocument.createTextNode(marker));
  inlineHost.insertBefore(markerSpan, inlineHost.firstChild);

  // No styleId — the doc's style table typically doesn't define
  // ListBullet/ListNumber, and an un-resolved styleId combined with
  // a missing numPr can confuse the layout engine into stacking
  // paragraphs at the same y-coordinate. Plain paragraphs with a
  // hard-indent attribute lay out cleanly.
  const paragraph = ctx.paragraphType.create(
    { indentLeft: LIST_INDENT_TWIPS },
    Fragment.fromArray(collectInline(inlineHost, ctx, []))
  );
  out.push(paragraph);

  for (const nested of nestedBlocks) {
    appendBlocks(nested, out, ctx, null);
  }
}

// Build a paragraph node from an element whose CHILD nodes are
// inline-only. `styleId` optional — undefined means body default.
// When a styleId is provided, its resolved run formatting from the
// document's style table is auto-applied to every text run via marks
// (so headings actually render bold + bigger, lists pick up their list
// font, etc.). `extraMarks` lets callers add additional active marks
// on top of the style-derived ones (currently unused but kept for
// future inline-from-style cases).
function makeParagraph(
  el: Element,
  ctx: BuildCtx,
  extraMarks: readonly Mark[],
  styleId: string | undefined
): PmNode {
  const styleMarks = styleId ? resolveStyleRunMarks(styleId, ctx) : [];
  const combinedActive = [...styleMarks, ...extraMarks];
  const inline = collectInline(el, ctx, combinedActive);
  const attrs: Record<string, unknown> = {};
  if (styleId) attrs.styleId = styleId;
  return ctx.paragraphType.create(
    Object.keys(attrs).length > 0 ? attrs : null,
    inline.length > 0 ? Fragment.fromArray(inline) : null
  );
}

// Recursive inline walker. Returns a flat list of text nodes with the
// appropriate marks applied. Nested inline elements (e.g. <strong><em>X)
// stack marks correctly.
function collectInline(
  el: Element,
  ctx: BuildCtx,
  activeMarks: readonly Mark[]
): PmNode[] {
  const out: PmNode[] = [];
  for (const child of Array.from(el.childNodes)) {
    if (child.nodeType === Node.TEXT_NODE) {
      const text = child.textContent ?? "";
      if (text.length === 0) continue;
      out.push(ctx.schema.text(text, activeMarks.length > 0 ? [...activeMarks] : undefined));
      continue;
    }
    if (child.nodeType !== Node.ELEMENT_NODE) continue;
    const c = child as Element;
    const tag = c.tagName.toUpperCase();
    let nextMarks = activeMarks;

    if (tag === "STRONG" || tag === "B") {
      if (ctx.bold) nextMarks = pushMark(nextMarks, ctx.bold.create());
    } else if (tag === "EM" || tag === "I") {
      if (ctx.italic) nextMarks = pushMark(nextMarks, ctx.italic.create());
    } else if (tag === "S" || tag === "DEL" || tag === "STRIKE") {
      if (ctx.strike) nextMarks = pushMark(nextMarks, ctx.strike.create());
    } else if (tag === "CODE") {
      // No inline code mark in the schema — fall back to bold so it
      // still stands out visually.
      if (ctx.bold) nextMarks = pushMark(nextMarks, ctx.bold.create());
    } else if (tag === "A") {
      const href = c.getAttribute("href");
      if (ctx.link && href) {
        nextMarks = pushMark(nextMarks, ctx.link.create({ href }));
      }
    } else if (tag === "BR") {
      // Hard line break inside a paragraph — emit a literal newline,
      // which docx-editor renders as a `<w:br/>` style soft return.
      out.push(ctx.schema.text("\n", activeMarks.length > 0 ? [...activeMarks] : undefined));
      continue;
    }

    out.push(...collectInline(c, ctx, nextMarks));
  }
  return out;
}

function pushMark(marks: readonly Mark[], add: Mark): readonly Mark[] {
  for (const existing of marks) {
    if (existing.type === add.type) return marks;
  }
  return [...marks, add];
}

// Default cell border — half-point single line, neutral gray. Applied
// to every side of every cell + header. `size` is in eighths of a point
// (Word's `w:sz` wire format); `color.rgb` is hex without `#`.
const DEFAULT_CELL_BORDER = {
  style: "single",
  size: 4, // 0.5pt
  color: { rgb: "999999" }
};
const DEFAULT_CELL_BORDERS = {
  top: DEFAULT_CELL_BORDER,
  bottom: DEFAULT_CELL_BORDER,
  left: DEFAULT_CELL_BORDER,
  right: DEFAULT_CELL_BORDER
};
// Light gray header fill — visually pops the title row from the body.
// Matches the tint Word's default "Plain Table 1" style uses.
const HEADER_CELL_BACKGROUND = "F2F2F2";

// Build a real PM `table` node from an HTML <table>. Returns null if
// the schema doesn't provide table-family node types (older / stripped
// schemas), so the caller can fall back to flat paragraphs without
// losing the cells' text content. GFM tables (from marked) always
// produce a single header row inside <thead> + body rows inside
// <tbody>; we honor that structure by emitting `tableHeader` cells in
// the first row and `tableCell` for the rest. Each cell gets a default
// half-point gray border on all four sides; header cells additionally
// get a light gray fill and their text runs are bold.
function buildTableNode(el: Element, ctx: BuildCtx): PmNode | null {
  if (!ctx.tableType || !ctx.tableRowType || !ctx.tableCellType) return null;
  const headerType = ctx.tableHeaderType ?? ctx.tableCellType;

  // Collect rows in document order, preserving thead-then-tbody. A
  // single querySelectorAll("tr") does that naturally since the DOM
  // walks tree order. Tag each row with whether it lives inside
  // <thead> so we know which cell node type to use even when an HTML
  // table puts <th> in a body row.
  type RowSpec = { row: Element; isHeader: boolean };
  const rows: RowSpec[] = [];
  for (const tr of Array.from(el.querySelectorAll("tr"))) {
    const inThead = tr.parentElement?.tagName === "THEAD";
    rows.push({ row: tr, isHeader: inThead });
  }
  if (rows.length === 0) return null;

  const rowNodes: PmNode[] = [];
  for (const { row, isHeader } of rows) {
    const cellEls = Array.from(row.querySelectorAll("th, td"));
    if (cellEls.length === 0) continue;
    const cellNodes: PmNode[] = [];
    for (const cellEl of cellEls) {
      // Each cell's content must be (paragraph | table)+. We render
      // the cell's inline content as a single paragraph; nested
      // tables / lists inside a table cell are unusual in markdown
      // but if they appear, fall through to the standard block
      // walker.
      const paragraphsInCell: PmNode[] = [];
      // Walk the cell's children: text/inline => part of a single
      // paragraph; block children => standalone blocks.
      const inlineHost = el.ownerDocument.createElement("span");
      const blockChildren: Element[] = [];
      for (const child of Array.from(cellEl.childNodes)) {
        if (child.nodeType === Node.ELEMENT_NODE) {
          const tagName = (child as Element).tagName;
          if (tagName === "TABLE" || tagName === "UL" || tagName === "OL") {
            blockChildren.push(child as Element);
            continue;
          }
        }
        inlineHost.appendChild(child.cloneNode(true));
      }
      const cellIsHeader = isHeader || cellEl.tagName === "TH";
      // Header cells: pre-apply bold mark to every text run inside so
      // the header reads at a glance even on documents whose style
      // table doesn't define a TableHeader paragraph style.
      const cellInlineMarks: Mark[] = [];
      if (cellIsHeader && ctx.bold) cellInlineMarks.push(ctx.bold.create());
      const inline = collectInline(inlineHost, ctx, cellInlineMarks);
      paragraphsInCell.push(
        ctx.paragraphType.create(
          null,
          inline.length > 0 ? Fragment.fromArray(inline) : null
        )
      );
      for (const blockEl of blockChildren) {
        appendBlocks(blockEl, paragraphsInCell, ctx, null);
      }
      // Borders on every cell; light gray fill on header cells.
      // tableHeader and tableCell share the same attrs surface, so the
      // same attrs object slots into either node type.
      const cellAttrs: Record<string, unknown> = {
        borders: DEFAULT_CELL_BORDERS
      };
      if (cellIsHeader) {
        // tableCell.attrs.backgroundColor takes a plain hex string
        // (the renderer wraps it as `#${value}` for CSS), NOT the
        // `{rgb}` ColorValue shape that BorderSpec.color uses. Same
        // attribute name across two different value shapes; easy to
        // get wrong, hence this note.
        cellAttrs.backgroundColor = HEADER_CELL_BACKGROUND;
      }
      const cellType = cellIsHeader ? headerType : ctx.tableCellType;
      const cellNode = cellType.createAndFill(
        cellAttrs,
        Fragment.fromArray(paragraphsInCell)
      );
      if (cellNode) cellNodes.push(cellNode);
    }
    if (cellNodes.length === 0) continue;
    const rowNode = ctx.tableRowType.create(
      isHeader ? { isHeader: true } : null,
      Fragment.fromArray(cellNodes)
    );
    rowNodes.push(rowNode);
  }
  if (rowNodes.length === 0) return null;
  return ctx.tableType.create(null, Fragment.fromArray(rowNodes));
}
