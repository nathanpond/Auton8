import { useEffect, useRef, useState } from "react";
import { notesTheme } from "./notesTheme";

type Props = {
  entityLabel: string;        // "cabinet" | "notebook" | ...
  isArchived: boolean;
  onRename: () => void;
  onArchive: () => void;
  onDelete: () => void;
  // Optional: parent can keep hover-revealed controls visible while the menu
  // is open. Cabinet header (always-visible kebab) doesn't pass this.
  onOpenChange?: (open: boolean) => void;
  size?: "sm" | "md";
};

// Kebab popover used by cabinet headers and notebook rows. Click-outside or
// Escape closes; arrow keys/etc. not wired because this is a 3-item menu and
// mouse drive is the dominant use.
export function ContentItemMenu({
  entityLabel,
  isArchived,
  onRename,
  onArchive,
  onDelete,
  onOpenChange,
  size = "md"
}: Props) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  useEffect(() => {
    onOpenChange?.(open);
  }, [open, onOpenChange]);

  const dims = size === "sm" ? { w: 18, h: 18, font: 9 } : { w: 24, h: 24, font: 11 };

  return (
    <div ref={wrapRef} style={{ position: "relative", display: "inline-flex" }}>
      <button
        type="button"
        title={`${capitalize(entityLabel)} options`}
        onClick={(e) => {
          e.stopPropagation();
          setOpen((o) => !o);
        }}
        style={{
          background: open ? notesTheme.hover : "transparent",
          border: "none",
          cursor: "pointer",
          color: notesTheme.muted,
          width: dims.w,
          height: dims.h,
          borderRadius: 3,
          fontSize: dims.font,
          padding: 0
        }}
      >
        <i className="fa fa-ellipsis" />
      </button>

      {open && (
        <div
          onClick={(e) => e.stopPropagation()}
          style={{
            position: "absolute",
            top: "calc(100% + 4px)",
            right: 0,
            minWidth: 180,
            background: "#fff",
            border: `1px solid ${notesTheme.border}`,
            borderRadius: 4,
            boxShadow: "0 6px 18px rgba(0,0,0,0.12)",
            padding: 4,
            zIndex: 60
          }}
        >
          <MenuItem
            icon="fa-pen"
            label="Rename / edit"
            onClick={() => {
              setOpen(false);
              onRename();
            }}
          />
          <MenuItem
            icon={isArchived ? "fa-box-open" : "fa-box-archive"}
            label={isArchived ? "Unarchive" : "Archive"}
            onClick={() => {
              setOpen(false);
              onArchive();
            }}
          />
          <Divider />
          <MenuItem
            icon="fa-trash"
            label={`Delete ${entityLabel}`}
            danger
            onClick={() => {
              setOpen(false);
              onDelete();
            }}
          />
        </div>
      )}
    </div>
  );
}

function MenuItem({
  icon,
  label,
  onClick,
  danger
}: {
  icon: string;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  const [hover, setHover] = useState(false);
  const color = danger ? notesTheme.danger : notesTheme.dark;
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        width: "100%",
        display: "flex",
        alignItems: "center",
        gap: 8,
        background: hover ? (danger ? "#fee" : notesTheme.hover) : "transparent",
        border: "none",
        borderRadius: 4,
        padding: "6px 10px",
        textAlign: "left",
        cursor: "pointer",
        color,
        fontSize: 12,
        fontWeight: 600,
        fontFamily: "inherit"
      }}
    >
      <i className={`fa ${icon}`} style={{ width: 14, fontSize: 11 }} />
      {label}
    </button>
  );
}

function Divider() {
  return <div style={{ height: 1, background: notesTheme.border, margin: "4px 2px" }} />;
}

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}
