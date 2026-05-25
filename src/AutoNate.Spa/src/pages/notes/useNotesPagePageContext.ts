import { useCallback, useMemo, useRef } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import type {
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";

const PAGE_KEY = "notes";
const SCHEMA_VERSION = 1;

// What NotesPage exposes to the chatbot:
//   - Which project / cabinet / notebook the user has open in the rail.
//   - Which page is active (if any) and the open tab strip per page.
//
// What the chatbot can DO on this page (Phase 3 v1):
//   - Nothing imperative. BlockNote bodies live in the Yjs collab session;
//     mutating them safely via apply_page_action requires routing through
//     the editor's Yjs binding, which is a follow-up. Today the agent uses
//     ManageNotesSkill to create new pages from markdown and to mutate page
//     metadata (rename/move/archive); the snapshot below lets it default-
//     fill notebook / parent-page ids without re-asking.
//
// To extend in a future phase: add `replace_blocks_from_markdown` /
// `append_blocks_from_markdown` actions whose handler grabs the editor for
// `activePageId` and calls BlockNote's tryParseMarkdownToBlocks +
// editor.replaceBlocks / insertBlocks. Keep the converter strictly client-
// side for already-open pages so the Yjs CRDT stays authoritative.
type Options = {
  activeProjectId: string | null;
  activeCabinetId: string | null;
  activePageId: string | null;
  projects: Array<{ id: string; name: string }>;
  cabinets: Array<{ id: string; projectId: string; name: string }>;
  notebooks: Array<{ id: string; cabinetId: string; name: string }>;
  pages: Array<{ id: string; notebookId: string; parentPageId: string | null; title: string }>;
};

export function useNotesPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    const project = o.projects.find((p) => p.id === o.activeProjectId) ?? null;
    const cabinet = o.cabinets.find((c) => c.id === o.activeCabinetId) ?? null;
    const activePage = o.pages.find((p) => p.id === o.activePageId) ?? null;
    const activeNotebook = activePage
      ? o.notebooks.find((n) => n.id === activePage.notebookId) ?? null
      : null;

    const summaryParts = [
      project ? `project '${project.name}'` : "no project",
      cabinet ? `cabinet '${cabinet.name}'` : null,
      activeNotebook ? `notebook '${activeNotebook.name}'` : null,
      activePage ? `page '${activePage.title}'` : "no page open"
    ].filter(Boolean);
    const summary = `NotesPage · ${summaryParts.join(" · ")}`;

    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version:
        (o.activeProjectId?.length ?? 0)
        + (o.activeCabinetId?.length ?? 0)
        + (o.activePageId?.length ?? 0)
        + o.cabinets.length
        + o.notebooks.length
        + o.pages.length,
      data: {
        activeProject: project,
        activeCabinet: cabinet,
        activeNotebook,
        activePage,
        cabinetsInProject: o.cabinets.filter((c) => c.projectId === o.activeProjectId),
        notebooksInCabinet: o.notebooks.filter((n) => n.cabinetId === o.activeCabinetId),
        pagesInActiveNotebook: activeNotebook
          ? o.pages.filter((p) => p.notebookId === activeNotebook.id)
          : []
      }
    };
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    switch (req.topic) {
      case "selection.live": {
        const o = optsRef.current;
        return {
          ok: true,
          data: {
            activeProjectId: o.activeProjectId,
            activeCabinetId: o.activeCabinetId,
            activePageId: o.activePageId
          }
        };
      }
      default:
        return { ok: false, error: "unknown_topic", message: `NotesPage does not handle topic '${req.topic}'.` };
    }
  }, []);

  const entry = useMemo<PageContextProviderEntry>(
    () => ({
      pageKey: PAGE_KEY,
      getSnapshot,
      onPageQuery
    }),
    [getSnapshot, onPageQuery]
  );

  useRegisterPageContext(entry);
}
