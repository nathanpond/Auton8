import { useId, useState } from "react";
import { TextInput } from "@mantine/core";
import { FaIconPicker } from "./FaIconPicker";
import {
  NotesGroupLabel,
  NotesModal,
  btnGhostStyle,
  btnPrimaryStyle,
  notesInputStyles
} from "./NotesModal";
import { notesTheme } from "./notesTheme";

type Props = {
  cabinetName: string;
  onClose: () => void;
  onCreate: (vars: { name: string; description?: string; icon?: string }) => void;
  submitting?: boolean;
};

export function NewNotebookModal({ cabinetName, onClose, onCreate, submitting }: Props) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [icon, setIcon] = useState<string>("fa-book");
  const iconLabelId = useId();

  const submit = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    onCreate({
      name: trimmed,
      description: description.trim() || undefined,
      icon
    });
  };

  return (
    <NotesModal
      onClose={onClose}
      title={
        <>
          New notebook in <span style={{ color: notesTheme.muted }}>{cabinetName}</span>
        </>
      }
      icon="fa-book-medical"
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
            {submitting ? "Creating…" : "Create notebook"}
          </button>
        </>
      }
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <TextInput
          label="Name"
          // Focus lands here rather than on the close button: naming the
          // notebook is what the dialog is for, and every other field has a
          // usable default.
          data-autofocus
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          onKeyDown={(e) => {
            // Escape is handled by the dialog itself; only Enter-to-submit is
            // this field's business.
            if (e.key === "Enter") submit();
          }}
          placeholder="Service Department"
          styles={notesInputStyles}
        />

        <TextInput
          label="Description (optional)"
          value={description}
          onChange={(e) => setDescription(e.currentTarget.value)}
          placeholder="What this notebook collects"
          styles={notesInputStyles}
        />

        <div>
          {/* The icon picker is a grid of buttons plus a search box, not one
              input, so the micro-label names a group rather than a control. */}
          <NotesGroupLabel id={iconLabelId}>Icon</NotesGroupLabel>
          <div role="group" aria-labelledby={iconLabelId}>
            <FaIconPicker value={icon} onChange={setIcon} />
          </div>
        </div>
      </div>
    </NotesModal>
  );
}
