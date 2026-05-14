import { useState } from "react";
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
          width: "min(440px, 100%)",
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
            {parentKind === "page" ? "New sub-page in" : "New page in"}{" "}
            <span style={{ color: notesTheme.muted, fontWeight: 600 }}>{parentLabel}</span>
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
            Title
          </div>
          <input
            autoFocus
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit();
              if (e.key === "Escape") onClose();
            }}
            placeholder={parentKind === "page" ? "Untitled sub-page" : "Untitled page"}
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
          <button
            type="button"
            onClick={onClose}
            style={{
              background: "#fff",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: notesTheme.dark,
              cursor: "pointer",
              fontFamily: "inherit"
            }}
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!title.trim() || submitting}
            style={{
              background: notesTheme.primary,
              border: `1px solid ${notesTheme.primary}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: "#fff",
              fontFamily: "inherit",
              opacity: !title.trim() || submitting ? 0.5 : 1,
              cursor: !title.trim() || submitting ? "not-allowed" : "pointer"
            }}
          >
            <i className="fa fa-plus" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Creating…" : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}
