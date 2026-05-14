import { useEffect, useState } from "react";
import { NotebookDto } from "@/api/content";
import { FaIconPicker } from "./FaIconPicker";
import { notesTheme } from "./notesTheme";

type Props = {
  notebook: NotebookDto;
  onClose: () => void;
  onSave: (vars: {
    name: string;
    description: string | null;
    icon: string | null;
  }) => void;
  submitting?: boolean;
};

export function EditNotebookModal({ notebook, onClose, onSave, submitting }: Props) {
  const [name, setName] = useState(notebook.name);
  const [description, setDescription] = useState(notebook.description ?? "");
  const [icon, setIcon] = useState<string>(notebook.icon ?? "fa-book");

  useEffect(() => {
    setName(notebook.name);
    setDescription(notebook.description ?? "");
    setIcon(notebook.icon ?? "fa-book");
  }, [notebook.id]);

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
    <div
      onClick={onClose}
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 200,
        background: "rgba(32, 37, 42, 0.55)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: 20,
        animation: "notesFadeIn 140ms ease"
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "min(520px, 100%)",
          background: "#fff",
          borderRadius: 6,
          boxShadow: "0 22px 60px -12px rgba(0,0,0,0.35)",
          display: "flex",
          flexDirection: "column",
          fontFamily: "inherit",
          animation: "notesPopIn 180ms cubic-bezier(.2,.9,.3,1.2)"
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "14px 18px",
            borderBottom: `1px solid ${notesTheme.border}`
          }}
        >
          <h3
            style={{
              margin: 0,
              fontSize: 14,
              fontWeight: 700,
              color: notesTheme.dark,
              display: "flex",
              alignItems: "center",
              gap: 8
            }}
          >
            <i className="fa fa-pen-to-square" style={{ color: notesTheme.primary }} />
            Edit notebook
          </h3>
          <button
            type="button"
            onClick={onClose}
            style={{
              width: 28,
              height: 28,
              border: "none",
              background: "transparent",
              borderRadius: 3,
              color: notesTheme.muted,
              cursor: "pointer",
              fontSize: 14
            }}
          >
            <i className="fa fa-xmark" />
          </button>
        </div>

        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
          <div>
            <Label>Name</Label>
            <input
              autoFocus
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") submit();
                if (e.key === "Escape") onClose();
              }}
              style={inputStyle}
            />
          </div>

          <div>
            <Label>Description</Label>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What this notebook collects"
              style={inputStyle}
            />
          </div>

          <div>
            <Label>Icon</Label>
            <FaIconPicker value={icon} onChange={setIcon} />
          </div>
        </div>

        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: 8,
            padding: "12px 16px",
            borderTop: `1px solid ${notesTheme.border}`,
            background: "#f8f9fa"
          }}
        >
          <button type="button" onClick={onClose} style={btnGhost}>
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!name.trim() || submitting}
            style={{
              ...btnPrimary,
              opacity: !name.trim() || submitting ? 0.5 : 1,
              cursor: !name.trim() || submitting ? "not-allowed" : "pointer"
            }}
          >
            <i className="fa fa-check" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Saving…" : "Save changes"}
          </button>
        </div>
      </div>
    </div>
  );
}

function Label({ children }: { children: React.ReactNode }) {
  return (
    <div
      style={{
        fontSize: 10.5,
        fontWeight: 700,
        color: notesTheme.muted,
        textTransform: "uppercase",
        letterSpacing: "0.06em",
        marginBottom: 6
      }}
    >
      {children}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  border: `1px solid ${notesTheme.border}`,
  borderRadius: 4,
  padding: "8px 12px",
  fontSize: 13,
  fontFamily: "inherit",
  outline: "none"
};

const btnGhost: React.CSSProperties = {
  background: "#fff",
  border: `1px solid ${notesTheme.border}`,
  borderRadius: 4,
  padding: "6px 14px",
  fontSize: 12,
  fontWeight: 700,
  color: notesTheme.dark,
  cursor: "pointer",
  fontFamily: "inherit"
};

const btnPrimary: React.CSSProperties = {
  background: notesTheme.primary,
  border: `1px solid ${notesTheme.primary}`,
  borderRadius: 4,
  padding: "6px 14px",
  fontSize: 12,
  fontWeight: 700,
  color: "#fff",
  fontFamily: "inherit"
};
