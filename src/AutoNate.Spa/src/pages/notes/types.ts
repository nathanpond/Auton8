import { NoteDto, NotebookDto, PageTreeNodeDto } from "@/api/content";
import { WireNoteKind } from "./notesTheme";

// In-tree representation derived from PageTreeNodeDto[] — parents adopt their
// children as an array so the explorer can render the hierarchy recursively
// without per-node lookups.
export type PageTreeNode = PageTreeNodeDto & {
  children: PageTreeNode[];
};

export type NotebookWithPages = NotebookDto & {
  pages: PageTreeNode[];
};

// Tabs in the editor pane: one "page" tab anchored to the active page, plus
// one tab per note belonging to that page. The page tab is not closable.
export type EditorTab =
  | { id: string; kind: "page"; name: string }
  | { id: string; kind: WireNoteKind; name: string; noteId: string };

export function nodeMatchesPage(node: PageTreeNode, pageId: string): boolean {
  if (node.id === pageId) return true;
  return node.children.some((c) => nodeMatchesPage(c, pageId));
}

export function flattenToTree(rows: PageTreeNodeDto[]): PageTreeNode[] {
  const byId = new Map<string, PageTreeNode>();
  for (const r of rows) byId.set(r.id, { ...r, children: [] });
  const roots: PageTreeNode[] = [];
  for (const r of rows) {
    const node = byId.get(r.id)!;
    if (r.parentPageId && byId.has(r.parentPageId)) {
      byId.get(r.parentPageId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }
  const sortRecursive = (list: PageTreeNode[]) => {
    list.sort((a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title));
    list.forEach((n) => sortRecursive(n.children));
  };
  sortRecursive(roots);
  return roots;
}

// Minimal shape needed for the tab strip + URL routing. Both NoteDto
// (REST) and PageNoteMetadata (Yjs `useYjsNotesList`) satisfy it.
type NoteTabSource = Pick<NoteDto, "id" | "noteKind" | "title" | "pageNoteIndex">;

export function tabsForPage(
  pageId: string,
  pageName: string,
  notes: readonly NoteTabSource[]
): EditorTab[] {
  const pageTab: EditorTab = { id: `${pageId}::page`, kind: "page", name: pageName };
  const noteTabs: EditorTab[] = notes.map((n) => ({
    id: `${pageId}::${n.id}`,
    kind: n.noteKind,
    name: n.title ?? "Untitled",
    noteId: n.id
  }));
  return [pageTab, ...noteTabs];
}
