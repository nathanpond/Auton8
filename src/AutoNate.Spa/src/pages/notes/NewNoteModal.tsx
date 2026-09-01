import { useId, useState } from "react";
import { TextInput } from "@mantine/core";
import {
  NotesGroupLabel,
  NotesModal,
  btnGhostStyle,
  btnPrimaryStyle,
  notesInputStyles
} from "./NotesModal";
import { NOTE_KIND_META, WireNoteKind, notesTheme } from "./notesTheme";

type Props = {
  onClose: () => void;
  onCreate: (vars: { name: string; kind: WireNoteKind }) => void;
  submitting?: boolean;
};

const DEFAULT_NAME: Record<WireNoteKind, string> = {
  richtext: "Untitled note",
  drawing: "Untitled sketch",
  diagram: "Untitled diagram"
};

export function NewNoteModal({ onClose, onCreate, submitting }: Props) {
  const [kind, setKind] = useState<WireNoteKind>("richtext");
  const [name, setName] = useState("");
  const kindLabelId = useId();

  const submit = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    onCreate({ name: trimmed, kind });
  };

  return (
    <NotesModal
      onClose={onClose}
      title="New note"
      icon="fa-file-circle-plus"
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
            {submitting ? "Creating…" : "Create note"}
          </button>
        </>
      }
    >
      <NotesGroupLabel id={kindLabelId}>Type</NotesGroupLabel>
      <div
        role="group"
        aria-labelledby={kindLabelId}
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr 1fr",
          gap: 10,
          marginBottom: 18
        }}
      >
        {(["richtext", "drawing", "diagram"] as const).map((k) => (
          <KindCard key={k} kindId={k} active={kind === k} onClick={() => setKind(k)} />
        ))}
      </div>

      <TextInput
        label="Name"
        // Focus lands here rather than on the close button: naming the note is
        // what the dialog is for, and the kind picker has a working default.
        data-autofocus
        value={name}
        onChange={(e) => setName(e.currentTarget.value)}
        onKeyDown={(e) => {
          // Escape is handled by the dialog itself; only Enter-to-submit is
          // this field's business.
          if (e.key === "Enter") submit();
        }}
        placeholder={DEFAULT_NAME[kind]}
        styles={notesInputStyles}
      />
    </NotesModal>
  );
}

function KindCard({
  kindId,
  active,
  onClick
}: {
  kindId: WireNoteKind;
  active: boolean;
  onClick: () => void;
}) {
  const meta = NOTE_KIND_META[kindId];
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      style={{
        textAlign: "left",
        cursor: "pointer",
        fontFamily: "inherit",
        background: active ? "#fff" : "#fafbfc",
        border: `2px solid ${active ? meta.color : notesTheme.border}`,
        borderRadius: 6,
        padding: "14px 12px",
        display: "flex",
        flexDirection: "column",
        gap: 10,
        boxShadow: active ? `0 0 0 4px ${meta.color}20` : "none",
        transition: "all 120ms"
      }}
    >
      <div
        style={{
          width: 38,
          height: 38,
          borderRadius: 7,
          background: meta.color + "20",
          color: meta.color,
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          fontSize: 16
        }}
      >
        <i className={`fa ${meta.icon}`} />
      </div>
      <div>
        <div
          style={{
            fontSize: 13,
            fontWeight: 700,
            color: notesTheme.dark,
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between"
          }}
        >
          {meta.label}
          {active && <i className="fa fa-check" style={{ color: meta.color, fontSize: 11 }} />}
        </div>
        <div
          style={{
            fontSize: 10,
            color: notesTheme.muted,
            marginTop: 2,
            fontWeight: 700,
            textTransform: "uppercase",
            letterSpacing: "0.05em"
          }}
        >
          {meta.tech}
        </div>
      </div>
      <div style={{ fontSize: 11.5, color: notesTheme.muted, lineHeight: 1.4 }}>
        {meta.description}
      </div>
    </button>
  );
}
