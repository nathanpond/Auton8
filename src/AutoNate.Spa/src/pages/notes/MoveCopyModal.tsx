import { useMemo, useState } from "react";
import { TextInput } from "@mantine/core";
import { ProjectTreePageDto, ProjectTreeResponse } from "@/api/content";
import { useProjectTree } from "@/hooks/useContent";
import { NotesModal, btnGhostStyle, btnPrimaryStyle } from "./NotesModal";
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
    <NotesModal
      onClose={onClose}
      title={
        // minWidth:0 lets the long "Move page — <title>" string ellipsize
        // inside the header flex row the way the old <h3> did.
        <span
          style={{
            minWidth: 0,
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: "nowrap"
          }}
        >
          {title}
        </span>
      }
      icon={mode === "move" ? "fa-arrow-right" : "fa-copy"}
      width="min(560px, 100%)"
      busy={busy}
      footer={
        <>
          <div
            style={{
              fontSize: 11.5,
              color: notesTheme.muted,
              flex: 1,
              minWidth: 0,
              overflow: "hidden"
            }}
          >
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
              ...btnGhostStyle,
              cursor: busy ? "not-allowed" : "pointer",
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
              ...btnPrimaryStyle,
              cursor: !canConfirm ? "not-allowed" : "pointer",
              opacity: !canConfirm ? 0.6 : 1
            }}
          >
            {busy ? "Working…" : verb}
          </button>
        </>
      }
    >
      <div style={{ fontSize: 12, color: notesTheme.muted, marginBottom: 8 }}>
        {itemKind === "page"
          ? "Pick a notebook or page in the same project."
          : "Pick a page in the same project."}{" "}
        You need contributor access to the destination.
      </div>

      <TextInput
        // No visible label in this design, so the filter gets an aria-label
        // naming what it narrows — previously it announced as "edit, blank".
        aria-label="Filter destinations"
        // Focus opens here rather than on the close button: typing to narrow
        // the tree is the first thing this dialog is for.
        data-autofocus
        value={query}
        onChange={(e) => setQuery(e.currentTarget.value)}
        placeholder="Filter destinations…"
        leftSection={
          <i
            className="fa fa-magnifying-glass"
            style={{ fontSize: 10.5, color: notesTheme.muted }}
          />
        }
        leftSectionWidth={26}
        leftSectionPointerEvents="none"
        styles={{
          input: {
            border: `1px solid ${notesTheme.border}`,
            borderRadius: 4,
            padding: "6px 10px 6px 26px",
            fontSize: 11.5,
            fontFamily: "inherit",
            color: notesTheme.dark,
            background: "#fff",
            minHeight: 0,
            height: "auto"
          }
        }}
      />

      {/* The negative inline margin restores the list's original 8px gutter
          under the shell body's 20px padding, and the explicit maxHeight
          keeps the tree scrolling inside a bounded region instead of
          stretching the dialog the way the old 80vh panel prevented. */}
      <div
        style={{
          marginTop: 12,
          marginInline: -20,
          padding: "0 8px",
          overflowY: "auto",
          minHeight: 200,
          maxHeight: "46vh"
        }}
      >
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
            marginTop: 12,
            padding: "8px 10px",
            background: "#fee",
            border: `1px solid ${notesTheme.danger}`,
            borderRadius: 4,
            color: notesTheme.danger,
            fontSize: 12
          }}
        >
          <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
          {error}
        </div>
      )}
    </NotesModal>
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
      // The row's selected state was carried by background colour and a
      // check glyph only; aria-pressed states it for a screen reader.
      aria-pressed={selected}
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
