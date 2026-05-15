import { useEffect, useRef, useState } from "react";
import { notesTheme } from "./notesTheme";

type Props = {
  value: string;
  readOnly?: boolean;
  // Persisted only when the new value is non-empty AND differs from `value`.
  // Empty or whitespace-only inputs silently revert to the previous value
  // on commit; the caller never has to handle empty-string saves.
  onSave: (next: string) => void;
  // Style overrides for both the rendered <h1> and the active <input> so a
  // single component can match the editor's typography (page-style large
  // title, note-editor-bar smaller title, etc).
  style?: React.CSSProperties;
};

// Click-to-rename inline editor for note titles. Renders an <h1> in display
// mode; clicking swaps it for a borderless <input> that auto-focuses and
// selects its text. Commit happens on blur or Enter; Escape cancels and
// reverts. Save is gated on a non-empty trimmed value that differs from the
// current — so casual clicks that don't change anything are silent no-ops.
//
// `readOnly` (true when viewing a revision) keeps the title as a plain
// <h1> with default cursor and no click handler.
export function EditableNoteTitle({ value, readOnly, onSave, style }: Props) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const inputRef = useRef<HTMLInputElement>(null);

  // While not editing, mirror the latest server-truth value into the draft
  // so the input shows the right starting text when the user clicks. Doing
  // this inside the editing branch would clobber in-progress typing the
  // moment the parent re-renders (e.g. from a sibling autosave invalidation).
  useEffect(() => {
    if (!editing) setDraft(value);
  }, [value, editing]);

  // Autofocus + select-all so the user can replace the name with a single
  // keystroke. Runs once per entry into edit mode.
  useEffect(() => {
    if (!editing) return;
    const input = inputRef.current;
    if (!input) return;
    input.focus();
    input.select();
  }, [editing]);

  const finish = (commit: boolean) => {
    setEditing(false);
    if (!commit) {
      setDraft(value);
      return;
    }
    const trimmed = draft.trim();
    if (!trimmed || trimmed === value) {
      setDraft(value);
      return;
    }
    onSave(trimmed);
  };

  if (editing) {
    return (
      <input
        ref={inputRef}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => finish(true)}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.preventDefault();
            finish(true);
          }
          if (e.key === "Escape") {
            e.preventDefault();
            finish(false);
          }
        }}
        style={{
          background: "transparent",
          border: "none",
          outline: "none",
          padding: 0,
          margin: 0,
          width: "100%",
          fontFamily: "inherit",
          color: notesTheme.dark,
          ...style
        }}
      />
    );
  }

  return (
    <h1
      onClick={() => {
        if (readOnly) return;
        setDraft(value);
        setEditing(true);
      }}
      title={readOnly ? undefined : "Click to rename"}
      style={{
        cursor: readOnly ? "default" : "text",
        margin: 0,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        ...style
      }}
    >
      {value}
    </h1>
  );
}
