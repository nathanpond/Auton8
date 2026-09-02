import { ReactNode, useEffect, useRef } from "react";
import { Modal } from "@mantine/core";
import { notesTheme } from "./notesTheme";

// Shared dialog shell for every Notes modal (archived-8).
//
// The twelve Notes dialogs were each a hand-rolled `<div onClick={onClose}>`
// overlay wrapping a `<div onClick={stopPropagation}>` panel. That renders no
// dialog role, so a screen reader announced nothing; it trapped no focus, so
// Tab walked straight out into the page behind; and it returned no focus, so
// closing dropped the user on <body>. Creating, renaming, moving and deleting
// notes were all unreachable without a mouse.
//
// This wraps Mantine's compound Modal, which supplies role="dialog",
// aria-modal, aria-labelledby wired to Modal.Title, a focus trap, Escape, and
// focus return — the whole contract — while the compound parts let the Notes
// chrome keep its own look. A plain <Modal> would have imposed Mantine's
// header and padding on a module that deliberately styles itself from
// `notesTheme` to match the design prototype.
//
// Initial focus: Mantine focuses the first focusable element, which is the
// close button. Put `data-autofocus` on the field a dialog opens for (the
// name input, usually) so focus lands somewhere useful instead.

type Props = {
  onClose: () => void;
  // Rendered as Modal.Title (an <h2>), which becomes the dialog's accessible
  // name. Always provide one — an unnamed dialog announces as just "dialog".
  title: ReactNode;
  // Font Awesome class for the glyph beside the title, e.g. "fa-file-circle-plus".
  icon?: string;
  iconColor?: string;
  // Any valid CSS width. Matches the `width: min(Npx, 100%)` the hand-rolled
  // panels used.
  width?: string | number;
  // Sticky footer strip below the body — the Cancel / confirm row.
  footer?: ReactNode;
  // Set while a submit is in flight so the dialog cannot be dismissed out
  // from under a request that is already on the wire.
  busy?: boolean;
  children: ReactNode;
};

export function NotesModal({
  onClose,
  title,
  icon,
  iconColor = notesTheme.primary,
  width = "min(640px, 100%)",
  footer,
  busy = false,
  children
}: Props) {
  // Focus return, done here rather than left to Mantine's `returnFocus`.
  //
  // That prop is driven by `useFocusReturn`, which captures the opener inside
  // a `useDidUpdate` — an effect that deliberately skips the first render. It
  // therefore only works for a modal that is mounted once and toggled via
  // `opened`. Every Notes dialog is instead conditionally rendered
  // (`{editing && <EditPageModal … />}`), so it mounts with `opened` already
  // true and unmounts on close: the capture branch never runs, and neither
  // does the restore branch. `returnFocus` stays set below because it is
  // correct for any future always-mounted caller, but it does nothing here —
  // an E2E assertion on focus return is what surfaced this.
  const openerRef = useRef<HTMLElement | null>(null);
  useEffect(() => {
    const active = document.activeElement;
    openerRef.current = active instanceof HTMLElement ? active : null;
    return () => {
      const opener = openerRef.current;
      openerRef.current = null;
      if (!opener?.isConnected) return;
      // Deferred one tick so the check runs after the dialog's DOM is gone.
      // Closing leaves focus on <body>; anything else means focus has already
      // been placed deliberately and must not be yanked back.
      window.setTimeout(() => {
        if (document.activeElement === null || document.activeElement === document.body) {
          opener.focus({ preventScroll: true });
        }
      }, 0);
    };
  }, []);

  return (
    <Modal.Root
      opened
      onClose={onClose}
      centered
      padding={0}
      radius={6}
      zIndex={200}
      size={width}
      closeOnEscape={!busy}
      closeOnClickOutside={!busy}
      returnFocus
      trapFocus
    >
      <Modal.Overlay backgroundOpacity={0.55} color={notesTheme.darkHeader} />
      <Modal.Content
        style={{
          fontFamily: "inherit",
          boxShadow: "0 22px 60px -12px rgba(0,0,0,0.35)"
        }}
      >
        <Modal.Header
          style={{
            padding: "14px 18px",
            borderBottom: `1px solid ${notesTheme.border}`,
            minHeight: 0
          }}
        >
          <Modal.Title
            style={{
              fontSize: 14,
              fontWeight: 700,
              color: notesTheme.dark,
              display: "flex",
              alignItems: "center",
              gap: 8
            }}
          >
            {icon && <i className={`fa ${icon}`} style={{ color: iconColor }} />}
            {title}
          </Modal.Title>
          {!busy && <Modal.CloseButton aria-label="Close dialog" />}
        </Modal.Header>

        <Modal.Body style={{ padding: 20 }}>{children}</Modal.Body>

        {footer && (
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
            {footer}
          </div>
        )}
      </Modal.Content>
    </Modal.Root>
  );
}

// Field styling shared by the Notes dialogs. Passed to Mantine inputs via
// `styles`, so the label is a real <label htmlFor> wired to the control
// (archived-9) while still looking like the prototype's uppercase micro-label.
// Previously `Label` rendered a bare <div> next to an unlabelled <input>,
// and screen readers announced the field as "edit, blank".
export const notesInputStyles = {
  label: {
    fontSize: 10.5,
    fontWeight: 700,
    color: notesTheme.muted,
    textTransform: "uppercase" as const,
    letterSpacing: "0.06em",
    marginBottom: 6
  },
  input: {
    border: `1px solid ${notesTheme.border}`,
    borderRadius: 4,
    padding: "8px 12px",
    fontSize: 13,
    fontFamily: "inherit",
    color: notesTheme.dark
  }
};

// Footer button styling, previously copy-pasted into every modal file.
export const btnGhostStyle: React.CSSProperties = {
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

export const btnPrimaryStyle: React.CSSProperties = {
  background: notesTheme.primary,
  border: `1px solid ${notesTheme.primary}`,
  borderRadius: 4,
  padding: "6px 14px",
  fontSize: 12,
  fontWeight: 700,
  color: "#fff",
  fontFamily: "inherit"
};

// Group label for a set of controls that is not a single input (the note-kind
// picker, for example). Renders the same micro-label, and the caller points
// the group's aria-labelledby at it so the grouping is exposed rather than
// implied by proximity.
export function NotesGroupLabel({ id, children }: { id: string; children: ReactNode }) {
  return (
    <div id={id} style={notesInputStyles.label}>
      {children}
    </div>
  );
}
