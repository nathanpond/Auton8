import { useCallback, useMemo, useRef } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import type {
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";
import { getPageBodyEditor } from "@/lib/blocknote/pageBodyEditorRegistry";

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
          : [],
        // Topics the model can fetch via query_page. Listing them in the
        // snapshot is what lets the model discover them without us having
        // to bake them into the system prompt.
        queryTopics: [
          {
            topic: "page_body",
            description:
              "Live BlockNote body of the currently open page (or a specified pageId), including unsaved edits. args: { format?: 'markdown'|'blocks'|'text' = 'markdown', pageId?: string }."
          },
          {
            topic: "selection.live",
            description: "Latest activeProjectId / activeCabinetId / activePageId selections."
          }
        ]
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
      case "page_body": {
        // Read the live BlockNote document for the active page — including
        // any unsaved edits. Optional args.format = "markdown" | "blocks"
        // (default "markdown") trades fidelity (blocks JSON) for size and
        // model-friendliness (markdown). Optional args.pageId targets a
        // specific page; defaults to the active page.
        const o = optsRef.current;
        const format =
          typeof req.args?.format === "string"
            ? (req.args.format as "markdown" | "blocks" | "text")
            : "markdown";
        const targetPageId =
          typeof req.args?.pageId === "string" ? req.args.pageId : o.activePageId;
        if (!targetPageId) {
          return { ok: false, error: "no_active_page", message: "No page is currently open in the editor." };
        }
        const editor = getPageBodyEditor(targetPageId);
        if (!editor) {
          return {
            ok: false,
            error: "editor_unavailable",
            message: `No live editor is mounted for page '${targetPageId}'. Open the page in the Notes tab and retry, or fetch the saved body via the records API.`
          };
        }
        const blocks = editor.document;
        if (format === "blocks") {
          return { ok: true, data: { pageId: targetPageId, format, blocks } };
        }
        if (format === "text") {
          // Flatten to plain text — strips formatting, useful for token-tight
          // summarisation prompts.
          const text = await editor.blocksToMarkdownLossy(blocks);
          return { ok: true, data: { pageId: targetPageId, format, text } };
        }
        // Default: markdown.
        const markdown = await editor.blocksToMarkdownLossy(blocks);
        return { ok: true, data: { pageId: targetPageId, format: "markdown", markdown } };
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
