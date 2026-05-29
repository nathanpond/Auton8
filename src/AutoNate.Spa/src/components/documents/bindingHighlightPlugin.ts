import { Plugin, PluginKey } from "prosemirror-state";
import type { EditorState } from "prosemirror-state";
import type { EditorView } from "prosemirror-view";
import { Decoration, DecorationSet } from "prosemirror-view";
import { bindingIdFromInstruction } from "./bindingFieldNode";
import { parseTableMarkerInstruction } from "./bindingTableNode";

// Ephemeral hover-highlight for bound content. When the user hovers a
// binding row in the side panel, the panel calls setHoveredBinding with
// the binding id; this plugin draws a SINGLE translucent rectangle over
// the bounding box of that binding's rendered content.
//
// Getting one clean box out of docx-editor's *paged* renderer takes a
// two-step trick:
//
//   1. The renderer parks the real ProseMirror editable off-screen
//      (left: -10000px) and paints the visible pages separately, so
//      `view.nodeDOM()` returns off-screen coordinates — useless for
//      positioning. BUT the renderer also projects every PM decoration
//      into the visible `.paged-editor__decoration-overlay` layer, one
//      rect per text run, in *visible* coordinates.
//
//   2. So we emit invisible "anchor" decorations over the binding's
//      node(s), let the renderer project them into visible-coordinate
//      rects, then union those rects into one bounding box and position a
//      single `position: fixed` overlay over it. (A plain decoration
//      can't be the highlight itself — it shatters into per-run boxes;
//      that's the whole problem we're working around.)
//
// We reposition on scroll/resize and on a rAF after each transaction
// (the renderer projects asynchronously), and clip to the editor's scroll
// viewport so the box never bleeds over the toolbar, ruler, or panel.

type HighlightState = { hoveredId: string | null };

export const bindingHighlightKey = new PluginKey<HighlightState>("bindingHighlight");

// Invisible marker class placed on the binding's node decorations. The
// paged renderer copies it onto its projected overlay rects, which is how
// we recover visible-coordinate positions. No CSS targets it.
const ANCHOR_CLASS = "binding-hl-anchor";

// Padding (px) added around the measured content so the border doesn't sit
// flush against the text.
const OVERLAY_PADDING = 4;

export function bindingHighlightPlugin(): Plugin<HighlightState> {
  return new Plugin<HighlightState>({
    key: bindingHighlightKey,
    state: {
      init: () => ({ hoveredId: null }),
      apply: (tr, prev) => {
        const meta = tr.getMeta(bindingHighlightKey) as HighlightState | undefined;
        return meta && "hoveredId" in meta ? meta : prev;
      }
    },
    props: {
      // Invisible anchors only — the visible box is the imperative overlay.
      decorations(state) {
        const hoveredId = bindingHighlightKey.getState(state)?.hoveredId ?? null;
        if (!hoveredId) return DecorationSet.empty;
        const decos = collectAnchorDecorations(state, hoveredId);
        return decos.length ? DecorationSet.create(state.doc, decos) : DecorationSet.empty;
      }
    },
    view: (editorView) => new BindingHighlightOverlay(editorView)
  });
}

// Build node decorations (carrying the invisible anchor class) over every
// piece of the hovered binding's content: a record-field `field` node, or
// an aql-table caption paragraph + the table that follows it.
function collectAnchorDecorations(state: EditorState, hoveredId: string): Decoration[] {
  const decos: Decoration[] = [];
  const { doc } = state;

  doc.descendants((node, pos) => {
    if (node.type.name === "field") {
      if (bindingIdFromInstruction(node.attrs.instruction) === hoveredId) {
        decos.push(Decoration.node(pos, pos + node.nodeSize, { class: ANCHOR_CLASS }));
      }
      return false; // leaf
    }
    return true;
  });

  doc.forEach((block, blockPos) => {
    if (block.type.name === "paragraph" && block.firstChild?.type.name === "field") {
      const marker = parseTableMarkerInstruction(block.firstChild.attrs.instruction);
      if (marker && marker.bindingId === hoveredId) {
        decos.push(
          Decoration.node(blockPos, blockPos + block.nodeSize, { class: ANCHOR_CLASS })
        );
        const afterPara = blockPos + block.nodeSize;
        const next = afterPara < doc.content.size ? doc.nodeAt(afterPara) : null;
        if (next?.type.name === "table") {
          decos.push(
            Decoration.node(afterPara, afterPara + next.nodeSize, { class: ANCHOR_CLASS })
          );
        }
      }
    }
  });

  return decos;
}

// How many frames to keep re-measuring after each trigger. docx-editor's
// paged renderer projects our anchor decorations into its overlay layer
// asynchronously (a frame or two after the transaction), and the rects
// settle over a few more frames; repositioning across a short window lets
// the box appear and track without coupling to the library's internals.
const SETTLE_FRAMES = 8;

class BindingHighlightOverlay {
  private overlay: HTMLDivElement | null = null;
  private rafId: number | null = null;
  private framesLeft = 0;
  private readonly onScrollOrResize = () => this.schedule();

  constructor(private readonly view: EditorView) {
    // Capture-phase scroll catches scrolling on any nested editor scroller.
    window.addEventListener("scroll", this.onScrollOrResize, true);
    window.addEventListener("resize", this.onScrollOrResize);
  }

  update() {
    this.schedule();
  }

  destroy() {
    window.removeEventListener("scroll", this.onScrollOrResize, true);
    window.removeEventListener("resize", this.onScrollOrResize);
    if (this.rafId != null) cancelAnimationFrame(this.rafId);
    this.overlay?.remove();
    this.overlay = null;
  }

  private schedule() {
    this.framesLeft = SETTLE_FRAMES;
    this.tick();
  }

  private tick() {
    if (this.rafId != null) cancelAnimationFrame(this.rafId);
    this.rafId = requestAnimationFrame(() => {
      this.rafId = null;
      this.reposition();
      if (this.framesLeft-- > 0) this.tick();
    });
  }

  private hide() {
    if (this.overlay) this.overlay.style.display = "none";
  }

  private ensureOverlay(): HTMLDivElement {
    if (!this.overlay) {
      const el = document.createElement("div");
      el.className = "binding-hover-overlay";
      el.setAttribute("aria-hidden", "true");
      document.body.appendChild(el);
      this.overlay = el;
    }
    return this.overlay;
  }

  private reposition() {
    const hoveredId = bindingHighlightKey.getState(this.view.state)?.hoveredId ?? null;
    if (!hoveredId) return this.hide();

    const scroller = getScrollParent(this.view.dom as HTMLElement);
    const clip = scroller?.getBoundingClientRect() ?? null;

    // Measure the renderer's projected anchor rects (visible coords).
    // Keep only rects that intersect the editor's scroll viewport — this
    // drops the off-screen (-10000px) editable copy that also carries the
    // anchor class, and any anchors scrolled out of view.
    const anchors = document.querySelectorAll<HTMLElement>(`.${ANCHOR_CLASS}`);
    let left = Infinity;
    let top = Infinity;
    let right = -Infinity;
    let bottom = -Infinity;
    let found = false;
    anchors.forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.width <= 0 && r.height <= 0) return;
      if (clip) {
        const intersects =
          r.right > clip.left &&
          r.left < clip.right &&
          r.bottom > clip.top &&
          r.top < clip.bottom;
        if (!intersects) return;
      } else if (r.left < -1000) {
        return; // no scroller to clip against — drop the parked copy
      }
      found = true;
      left = Math.min(left, r.left);
      top = Math.min(top, r.top);
      right = Math.max(right, r.right);
      bottom = Math.max(bottom, r.bottom);
    });
    if (!found) return this.hide();

    // The projected anchor rects hug the text, so a table's box stops at
    // the rightmost text rather than the grid edge (cells are wider than
    // their content). Extend the box to cover any visible table cell that
    // intersects the anchor region. docx-editor renders the paged table
    // as `.layout-table-cell` divs (the real <table> is parked off-screen);
    // unioning the overlapping cells reaches the full grid bounds. For a
    // non-table binding nothing intersects, so this is a no-op.
    const anchorBox = { left, top, right, bottom };
    document.querySelectorAll<HTMLElement>(".layout-table-cell").forEach((cell) => {
      const r = cell.getBoundingClientRect();
      if (r.width <= 0 && r.height <= 0) return;
      const intersects =
        r.right > anchorBox.left &&
        r.left < anchorBox.right &&
        r.bottom > anchorBox.top &&
        r.top < anchorBox.bottom;
      if (!intersects) return;
      left = Math.min(left, r.left);
      top = Math.min(top, r.top);
      right = Math.max(right, r.right);
      bottom = Math.max(bottom, r.bottom);
    });

    left -= OVERLAY_PADDING;
    top -= OVERLAY_PADDING;
    right += OVERLAY_PADDING;
    bottom += OVERLAY_PADDING;

    // Clamp the box to the visible scroll viewport.
    if (clip) {
      top = Math.max(top, clip.top);
      bottom = Math.min(bottom, clip.bottom);
      left = Math.max(left, clip.left);
      right = Math.min(right, clip.right);
    }
    if (bottom <= top || right <= left) return this.hide();

    const el = this.ensureOverlay();
    el.style.display = "block";
    el.style.left = `${left}px`;
    el.style.top = `${top}px`;
    el.style.width = `${right - left}px`;
    el.style.height = `${bottom - top}px`;
  }
}

// Nearest scrollable ancestor (vertical), or null.
function getScrollParent(el: HTMLElement | null): HTMLElement | null {
  let cur = el?.parentElement ?? null;
  while (cur) {
    const overflowY = getComputedStyle(cur).overflowY;
    if (/(auto|scroll|overlay)/.test(overflowY) && cur.scrollHeight > cur.clientHeight) {
      return cur;
    }
    cur = cur.parentElement;
  }
  return null;
}

// Set (or clear, with null) the currently highlighted binding. No-op when
// the value is unchanged so idle re-hovers don't churn transactions. The
// transaction carries no doc steps — kept out of undo history defensively.
export function setHoveredBinding(
  view: EditorView | null,
  hoveredId: string | null
): void {
  if (!view) return;
  try {
    if (currentHoveredId(view.state) === hoveredId) return;
    const tr = view.state.tr.setMeta(bindingHighlightKey, { hoveredId });
    tr.setMeta("addToHistory", false);
    view.dispatch(tr);
  } catch {
    // Dispatching on a destroyed view throws deep in updateState
    // (`matchesNode` of null). Under React StrictMode's mount/unmount/
    // mount cycle a stale callback can fire against a torn-down view —
    // swallow it (the view is being discarded anyway), don't let it
    // crash the React tree.
  }
}

function currentHoveredId(state: EditorState): string | null {
  return bindingHighlightKey.getState(state)?.hoveredId ?? null;
}

// Scroll the editor so the given binding's content comes into view, and
// highlight it (so the user sees what they navigated to). Reuses the same
// projected anchor rects the highlight overlay measures; because the paged
// renderer projects them a frame or two after the highlight transaction,
// we retry across a few frames until they appear, then scroll the topmost
// one toward the top of the viewport.
export function scrollToBinding(view: EditorView | null, bindingId: string): void {
  if (!view) return;
  setHoveredBinding(view, bindingId);

  // Scroll the topmost VISIBLE projected anchor into view. We can't scroll
  // via view.dom — the paged renderer parks the editable off-screen in a
  // subtree with no shared scroll parent. The projected anchor rects, by
  // contrast, live in the visible content layer, so scrollIntoView on one
  // finds the real scroll container. The renderer projects them a frame or
  // two after the highlight transaction, so retry until one appears.
  let attempts = 0;
  const run = () => {
    let best: HTMLElement | null = null;
    let bestTop = Infinity;
    document.querySelectorAll<HTMLElement>(`.${ANCHOR_CLASS}`).forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.left < -1000) return; // skip the parked off-screen editable copy
      if (r.width <= 0 && r.height <= 0) return;
      if (r.top < bestTop) {
        bestTop = r.top;
        best = el;
      }
    });
    if (best) {
      best.scrollIntoView({ block: "center", behavior: "smooth" });
      return;
    }
    if (attempts++ < 15) requestAnimationFrame(run);
  };
  requestAnimationFrame(run);
}
