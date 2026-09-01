import { useEffect, useId, useState } from "react";
import { TextInput } from "@mantine/core";
import { CabinetDto } from "@/api/content";
import { FaIconPicker } from "./FaIconPicker";
import {
  NotesGroupLabel,
  NotesModal,
  btnGhostStyle,
  btnPrimaryStyle,
  notesInputStyles
} from "./NotesModal";

type Props = {
  cabinet: CabinetDto;
  onClose: () => void;
  onSave: (vars: {
    name: string;
    description: string | null;
    icon: string | null;
  }) => void;
  submitting?: boolean;
};

// Edit-cabinet modal. Mirrors NewCabinetModal but pre-fills fields and submits
// a partial PATCH containing only what changed.
export function EditCabinetModal({ cabinet, onClose, onSave, submitting }: Props) {
  const [name, setName] = useState(cabinet.name);
  const [description, setDescription] = useState(cabinet.description ?? "");
  const [icon, setIcon] = useState<string>(cabinet.icon ?? "fa-folder");
  const iconLabelId = useId();

  useEffect(() => {
    setName(cabinet.name);
    setDescription(cabinet.description ?? "");
    setIcon(cabinet.icon ?? "fa-folder");
  }, [cabinet.id]);

  const submit = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const trimmedDescription = description.trim();
    onSave({
      name: trimmed,
      description: trimmedDescription === "" ? null : trimmedDescription,
      icon: icon || null
    });
  };

  return (
    <NotesModal
      onClose={onClose}
      title="Edit cabinet"
      icon="fa-pen-to-square"
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
            <i className="fa fa-check" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Saving…" : "Save changes"}
          </button>
        </>
      }
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <TextInput
          label="Name"
          // Focus lands here rather than on the close button: renaming the
          // cabinet is the usual reason this dialog is opened, and the icon
          // picker already carries the cabinet's current value.
          data-autofocus
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          onKeyDown={(e) => {
            // Escape is handled by the dialog itself; only Enter-to-submit is
            // this field's business.
            if (e.key === "Enter") submit();
          }}
          styles={notesInputStyles}
        />

        <TextInput
          label="Description"
          value={description}
          onChange={(e) => setDescription(e.currentTarget.value)}
          placeholder="Short description shown under the cabinet name"
          styles={notesInputStyles}
        />

        <div>
          {/* The picker is a grid of buttons, not one control, so the label
              names the group instead of pretending to be a field label. */}
          <NotesGroupLabel id={iconLabelId}>Icon</NotesGroupLabel>
          <div role="group" aria-labelledby={iconLabelId}>
            <FaIconPicker value={icon} onChange={setIcon} />
          </div>
        </div>
      </div>
    </NotesModal>
  );
}
