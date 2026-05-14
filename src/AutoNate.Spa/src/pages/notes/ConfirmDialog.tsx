import { useEffect } from "react";
import { notesTheme } from "./notesTheme";

type Props = {
  icon?: string;
  title: string;
  body: React.ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  destructive?: boolean;
  busy?: boolean;
  error?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
};

// Small modal used for destructive confirmations (delete cabinet/notebook/etc).
// Matches the design language of the other notes modals — same backdrop, same
// pop-in animation, same footer button styling.
export function ConfirmDialog({
  icon = "fa-triangle-exclamation",
  title,
  body,
  confirmLabel,
  cancelLabel = "Cancel",
  destructive,
  busy,
  error,
  onConfirm,
  onCancel
}: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
      if (e.key === "Enter") onConfirm();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onCancel, onConfirm]);

  const confirmBg = destructive ? notesTheme.danger : notesTheme.primary;
  return (
    <div
      onClick={onCancel}
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 220,
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
          width: "min(420px, 100%)",
          background: "#fff",
          borderRadius: 6,
          boxShadow: "0 22px 60px -12px rgba(0,0,0,0.35)",
          fontFamily: "inherit",
          animation: "notesPopIn 180ms cubic-bezier(.2,.9,.3,1.2)"
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            padding: "16px 18px",
            borderBottom: `1px solid ${notesTheme.border}`
          }}
        >
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: 6,
              background: (destructive ? notesTheme.danger : notesTheme.primary) + "20",
              color: destructive ? notesTheme.danger : notesTheme.primary,
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              fontSize: 14,
              flexShrink: 0
            }}
          >
            <i className={`fa ${icon}`} />
          </div>
          <h3
            style={{
              margin: 0,
              fontSize: 14,
              fontWeight: 700,
              color: notesTheme.dark
            }}
          >
            {title}
          </h3>
        </div>

        <div
          style={{
            padding: 18,
            fontSize: 12.5,
            color: notesTheme.dark,
            lineHeight: 1.55
          }}
        >
          {body}
          {error && (
            <div
              style={{
                marginTop: 12,
                padding: "8px 10px",
                background: "#fee",
                border: `1px solid ${notesTheme.danger}`,
                borderRadius: 4,
                color: notesTheme.danger,
                fontSize: 12
              }}
            >
              <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
              {error}
            </div>
          )}
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
            onClick={onCancel}
            disabled={busy}
            style={{
              background: "#fff",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: notesTheme.dark,
              cursor: busy ? "not-allowed" : "pointer",
              fontFamily: "inherit",
              opacity: busy ? 0.6 : 1
            }}
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            style={{
              background: confirmBg,
              border: `1px solid ${confirmBg}`,
              borderRadius: 4,
              padding: "6px 14px",
              fontSize: 12,
              fontWeight: 700,
              color: "#fff",
              fontFamily: "inherit",
              cursor: busy ? "not-allowed" : "pointer",
              opacity: busy ? 0.6 : 1
            }}
          >
            {busy ? "Working…" : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
