import { useMemo, useState } from "react";
import type { ThreadData } from "@blocknote/core/comments";
import { CommentsExtension } from "@blocknote/core/comments";
import {
  Thread,
  getReferenceText,
  useBlockNoteEditor,
  useExtension,
  useExtensionState,
  useThreads
} from "@blocknote/react";
import { ActionIcon, Tooltip } from "@mantine/core";
import { notesTheme } from "@/pages/notes/notesTheme";

// Custom threads sidebar with a two-section layout: open threads at the top
// (full BlockNote <Thread> UI), resolved threads below as collapsed rows
// (checkmark + reference text). Replaces BlockNote's stock <ThreadsSidebar>
// which only supports `filter="open" | "resolved" | "all"` with no
// per-state visual differentiation.
export function NotesThreadsSidebar() {
  const editor = useBlockNoteEditor<any, any, any>();
  const { selectedThreadId, threadPositions } = useExtensionState(CommentsExtension);
  const threadsMap = useThreads();

  const { openThreads, resolvedThreads } = useMemo(() => {
    const open: ThreadRow[] = [];
    const resolved: ThreadRow[] = [];
    for (const thread of threadsMap.values()) {
      const pos = threadPositions.get(thread.id);
      const row: ThreadRow = {
        thread,
        referenceText: getReferenceText(editor, pos),
        orphaned: pos === undefined
      };
      (thread.resolved ? resolved : open).push(row);
    }
    const byPosition = (a: ThreadRow, b: ThreadRow) => {
      const pa = threadPositions.get(a.thread.id)?.from ?? Number.MAX_VALUE;
      const pb = threadPositions.get(b.thread.id)?.from ?? Number.MAX_VALUE;
      return pa - pb;
    };
    open.sort(byPosition);
    resolved.sort(byPosition);
    return { openThreads: open, resolvedThreads: resolved };
  }, [threadsMap, threadPositions, editor]);

  return (
    <div className="bn-threads-sidebar">
      {openThreads.map((row) => (
        <Thread
          key={row.thread.id}
          thread={row.thread}
          selected={row.thread.id === selectedThreadId}
          orphaned={row.orphaned}
          referenceText={row.referenceText}
          tabIndex={0}
        />
      ))}

      {resolvedThreads.length > 0 && (
        <div
          style={{
            padding: "10px 14px 6px",
            fontSize: 11,
            fontWeight: 600,
            letterSpacing: "0.04em",
            textTransform: "uppercase",
            color: notesTheme.muted,
            borderTop: openThreads.length > 0 ? `1px solid ${notesTheme.border}` : undefined,
            marginTop: openThreads.length > 0 ? 8 : 0
          }}
        >
          Resolved · {resolvedThreads.length}
        </div>
      )}
      {resolvedThreads.map((row) => (
        <ResolvedThreadRow
          key={row.thread.id}
          row={row}
          selected={row.thread.id === selectedThreadId}
        />
      ))}
    </div>
  );
}

interface ThreadRow {
  thread: ThreadData;
  referenceText: string;
  orphaned: boolean;
}

function ResolvedThreadRow({
  row,
  selected
}: {
  row: ThreadRow;
  selected: boolean;
}) {
  const [expanded, setExpanded] = useState(false);
  const comments = useExtension(CommentsExtension);

  if (expanded) {
    // Wrap the Thread in a relative container so we can pin a small
    // "collapse" affordance over its top-right corner. BlockNote's own
    // hover-toolbar (resolve/menu/etc.) lives inside individual Comment
    // cards — our outer chevron sits above them and never conflicts.
    return (
      <div style={{ position: "relative" }}>
        <Tooltip label="Collapse" position="left">
          <ActionIcon
            variant="subtle"
            color="gray"
            size="sm"
            onClick={() => {
              setExpanded(false);
              comments.selectThread(undefined);
            }}
            aria-label="Collapse resolved thread"
            style={{ position: "absolute", top: 6, right: 6, zIndex: 5 }}
          >
            <i className="fa fa-chevron-up" />
          </ActionIcon>
        </Tooltip>
        <Thread
          thread={row.thread}
          selected={selected}
          orphaned={row.orphaned}
          referenceText={row.referenceText}
          tabIndex={0}
        />
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={() => {
        setExpanded(true);
        comments.selectThread(row.thread.id, true);
      }}
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: 10,
        width: "100%",
        padding: "10px 14px",
        background: "transparent",
        border: "none",
        borderTop: `1px solid ${notesTheme.border}`,
        textAlign: "left",
        cursor: "pointer",
        color: notesTheme.dark,
        opacity: 0.72
      }}
    >
      <i
        className="fa fa-circle-check"
        style={{ color: notesTheme.green, marginTop: 2, fontSize: 14 }}
        aria-hidden="true"
      />
      <span
        style={{
          flex: 1,
          minWidth: 0,
          fontSize: 13,
          fontWeight: 500,
          overflow: "hidden",
          textOverflow: "ellipsis",
          whiteSpace: "nowrap"
        }}
      >
        {row.referenceText || "(no reference text)"}
      </span>
    </button>
  );
}
