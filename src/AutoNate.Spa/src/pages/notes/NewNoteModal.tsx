import { useState } from "react";
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

  const submit = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    onCreate({ name: trimmed, kind });
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
          width: "min(640px, 100%)",
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
            <i className="fa fa-file-circle-plus" style={{ color: notesTheme.primary }} />
            New note
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

        <div style={{ padding: 20 }}>
          <Label>Type</Label>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr 1fr",
              gap: 10,
              marginBottom: 18
            }}
          >
            {(["richtext", "drawing", "diagram"] as const).map((k) => (
              <KindCard
                key={k}
                kindId={k}
                active={kind === k}
                onClick={() => setKind(k)}
              />
            ))}
          </div>

          <Label>Name</Label>
          <input
            value={name}
            autoFocus
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit();
              if (e.key === "Escape") onClose();
            }}
            placeholder={DEFAULT_NAME[kind]}
            style={{
              width: "100%",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "8px 12px",
              fontSize: 13,
              fontFamily: "inherit",
              outline: "none"
            }}
          />
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
        </div>
      </div>
    </div>
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

const btnGhostStyle: React.CSSProperties = {
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

const btnPrimaryStyle: React.CSSProperties = {
  background: notesTheme.primary,
  border: `1px solid ${notesTheme.primary}`,
  borderRadius: 4,
  padding: "6px 14px",
  fontSize: 12,
  fontWeight: 700,
  color: "#fff",
  fontFamily: "inherit"
};
