import { useEffect, useRef, useState } from "react";
import { Tooltip } from "@mantine/core";
import { CabinetDto, NotebookDto, PageTreeNodeDto } from "@/api/content";
import { ContentItemMenu } from "./ContentItemMenu";
import { cabinetColorFor, defaultCabinetIcon, notesTheme } from "./notesTheme";
import { NotebookWithPages, PageTreeNode } from "./types";

// Returns a ref to attach to a truncated (ellipsis) text node and a boolean
// indicating whether its content currently overflows. Re-measures on mount,
// when the watched value changes, and on every size change of the element —
// so the result stays correct as the sidebar is resized. Used to disable
// row-name tooltips when the full text already fits.
function useTextOverflow<T extends HTMLElement>(text: string) {
  const ref = useRef<T>(null);
  const [overflowing, setOverflowing] = useState(false);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const check = () => setOverflowing(el.scrollWidth > el.clientWidth);
    check();
    const ro = new ResizeObserver(check);
    ro.observe(el);
    return () => ro.disconnect();
  }, [text]);
  return [ref, overflowing] as const;
}

export const EXPLORER_MIN_WIDTH = 220;
export const EXPLORER_MAX_WIDTH = 720;
export const EXPLORER_DEFAULT_WIDTH = 320;

// Hint from the parent that these node ids should be expanded once their row
// is mounted (e.g. after creating a child inside them). Doesn't close rows
// that aren't in the set — user-driven collapses are preserved.
export type NewPageTarget = {
  notebookId: string;
  parentPageId: string | null;
  parentLabel: string;
  parentKind: "notebook" | "page";
};

type Props = {
  cabinet: CabinetDto | null;
  notebooks: NotebookWithPages[];
  loading?: boolean;
  activePageId: string | null;
  onPagePick: (pageId: string) => void;
  onNewNotebook?: () => void;
  onRenameCabinet?: (cabinet: CabinetDto) => void;
  onArchiveCabinet?: (cabinet: CabinetDto) => void;
  onDeleteCabinet?: (cabinet: CabinetDto) => void;
  onRenameNotebook?: (notebook: NotebookDto) => void;
  onArchiveNotebook?: (notebook: NotebookDto) => void;
  onDeleteNotebook?: (notebook: NotebookDto) => void;
  onRenamePage?: (page: PageTreeNodeDto) => void;
  onArchivePage?: (page: PageTreeNodeDto) => void;
  onDeletePage?: (page: PageTreeNodeDto) => void;
  onNewPage?: (target: NewPageTarget) => void;
  forceExpandIds?: ReadonlySet<string>;
  width: number;
  // Called continuously while dragging; parent stores the live value.
  onResize: (next: number) => void;
  // Called once when the drag ends (mouseup). Receives the final width
  // directly so the parent doesn't have to chase its own state through
  // a stale-closure path on the persist callback.
  onResizeEnd?: (final: number) => void;
  // Hides the entire sidebar (cabinet rail + this panel). Parent renders
  // the floating restore button to bring it back.
  onCollapse?: () => void;
};

const kebabSmStyle: React.CSSProperties = {
  background: "transparent",
  border: "none",
  cursor: "pointer",
  color: notesTheme.muted,
  width: 18,
  height: 18,
  borderRadius: 3,
  padding: 0
};

export function Explorer({
  cabinet,
  notebooks,
  loading,
  activePageId,
  onPagePick,
  onNewNotebook,
  onRenameCabinet,
  onArchiveCabinet,
  onDeleteCabinet,
  onRenameNotebook,
  onArchiveNotebook,
  onDeleteNotebook,
  onRenamePage,
  onArchivePage,
  onDeletePage,
  onNewPage,
  forceExpandIds,
  width,
  onResize,
  onResizeEnd,
  onCollapse
}: Props) {
  const canCreate = !!cabinet && !!onNewNotebook;
  const [query, setQuery] = useState("");
  const color = cabinet ? cabinetColorFor(cabinet.id) : notesTheme.muted;
  const icon = cabinet?.icon ?? defaultCabinetIcon();
  // Pointer-driven resize. We attach window listeners only for the lifetime
  // of a single drag so we don't leak them. width = startWidth + (clientX -
  // startX), clamped to [min, max]. Cursor + user-select are forced globally
  // during the drag so the cursor doesn't flicker over other elements and
  // accidental text selection doesn't fight the drag.
  const [dragging, setDragging] = useState(false);
  const beginDrag = (e: React.MouseEvent) => {
    e.preventDefault();
    const startX = e.clientX;
    const startWidth = width;
    // Local mirror of the latest computed width so the mouseup handler can
    // report it back to the parent without depending on React state — by
    // the time onResizeEnd fires, the parent has re-rendered many times
    // and a closure over `width` would be stale (it'd still hold startWidth).
    let latestWidth = startWidth;
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
    setDragging(true);
    const onMove = (ev: MouseEvent) => {
      latestWidth = clamp(
        startWidth + (ev.clientX - startX),
        EXPLORER_MIN_WIDTH, EXPLORER_MAX_WIDTH
      );
      onResize(latestWidth);
    };
    const onUp = () => {
      document.body.style.removeProperty("cursor");
      document.body.style.removeProperty("user-select");
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      setDragging(false);
      onResizeEnd?.(latestWidth);
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  };

  return (
    <aside
      style={{
        position: "relative",
        width,
        flexShrink: 0,
        borderRight: `1px solid ${notesTheme.border}`,
        background: "#fff",
        display: "flex",
        flexDirection: "column",
        minWidth: 0
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "12px 14px",
          borderBottom: `1px solid ${notesTheme.border}`
        }}
      >
        <div
          style={{
            width: 30,
            height: 30,
            borderRadius: 6,
            background: color + "20",
            color,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 14
          }}
        >
          <i className={`fa ${icon}`} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 13, fontWeight: 700, color: notesTheme.dark }}>
            {cabinet?.name ?? "No cabinet selected"}
          </div>
          <div style={{ fontSize: 10.5, color: notesTheme.muted }}>
            {notebooks.length} {notebooks.length === 1 ? "notebook" : "notebooks"}
          </div>
        </div>
        {cabinet && (
          <ContentItemMenu
            entityLabel="cabinet"
            isArchived={cabinet.isArchived}
            onRename={() => onRenameCabinet?.(cabinet)}
            onArchive={() => onArchiveCabinet?.(cabinet)}
            onDelete={() => onDeleteCabinet?.(cabinet)}
          />
        )}
      </div>

      <div style={{ padding: "10px 12px 6px" }}>
        <div style={{ position: "relative" }}>
          <i
            className="fa fa-magnifying-glass"
            style={{
              position: "absolute",
              left: 9,
              top: "50%",
              transform: "translateY(-50%)",
              color: notesTheme.muted,
              fontSize: 10.5
            }}
          />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search this cabinet…"
            style={{
              width: "100%",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "6px 10px 6px 26px",
              fontSize: 11.5,
              outline: "none",
              fontFamily: "inherit",
              background: "#fff"
            }}
          />
        </div>
      </div>

      <div style={{ flex: 1, overflowY: "auto", padding: "4px 8px 8px" }}>
        {loading && (
          <div style={{ padding: 12, fontSize: 11, color: notesTheme.muted }}>
            Loading…
          </div>
        )}
        {!loading &&
          notebooks.map((nb, idx) => (
            <NotebookRow
              key={nb.id}
              nb={nb}
              defaultOpen={idx === 0}
              color={color}
              activePageId={activePageId}
              onPagePick={onPagePick}
              query={query.trim().toLowerCase()}
              onRename={onRenameNotebook}
              onArchive={onArchiveNotebook}
              onDelete={onDeleteNotebook}
              onRenamePage={onRenamePage}
              onArchivePage={onArchivePage}
              onDeletePage={onDeletePage}
              onNewPage={onNewPage}
              forceExpandIds={forceExpandIds}
            />
          ))}
        {!loading && notebooks.length === 0 && cabinet && (
          <div style={{ padding: 12, fontSize: 11, color: notesTheme.muted, fontStyle: "italic" }}>
            No notebooks in this cabinet yet.
          </div>
        )}
      </div>

      {/* Footer row: "New notebook" (grows) + collapse-sidebar icon button.
          Both share a single horizontal strip at the bottom of the panel. */}
      <div
        style={{
          display: "flex",
          alignItems: "stretch",
          gap: 6,
          margin: 10
        }}
      >
        <button
          type="button"
          onClick={onNewNotebook}
          disabled={!canCreate}
          title={canCreate ? "New notebook" : "Select a cabinet to add notebooks"}
          style={{
            flex: 1,
            minWidth: 0,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            gap: 6,
            background: "transparent",
            border: `1px dashed ${notesTheme.border}`,
            borderRadius: 4,
            padding: 7,
            color: notesTheme.muted,
            fontSize: 11.5,
            fontWeight: 700,
            cursor: canCreate ? "pointer" : "not-allowed",
            fontFamily: "inherit",
            opacity: canCreate ? 1 : 0.5
          }}
        >
          <i className="fa fa-plus" style={{ fontSize: 10 }} />
          New notebook
        </button>
        {onCollapse && (
          <Tooltip label="Collapse sidebar" position="top" withArrow>
            <button
              type="button"
              onClick={onCollapse}
              aria-label="Collapse sidebar"
              style={{
                flexShrink: 0,
                width: 32,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                background: "transparent",
                border: `1px solid ${notesTheme.border}`,
                borderRadius: 4,
                color: notesTheme.muted,
                cursor: "pointer",
                fontFamily: "inherit",
                fontSize: 12
              }}
            >
              <i className="fa fa-angles-left" />
            </button>
          </Tooltip>
        )}
      </div>

      {/* Resize handle — a thin invisible grab strip sitting over the right
          border. We render a 6px-wide hit target but only paint a 1px line
          (the existing border-right) so the panel chrome stays clean.
          During an active drag we colorize the strip so the user has visual
          confirmation that the drag is engaged. */}
      <div
        onMouseDown={beginDrag}
        role="separator"
        aria-orientation="vertical"
        aria-label="Resize explorer"
        title="Drag to resize"
        style={{
          position: "absolute",
          top: 0,
          bottom: 0,
          right: -3,
          width: 6,
          cursor: "col-resize",
          background: dragging ? notesTheme.primary : "transparent",
          opacity: dragging ? 0.4 : 1,
          zIndex: 5,
          userSelect: "none"
        }}
      />
    </aside>
  );
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function NotebookRow({
  nb,
  defaultOpen,
  color,
  activePageId,
  onPagePick,
  query,
  onRename,
  onArchive,
  onDelete,
  onRenamePage,
  onArchivePage,
  onDeletePage,
  onNewPage,
  forceExpandIds
}: {
  nb: NotebookWithPages;
  defaultOpen: boolean;
  color: string;
  activePageId: string | null;
  onPagePick: (pageId: string) => void;
  query: string;
  onRename?: (notebook: NotebookDto) => void;
  onArchive?: (notebook: NotebookDto) => void;
  onDelete?: (notebook: NotebookDto) => void;
  onRenamePage?: (page: PageTreeNodeDto) => void;
  onArchivePage?: (page: PageTreeNodeDto) => void;
  onDeletePage?: (page: PageTreeNodeDto) => void;
  onNewPage?: (target: NewPageTarget) => void;
  forceExpandIds?: ReadonlySet<string>;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const [hover, setHover] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const hasPages = nb.pages.length > 0;
  const controlsVisible = hover || menuOpen;
  const hasMenu = !!(onRename || onArchive || onDelete);
  const [titleRef, titleOverflowing] = useTextOverflow<HTMLSpanElement>(nb.name);

  useEffect(() => {
    if (forceExpandIds?.has(nb.id)) setOpen(true);
  }, [forceExpandIds, nb.id]);
  return (
    <div style={{ marginTop: 2 }}>
      {/* Outer row is a <div>, not a <button>: the row contains the "+" and
          kebab buttons, and nested <button>s are invalid HTML. role=button +
          tabIndex give keyboard focus + Enter/Space activation back.
          Wrapped in <Tooltip> so the full name shows on hover anywhere on
          the row — title, icons, padding — but only when the visible title
          is actually being truncated. */}
      <Tooltip
        label={nb.name}
        disabled={!titleOverflowing}
        openDelay={300}
        position="right"
        withArrow
      >
        <div
          role="button"
          tabIndex={0}
          onClick={() => hasPages && setOpen(!open)}
          onKeyDown={(e) => {
            if (e.key !== "Enter" && e.key !== " ") return;
            if (!hasPages) return;
            e.preventDefault();
            setOpen((o) => !o);
          }}
          onMouseEnter={() => setHover(true)}
          onMouseLeave={() => setHover(false)}
          style={{
            position: "relative",
            width: "100%",
            display: "flex",
            alignItems: "center",
            gap: 7,
            background: hover ? notesTheme.hover : "transparent",
            padding: "5px 8px",
            cursor: hasPages ? "pointer" : "default",
            fontFamily: "inherit",
            textAlign: "left",
            color: notesTheme.dark,
            borderRadius: 4
          }}
        >
          <i
            className={`fa fa-chevron-${open ? "down" : "right"}`}
            style={{
              fontSize: 9,
              color: notesTheme.muted,
              width: 10,
              visibility: hasPages ? "visible" : "hidden"
            }}
          />
          <i className={`fa ${nb.icon ?? "fa-book"}`} style={{ fontSize: 11, color }} />
          <span
            ref={titleRef}
            style={{
              fontSize: 12,
              fontWeight: 700,
              flex: 1,
              minWidth: 0,
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap"
            }}
          >
            {nb.name}
          </span>
        {/* Action affordances. Absolutely positioned so they don't reserve
            layout space when the row isn't hovered — long notebook names get
            the full row width. The solid background matches the row state so
            the icons sit on a clean surface; a thin gradient on the left fades
            any title text behind them out instead of cutting it abruptly. */}
        {controlsVisible && (
          <span
            onClick={(e) => e.stopPropagation()}
            style={{
              position: "absolute",
              right: 4,
              top: "50%",
              transform: "translateY(-50%)",
              display: "inline-flex",
              gap: 2,
              alignItems: "center",
              paddingLeft: 12,
              background: hover ? notesTheme.hover : "#fff",
              boxShadow: `-12px 0 8px -6px ${hover ? notesTheme.hover : "#fff"}`,
              borderRadius: 4
            }}
          >
            <button
              type="button"
              aria-label="New page"
              disabled={!onNewPage}
              onClick={(e) => {
                e.stopPropagation();
                onNewPage?.({
                  notebookId: nb.id,
                  parentPageId: null,
                  parentLabel: nb.name,
                  parentKind: "notebook"
                });
              }}
              style={{
                ...kebabSmStyle,
                cursor: onNewPage ? "pointer" : "not-allowed",
                opacity: onNewPage ? 1 : 0.5
              }}
            >
              <i className="fa fa-plus" style={{ fontSize: 9 }} />
            </button>
            {hasMenu && (
              <ContentItemMenu
                entityLabel="notebook"
                size="sm"
                isArchived={nb.isArchived}
                onRename={() => onRename?.(nb)}
                onArchive={() => onArchive?.(nb)}
                onDelete={() => onDelete?.(nb)}
                onOpenChange={setMenuOpen}
              />
            )}
          </span>
        )}
        </div>
      </Tooltip>
      {open && hasPages && (
        <div style={{ marginLeft: 16 }}>
          {nb.pages.map((p) => (
            <PageRow
              key={p.id}
              page={p}
              activePageId={activePageId}
              onPagePick={onPagePick}
              query={query}
              onRename={onRenamePage}
              onArchive={onArchivePage}
              onDelete={onDeletePage}
              onNewPage={onNewPage}
              forceExpandIds={forceExpandIds}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function PageRow({
  page,
  activePageId,
  onPagePick,
  query,
  depth = 0,
  onRename,
  onArchive,
  onDelete,
  onNewPage,
  forceExpandIds
}: {
  page: PageTreeNode;
  activePageId: string | null;
  onPagePick: (pageId: string) => void;
  query: string;
  depth?: number;
  onRename?: (page: PageTreeNodeDto) => void;
  onArchive?: (page: PageTreeNodeDto) => void;
  onDelete?: (page: PageTreeNodeDto) => void;
  onNewPage?: (target: NewPageTarget) => void;
  forceExpandIds?: ReadonlySet<string>;
}) {
  const hasChildren = page.children.length > 0;
  const [open, setOpen] = useState(hasChildren);
  const [hover, setHover] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const active = page.id === activePageId;
  const controlsVisible = hover || menuOpen;
  const hasMenu = !!(onRename || onArchive || onDelete);
  const [titleRef, titleOverflowing] = useTextOverflow<HTMLSpanElement>(page.title);

  useEffect(() => {
    if (forceExpandIds?.has(page.id)) setOpen(true);
  }, [forceExpandIds, page.id]);

  if (query && !pageMatchesQuery(page, query)) return null;

  return (
    <>
      <Tooltip
        label={page.title}
        disabled={!titleOverflowing}
        openDelay={300}
        position="right"
        withArrow
      >
        <div
          onClick={(e) => {
            e.stopPropagation();
            onPagePick(page.id);
          }}
          onMouseEnter={() => setHover(true)}
          onMouseLeave={() => setHover(false)}
          style={{
            position: "relative",
            display: "flex",
            alignItems: "center",
            gap: 6,
            padding: "4px 8px",
            marginLeft: depth * 14,
            borderRadius: 4,
            cursor: "pointer",
            background: active
              ? notesTheme.selected
              : hover
                ? notesTheme.hover
                : "transparent",
            color: active ? notesTheme.primary : notesTheme.dark
          }}
        >
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              if (hasChildren) setOpen(!open);
            }}
            style={{
              background: "transparent",
              border: "none",
              cursor: "pointer",
              width: 10,
              color: notesTheme.muted,
              padding: 0,
              visibility: hasChildren ? "visible" : "hidden"
            }}
          >
            <i className={`fa fa-chevron-${open ? "down" : "right"}`} style={{ fontSize: 8 }} />
          </button>
          <i
            className="fa fa-file-lines"
            style={{ fontSize: 11, color: active ? notesTheme.primary : notesTheme.muted }}
          />
          <span
            ref={titleRef}
            style={{
              fontSize: 11.5,
              fontWeight: active ? 700 : 500,
              flex: 1,
              minWidth: 0,
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap"
            }}
          >
            {page.title}
          </span>
        {controlsVisible && (
          <span
            onClick={(e) => e.stopPropagation()}
            style={{
              position: "absolute",
              right: 4,
              top: "50%",
              transform: "translateY(-50%)",
              display: "inline-flex",
              gap: 2,
              alignItems: "center",
              paddingLeft: 12,
              background: active
                ? notesTheme.selected
                : hover
                  ? notesTheme.hover
                  : "#fff",
              boxShadow: `-12px 0 8px -6px ${active ? notesTheme.selected : hover ? notesTheme.hover : "#fff"}`,
              borderRadius: 4
            }}
          >
            <button
              type="button"
              aria-label="New sub-page"
              disabled={!onNewPage}
              onClick={(e) => {
                e.stopPropagation();
                onNewPage?.({
                  notebookId: page.notebookId,
                  parentPageId: page.id,
                  parentLabel: page.title,
                  parentKind: "page"
                });
              }}
              style={{
                ...kebabSmStyle,
                cursor: onNewPage ? "pointer" : "not-allowed",
                opacity: onNewPage ? 1 : 0.5
              }}
            >
              <i className="fa fa-plus" style={{ fontSize: 9 }} />
            </button>
            {hasMenu && (
              <ContentItemMenu
                entityLabel="page"
                size="sm"
                isArchived={page.isArchived}
                onRename={() => onRename?.(page)}
                onArchive={() => onArchive?.(page)}
                onDelete={() => onDelete?.(page)}
                onOpenChange={setMenuOpen}
              />
            )}
          </span>
        )}
        </div>
      </Tooltip>
      {open && hasChildren && (
        <>
          {page.children.map((c) => (
            <PageRow
              key={c.id}
              page={c}
              activePageId={activePageId}
              onPagePick={onPagePick}
              query={query}
              depth={depth + 1}
              onRename={onRename}
              onArchive={onArchive}
              onDelete={onDelete}
              onNewPage={onNewPage}
              forceExpandIds={forceExpandIds}
            />
          ))}
        </>
      )}
    </>
  );
}

function pageMatchesQuery(page: PageTreeNode, query: string): boolean {
  if (page.title.toLowerCase().includes(query)) return true;
  return page.children.some((c) => pageMatchesQuery(c, query));
}
