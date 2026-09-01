import { useState } from "react";
import { TextInput } from "@mantine/core";
import { NotesModal, btnGhostStyle, btnPrimaryStyle, notesInputStyles } from "./NotesModal";
import { notesTheme } from "./notesTheme";

type Props = {
  parentLabel: string;          // notebook name or parent-page title
  parentKind: "notebook" | "page";
  onClose: () => void;
  onCreate: (vars: { title: string }) => void;
  submitting?: boolean;
};

export function NewPageModal({ parentLabel, parentKind, onClose, onCreate, submitting }: Props) {
  const [title, setTitle] = useState("");

  const submit = () => {
    const trimmed = title.trim();
    if (!trimmed) return;
    onCreate({ title: trimmed });
  };

  return (
    <NotesModal
      onClose={onClose}
      title={
        <>
          {parentKind === "page" ? "New sub-page in" : "New page in"}{" "}
          <span style={{ color: notesTheme.muted, fontWeight: 600 }}>{parentLabel}</span>
        </>
      }
      icon="fa-file-circle-plus"
      width="min(440px, 100%)"
      busy={submitting}
      footer={
        <>
          <button type="button" onClick={onClose} style={btnGhostStyle}>
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!title.trim() || submitting}
            style={{
              ...btnPrimaryStyle,
              opacity: !title.trim() || submitting ? 0.5 : 1,
              cursor: !title.trim() || submitting ? "not-allowed" : "pointer"
            }}
          >
            <i className="fa fa-plus" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Creating…" : "Create"}
          </button>
        </>
      }
    >
      <TextInput
        label="Title"
        // Focus lands here rather than on the close button: titling the page is
        // the dialog's only field, so there is nowhere else worth starting.
        data-autofocus
        value={title}
        onChange={(e) => setTitle(e.currentTarget.value)}
        onKeyDown={(e) => {
          // Escape is handled by the dialog itself; only Enter-to-submit is
          // this field's business.
          if (e.key === "Enter") submit();
        }}
        placeholder={parentKind === "page" ? "Untitled sub-page" : "Untitled page"}
        styles={notesInputStyles}
      />
    </NotesModal>
  );
}
