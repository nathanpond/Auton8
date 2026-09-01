import { NotesModal, btnGhostStyle, btnPrimaryStyle } from "./NotesModal";
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
// Matches the design language of the other notes modals — it shares the
// NotesModal shell, so it also gets role="dialog", a focus trap, Escape, and
// focus return.
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
  const confirmBg = destructive ? notesTheme.danger : notesTheme.primary;

  return (
    <NotesModal
      onClose={onCancel}
      title={title}
      icon={icon}
      iconColor={confirmBg}
      width="min(420px, 100%)"
      busy={busy}
      footer={
        <>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            style={{
              ...btnGhostStyle,
              cursor: busy ? "not-allowed" : "pointer",
              opacity: busy ? 0.6 : 1
            }}
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            // Focus lands on the confirm button rather than the close button:
            // this dialog exists to answer one question, and Enter then
            // confirms it the way the old document-level key handler did.
            data-autofocus
            style={{
              ...btnPrimaryStyle,
              // Destructive confirmations keep their own danger colour.
              background: confirmBg,
              border: `1px solid ${confirmBg}`,
              cursor: busy ? "not-allowed" : "pointer",
              opacity: busy ? 0.6 : 1
            }}
          >
            {busy ? "Working…" : confirmLabel}
          </button>
        </>
      }
    >
      <div style={{ fontSize: 12.5, color: notesTheme.dark, lineHeight: 1.55 }}>
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
    </NotesModal>
  );
}
