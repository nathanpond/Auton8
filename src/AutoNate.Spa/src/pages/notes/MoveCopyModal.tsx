import { useEffect, useMemo, useState } from "react";
import { ProjectTreePageDto, ProjectTreeResponse } from "@/api/content";
import { useProjectTree } from "@/hooks/useContent";
import { notesTheme } from "./notesTheme";

// Destination types selectable in the picker. Pages can re-parent under a
// notebook (becomes a top-level page) or another page. Notes can only attach
// to a page (parent kind in the data model is always "page"). PageDestination
// also carries the destination's notebook id because the backend's CopyPage
// endpoint requires it — keeping it on the destination saves a second lookup.
export type PageDestination = {
  kind: "page";
  id: string;
  // URL locator for the destination page — used by callers to navigate to
  // the new location after a successful move/copy.
  locator: number;
  title: string;
  notebookId: string;
};
export type NotebookDestination = {
  kind: "notebook";
  id: string;
  name: string;
};
export type Destination = PageDestination | NotebookDestination;

type Props = {
  mode: "move" | "copy";
  // What's being moved/copied. For a page, we exclude the page itself and
  // its descendants from valid destinations (no self-cycle). For a note, the
  // pickable set is restricted to pages.
  itemKind: "page" | "note";
  itemId: string;
  itemTitle: string;
  // The project the item lives in. The picker only ever shows this project's
  // tree — cross-project moves aren't supported.
  projectId: string | null;
  // Page-only: the source page's notebook + parent so we can grey out the
  // current location (no-op move) and skip the descendant subtree.
  sourceNotebookId?: string | null;
  sourceParentPageId?: string | null;
  // Note-only: the page that currently owns the note.
  sourcePageId?: string | null;
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onConfirm: (dest: Destination) => void;
};

export function MoveCopyModal({
  mode,
  itemKind,
  itemId,
  itemTitle,
  projectId,
  sourceNotebookId,
  sourceParentPageId,
  sourcePageId,
  busy,
  error,
  onClose,
  onConfirm
}: Props) {
  const [selected, setSelected] = useState<Destination | null>(null);
  const [query, setQuery] = useState("");
  const treeQuery = useProjectTree(projectId);

  // Pre-compute the set of descendant page ids for the page being moved —
  // a page can't land inside itself or one of its own descendants. For
  // notes the exclusion set is empty.
  const excludedPageIds = useMemo(() => {
    if (itemKind !== "page" || !treeQuery.data) return new Set<string>();
    return collectDescendants(treeQuery.data, itemId);
  }, [itemKind, itemId, treeQuery.data]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const title =
    (mode === "move" ? "Move " : "Copy ") +
    (itemKind === "page" ? "page" : "note") +
    ` — ${itemTitle}`;
  const verb = mode === "move" ? "Move here" : "Copy here";
  const canConfirm = selected != null && !busy && !isSameLocation(selected);

  function isSameLocation(dest: Destination): boolean {
    if (mode !== "move") return false;
    if (itemKind === "page") {
      if (dest.kind === "notebook") {
        // No-op when moving a page that is currently top-level in this notebook
        return dest.id === sourceNotebookId && sourceParentPageId == null;
      }
      return dest.id === sourceParentPageId;
    }
    // Note: no-op when target page equals source page.
    return dest.kind === "page" && dest.id === sourcePageId;
  }

  return (
    <div
      onClick={() => !busy && onClose()}
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 220,
        background: "rgba(32, 37, 42, 0.55)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: 20,
        animation: "notesFadeIn 140ms ease"
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "min(560px, 100%)",
          maxHeight: "80vh",
          background: "#fff",
          borderRadius: 6,
          boxShadow: "0 22px 60px -12px rgba(0,0,0,0.35)",
          fontFamily: "inherit",
          display: "flex",
          flexDirection: "column",
          animation: "notesPopIn 180ms cubic-bezier(.2,.9,.3,1.2)"
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            padding: "16px 18px",
            borderBottom: `1px solid ${notesTheme.border}`,
            flexShrink: 0
          }}
        >
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: 6,
              background: notesTheme.primary + "20",
              color: notesTheme.primary,
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              fontSize: 14,
              flexShrink: 0
            }}
          >
            <i className={`fa ${mode === "move" ? "fa-arrow-right" : "fa-copy"}`} />
          </div>
          <h3
            style={{
              margin: 0,
              fontSize: 14,
              fontWeight: 700,
              color: notesTheme.dark,
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap"
            }}
          >
            {title}
          </h3>
        </div>

        <div style={{ padding: "12px 18px 0 18px", flexShrink: 0 }}>
          <div style={{ fontSize: 12, color: notesTheme.muted, marginBottom: 8 }}>
            {itemKind === "page"
              ? "Pick a notebook or page in the same project."
              : "Pick a page in the same project."}{" "}
            You need contributor access to the destination.
          </div>
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
              placeholder="Filter destinations…"
              style={{
                width: "100%",
                border: `1px solid ${notesTheme.border}`,
                borderRadius: 4,
                padding: "6px 10px 6px 26px",
                fontSize: 11.5,
                outline: "none",
                fontFamily: "inherit",
                background: "#fff",
                boxSizing: "border-box"
              }}
            />
          </div>
        </div>

        <div style={{ padding: "12px 8px 0 8px", overflowY: "auto", flex: 1, minHeight: 200 }}>
          {treeQuery.isLoading && (
            <div style={{ padding: 12, fontSize: 11.5, color: notesTheme.muted }}>
              Loading project tree…
            </div>
          )}
          {treeQuery.isError && (
            <div style={{ padding: 12, fontSize: 11.5, color: notesTheme.danger }}>
              Couldn&apos;t load the project tree.
            </div>
          )}
          {treeQuery.data && (
            <TreePicker
              tree={treeQuery.data}
              itemKind={itemKind}
              excludedPageIds={excludedPageIds}
              selected={selected}
              onSelect={setSelected}
              query={query.trim().toLowerCase()}
            />
          )}
        </div>

        {error && (
          <div
            style={{
              margin: "12px 18px 0 18px",
              padding: "8px 10px",
              background: "#fee",
              border: `1px solid ${notesTheme.danger}`,
              borderRadius: 4,
              color: notesTheme.danger,
              fontSize: 12,
              flexShrink: 0
            }}
          >
            <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
            {error}
          </div>
        )}

        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            gap: 8,
            padding: "12px 16px",
            borderTop: `1px solid ${notesTheme.border}`,
            background: "#f8f9fa",
            flexShrink: 0
          }}
        >
          <div style={{ fontSize: 11.5, color: notesTheme.muted, flex: 1, minWidth: 0, overflow: "hidden" }}>
            {selected ? (
              <>
                <i
                  className={`fa ${selected.kind === "notebook" ? "fa-book" : "fa-file-lines"}`}
                  style={{ marginRight: 6, color: notesTheme.primary }}
                />
                <strong style={{ color: notesTheme.dark }}>
                  {selected.kind === "notebook" ? selected.name : selected.title}
                </strong>
              </>
            ) : (
              <span>No destination selected.</span>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            style={{
              background: "#fff",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: notesTheme.dark,
              cursor: busy ? "not-allowed" : "pointer",
              fontFamily: "inherit",
              opacity: busy ? 0.6 : 1
            }}
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => selected && onConfirm(selected)}
            disabled={!canConfirm}
            style={{
              background: notesTheme.primary,
              border: `1px solid ${notesTheme.primary}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: "#fff",
              fontFamily: "inherit",
              cursor: !canConfirm ? "not-allowed" : "pointer",
              opacity: !canConfirm ? 0.6 : 1
            }}
          >
            {busy ? "Working…" : verb}
          </button>
        </div>
      </div>
    </div>
  );
}

// Walk the tree once to collect every descendant page id (and the page
// itself). Used to gate page move/copy targets to non-self-non-descendants.
function collectDescendants(tree: ProjectTreeResponse, pageId: string): Set<string> {
  const result = new Set<string>([pageId]);
  for (const cab of tree.cabinets) {
    for (const nb of cab.notebooks) {
      // Build a parent → children index per notebook, then BFS from pageId.
      const childrenByParent = new Map<string, ProjectTreePageDto[]>();
      for (const p of nb.pages) {
        const key = p.parentPageId ?? "__root__";
        const list = childrenByParent.get(key) ?? [];
        list.push(p);
        childrenByParent.set(key, list);
      }
      const stack: string[] = [pageId];
      while (stack.length > 0) {
        const cur = stack.pop()!;
        const kids = childrenByParent.get(cur);
        if (!kids) continue;
        for (const k of kids) {
          if (result.has(k.id)) continue;
          result.add(k.id);
          stack.push(k.id);
        }
      }
    }
  }
  return result;
}

function TreePicker({
  tree,
  itemKind,
  excludedPageIds,
  selected,
  onSelect,
  query
}: {
  tree: ProjectTreeResponse;
  itemKind: "page" | "note";
  excludedPageIds: Set<string>;
  selected: Destination | null;
  onSelect: (dest: Destination) => void;
  query: string;
}) {
  // Notebooks are pickable only when the item is a page AND the user has
  // edit on the notebook. Pages are pickable for both kinds (page becomes a
  // sub-page; note attaches to that page) AND when the user has edit AND
  // the page isn't in the exclusion set.
  const allowNotebookDest = itemKind === "page";

  return (
    <div style={{ padding: "0 4px 12px" }}>
      {tree.cabinets.map((cab) => (
        <CabinetGroup
          key={cab.id}
          cabinet={cab}
          allowNotebookDest={allowNotebookDest}
          excludedPageIds={excludedPageIds}
          selected={selected}
          onSelect={onSelect}
          query={query}
        />
      ))}
      {tree.cabinets.length === 0 && (
        <div style={{ padding: 16, fontSize: 11.5, color: notesTheme.muted }}>
          You don&apos;t have access to any destinations in this project.
        </div>
      )}
    </div>
  );
}

function CabinetGroup({
  cabinet,
  allowNotebookDest,
  excludedPageIds,
  selected,
  onSelect,
  query
}: {
  cabinet: ProjectTreeResponse["cabinets"][number];
  allowNotebookDest: boolean;
  excludedPageIds: Set<string>;
  selected: Destination | null;
  onSelect: (dest: Destination) => void;
  query: string;
}) {
  return (
    <div style={{ marginTop: 4 }}>
      <div
        style={{
          padding: "6px 10px",
          fontSize: 10.5,
          fontWeight: 700,
          color: notesTheme.muted,
          textTransform: "uppercase",
          letterSpacing: "0.06em",
          display: "flex",
          alignItems: "center",
          gap: 6
        }}
      >
        <i className={`fa ${cabinet.icon ?? "fa-folder"}`} />
        <span>{cabinet.name}</span>
      </div>
      {cabinet.notebooks.map((nb) => (
        <NotebookGroup
          key={nb.id}
          notebook={nb}
          allowNotebookDest={allowNotebookDest}
          excludedPageIds={excludedPageIds}
          selected={selected}
          onSelect={onSelect}
          query={query}
        />
      ))}
    </div>
  );
}

function NotebookGroup({
  notebook,
  allowNotebookDest,
  excludedPageIds,
  selected,
  onSelect,
  query
}: {
  notebook: ProjectTreeResponse["cabinets"][number]["notebooks"][number];
  allowNotebookDest: boolean;
  excludedPageIds: Set<string>;
  selected: Destination | null;
  onSelect: (dest: Destination) => void;
  query: string;
}) {
  // Build a parent → children map for hierarchical render.
  const childrenByParent = useMemo(() => {
    const m = new Map<string | null, typeof notebook.pages>();
    for (const p of notebook.pages) {
      const key = p.parentPageId ?? null;
      const list = m.get(key) ?? [];
      list.push(p);
      m.set(key, list);
    }
    return m;
  }, [notebook.pages]);

  const matchesQuery = (label: string) => !query || label.toLowerCase().includes(query);
  const showNotebook = matchesQuery(notebook.name) || hasMatchingDescendant(notebook.pages, query);
  if (!showNotebook) return null;

  const notebookSelected = selected?.kind === "notebook" && selected.id === notebook.id;
  const notebookDisabled = !allowNotebookDest || !notebook.canEdit;

  return (
    <div style={{ marginBottom: 4 }}>
      <DestRow
        icon={`fa ${notebook.icon ?? "fa-book"}`}
        label={notebook.name}
        depth={0}
        selected={notebookSelected}
        disabled={notebookDisabled}
        disabledHint={
          !allowNotebookDest
            ? "Notes can only move/copy under a page"
            : !notebook.canEdit
              ? "You don't have contributor access here"
              : undefined
        }
        onClick={() => onSelect({ kind: "notebook", id: notebook.id, name: notebook.name })}
      />
      {(childrenByParent.get(null) ?? []).map((p) => (
        <PageBranch
          key={p.id}
          page={p}
          notebookId={notebook.id}
          allChildren={childrenByParent}
          depth={1}
          excludedPageIds={excludedPageIds}
          selected={selected}
          onSelect={onSelect}
          query={query}
        />
      ))}
    </div>
  );
}

function PageBranch({
  page,
  notebookId,
  allChildren,
  depth,
  excludedPageIds,
  selected,
  onSelect,
  query
}: {
  page: ProjectTreePageDto;
  notebookId: string;
  allChildren: Map<string | null, ProjectTreePageDto[]>;
  depth: number;
  excludedPageIds: Set<string>;
  selected: Destination | null;
  onSelect: (dest: Destination) => void;
  query: string;
}) {
  const matchesQuery = (label: string) => !query || label.toLowerCase().includes(query);
  const kids = allChildren.get(page.id) ?? [];
  const visibleSelf = matchesQuery(page.title);
  const visibleChildren = kids.some((c) => isMatchInSubtree(c, allChildren, query));
  if (!visibleSelf && !visibleChildren) return null;

  const excluded = excludedPageIds.has(page.id);
  const pickable = !excluded && page.canEdit;
  const isSel = selected?.kind === "page" && selected.id === page.id;
  return (
    <>
      <DestRow
        icon="fa fa-file-lines"
        label={page.title}
        depth={depth}
        selected={isSel}
        disabled={!pickable}
        disabledHint={
          excluded
            ? "Can't pick the source page or one of its descendants"
            : !page.canEdit
              ? "You don't have contributor access here"
              : undefined
        }
        onClick={() =>
          onSelect({
            kind: "page",
            id: page.id,
            locator: page.locator,
            title: page.title,
            notebookId
          })
        }
      />
      {kids.map((k) => (
        <PageBranch
          key={k.id}
          page={k}
          notebookId={notebookId}
          allChildren={allChildren}
          depth={depth + 1}
          excludedPageIds={excludedPageIds}
          selected={selected}
          onSelect={onSelect}
          query={query}
        />
      ))}
    </>
  );
}

function DestRow({
  icon,
  label,
  depth,
  selected,
  disabled,
  disabledHint,
  onClick
}: {
  icon: string;
  label: string;
  depth: number;
  selected: boolean;
  disabled: boolean;
  disabledHint?: string;
  onClick: () => void;
}) {
  const [hover, setHover] = useState(false);
  const showHover = hover && !disabled;
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={disabledHint}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 8,
        width: "100%",
        marginLeft: depth * 14,
        background: selected
          ? notesTheme.selected
          : showHover
            ? notesTheme.rowHover
            : "transparent",
        border: "none",
        borderRadius: 4,
        padding: "5px 10px",
        textAlign: "left",
        cursor: disabled ? "not-allowed" : "pointer",
        color: selected
          ? notesTheme.primary
          : disabled
            ? notesTheme.muted
            : notesTheme.dark,
        opacity: disabled ? 0.6 : 1,
        fontFamily: "inherit",
        fontSize: 12,
        fontWeight: selected ? 700 : 500,
        // width: depth * 14px gets added to marginLeft; subtract from width so
        // rows don't overflow the panel horizontally.
        boxSizing: "border-box"
      }}
    >
      <i className={icon} style={{ fontSize: 11, color: selected ? notesTheme.primary : notesTheme.muted }} />
      <span
        style={{
          flex: 1,
          minWidth: 0,
          overflow: "hidden",
          textOverflow: "ellipsis",
          whiteSpace: "nowrap"
        }}
      >
        {label}
      </span>
      {selected && <i className="fa fa-check" style={{ fontSize: 10 }} />}
    </button>
  );
}

function hasMatchingDescendant(
  pages: ProjectTreePageDto[],
  query: string
): boolean {
  if (!query) return true;
  return pages.some((p) => p.title.toLowerCase().includes(query));
}

function isMatchInSubtree(
  page: ProjectTreePageDto,
  byParent: Map<string | null, ProjectTreePageDto[]>,
  query: string
): boolean {
  if (!query) return true;
  if (page.title.toLowerCase().includes(query)) return true;
  const kids = byParent.get(page.id) ?? [];
  return kids.some((k) => isMatchInSubtree(k, byParent, query));
}
