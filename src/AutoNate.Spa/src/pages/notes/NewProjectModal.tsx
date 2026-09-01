import { useId, useState } from "react";
import { TextInput } from "@mantine/core";
import { useMe } from "@/hooks/useMe";
import {
  NotesGroupLabel,
  NotesModal,
  btnGhostStyle,
  btnPrimaryStyle,
  notesInputStyles
} from "./NotesModal";
import { notesTheme } from "./notesTheme";

type Props = {
  onClose: () => void;
  onCreate: (vars: { name: string; description?: string }) => void;
  submitting?: boolean;
};

export function NewProjectModal({ onClose, onCreate, submitting }: Props) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const ownerLabelId = useId();
  const meQuery = useMe();
  const me = meQuery.data?.authenticated ? meQuery.data : null;
  const ownerLabel = me
    ? `${[me.firstName, me.lastName].filter(Boolean).join(" ").trim() || me.username}`
    : "You";

  const submit = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    onCreate({
      name: trimmed,
      description: description.trim() || undefined
    });
  };

  return (
    <NotesModal
      onClose={onClose}
      title="New project"
      icon="fa-folder-tree"
      width="min(520px, 100%)"
      busy={submitting}
      footer={
        <>
          <button type="button" onClick={onClose} style={btnGhostStyle}>
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!name.trim() || submitting}
            style={{
              ...btnPrimaryStyle,
              opacity: !name.trim() || submitting ? 0.5 : 1,
              cursor: !name.trim() || submitting ? "not-allowed" : "pointer"
            }}
          >
            <i className="fa fa-plus" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Creating…" : "Create project"}
          </button>
        </>
      }
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <TextInput
          label="Name"
          // Focus lands here rather than on the close button: naming the
          // project is what the dialog is for, and the owner is fixed.
          data-autofocus
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          onKeyDown={(e) => {
            // Escape is handled by the dialog itself; only Enter-to-submit is
            // this field's business.
            if (e.key === "Enter") submit();
          }}
          placeholder="Acme launch"
          styles={notesInputStyles}
        />

        <TextInput
          label="Description (optional)"
          value={description}
          onChange={(e) => setDescription(e.currentTarget.value)}
          placeholder="Short description shown under the project name"
          styles={notesInputStyles}
        />

        <div>
          {/* Owner is a read-only readout, not a control, so the micro-label
              names the block via aria-labelledby instead of a <label>. */}
          <NotesGroupLabel id={ownerLabelId}>Owner</NotesGroupLabel>
          <div
            role="group"
            aria-labelledby={ownerLabelId}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "8px 12px",
              background: "#f8f9fa",
              fontSize: 13,
              color: notesTheme.dark
            }}
          >
            <i className="fa fa-user" style={{ color: notesTheme.muted, fontSize: 12 }} />
            <span>{ownerLabel}</span>
            <span style={{ marginLeft: "auto", fontSize: 11, color: notesTheme.muted }}>
              You become the project owner
            </span>
          </div>
        </div>
      </div>
    </NotesModal>
  );
}
