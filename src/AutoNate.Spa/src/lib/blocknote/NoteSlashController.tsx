import { useMemo } from "react";
import { filterSuggestionItems, type BlockNoteEditor } from "@blocknote/core";
import {
  SuggestionMenuController,
  getDefaultReactSlashMenuItems,
  useBlockNoteEditor,
  type DefaultReactSuggestionItem
} from "@blocknote/react";
import { useNotes } from "@/hooks/useContent";
import type { NoteDto, NoteKind } from "@/api/content";
import { NOTE_KIND_META } from "@/pages/notes/notesTheme";
import { NOTE_EMBED_BLOCK_TYPE, pageBlockNoteSchema } from "./pageSchema";

// Helper alias: BlockNote's `getDefaultReactSlashMenuItems` and
// `editor.insertBlocks` are typed against the structural
// `BlockNoteEditor<Record<string, BlockConfig<...>>>` shape. Our concrete
// schema is narrower, and TS rejects the cross-variance even though the
// runtime accepts it. Cast through this alias at the call sites to keep
// the noise contained.
type AnyBlockNoteEditor = BlockNoteEditor<any, any, any>;

// Wires the `/` slash menu so it includes one item per note attached to
// the current page, in addition to BlockNote's default items. Mounted as
// a child of <BlockNoteView> by YjsEditor only when the doc is a page
// (`page:<id>`); never appears for richtext-note editors.
//
// Architecture note: `getItems` cannot itself call hooks (it's a plain
// callback). We close over the React-Query result by computing
// `customItems` at component render time, so a notes-list refresh
// (new note added on another tab, picker swap) triggers a re-render
// of this controller and rebuilds the closure with fresh items.
export function NoteSlashController({ pageId }: { pageId: string }) {
  // Pass the page schema so the editor type knows about `noteEmbed` —
  // otherwise `editor.insertBlocks` would reject our custom block type
  // at compile time. The schema arg is for type inference only; the
  // hook reads the actual instance from React context.
  const editor = useBlockNoteEditor(pageBlockNoteSchema);
  const notesQuery = useNotes(pageId);

  const customItems = useMemo<DefaultReactSuggestionItem[]>(() => {
    const notes = notesQuery.data ?? [];
    return notes.map((note) =>
      buildNoteSlashItem(note, editor as unknown as AnyBlockNoteEditor)
    );
  }, [notesQuery.data, editor]);

  return (
    <SuggestionMenuController
      triggerCharacter="/"
      // Matches the default-UI controller we suppress in YjsEditor: don't pop
      // the slash menu inside table cells (typing `/` there is almost always
      // literal text, and the dropdown would obscure the cell).
      shouldOpen={(state) =>
        !state.selection.$from.parent.type.isInGroup("tableContent")
      }
      getItems={async (query) => {
        // Switch on the literal prefix `note` (case-insensitive, optional
        // separator + subquery). When the user types `/note <name>`, show
        // only note items filtered by <name>. Otherwise show the default
        // slash items filtered by the full query — keeping `/note` from
        // colliding with other commands the user might be typing (e.g.
        // `/numbered`).
        const noteCmd = /^note(?:[\s.]+(.*))?$/i.exec(query);
        if (noteCmd) {
          const subquery = (noteCmd[1] ?? "").trim();
          return filterSuggestionItems(customItems, subquery);
        }
        return filterSuggestionItems(
          getDefaultReactSlashMenuItems(editor as unknown as AnyBlockNoteEditor),
          query
        );
      }}
    />
  );
}

function buildNoteSlashItem(
  note: NoteDto,
  editor: AnyBlockNoteEditor
): DefaultReactSuggestionItem {
  const meta = NOTE_KIND_META[note.noteKind as NoteKind];
  const title = note.title?.trim() || "Untitled note";
  return {
    title,
    subtext: `${meta?.label ?? note.noteKind} · embed in this page`,
    group: "Notes on this page",
    // Searchable on kind alongside title — typing "/diagram" or
    // "/drawing" surfaces the matching notes without needing the user
    // to remember exact titles.
    aliases: [meta?.label.toLowerCase() ?? note.noteKind, note.noteKind],
    icon: (
      <i
        className={`fa ${meta?.icon ?? "fa-file-lines"}`}
        style={{ color: meta?.color, fontSize: 14 }}
      />
    ),
    onItemClick: () => {
      // BlockNote's SuggestionMenu wrapper runs `clearQuery()` (deleting
      // the `/note <name>` trigger text) BEFORE our handler fires, so
      // the cursor's block is now empty. Mirror the default-slash
      // pattern: if the current block is empty, REPLACE it with the
      // embed via updateBlock; otherwise insert AFTER. Leaving a stray
      // empty paragraph next to a custom leaf block was triggering
      // ProseMirror state cleanup that deleted the noteEmbed on the
      // next transaction (visible as the embed disappearing ~2s after
      // insert — see materializer diagnostic showing the Y.Doc lose
      // the noteEmbed container between auto-saves).
      const cursor = editor.getTextCursorPosition();
      const block = cursor.block as {
        content?: Array<{ type?: string; text?: string }> | string;
      };
      const isEmpty =
        Array.isArray(block.content) &&
        (block.content.length === 0 ||
          (block.content.length === 1 &&
            block.content[0]?.type === "text" &&
            (block.content[0] as { text?: string }).text === ""));
      if (isEmpty) {
        editor.updateBlock(cursor.block, {
          type: NOTE_EMBED_BLOCK_TYPE,
          props: { noteId: note.id }
        });
      } else {
        editor.insertBlocks(
          [
            {
              type: NOTE_EMBED_BLOCK_TYPE,
              props: { noteId: note.id }
            }
          ],
          cursor.block,
          "after"
        );
      }
    }
  };
}
