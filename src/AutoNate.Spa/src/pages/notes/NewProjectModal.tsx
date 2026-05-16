import { useState } from "react";
import { useMe } from "@/hooks/useMe";
import { notesTheme } from "./notesTheme";

type Props = {
  onClose: () => void;
  onCreate: (vars: { name: string; description?: string }) => void;
  submitting?: boolean;
};

export function NewProjectModal({ onClose, onCreate, submitting }: Props) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
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
            <i className="fa fa-folder-tree" style={{ color: notesTheme.primary }} />
            New project
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
              placeholder="Acme launch"
              style={inputStyle}
            />
          </div>

          <div>
            <Label>Description (optional)</Label>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Short description shown under the project name"
              style={inputStyle}
            />
          </div>

          <div>
            <Label>Owner</Label>
            <div
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
            <i className="fa fa-plus" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Creating…" : "Create project"}
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
