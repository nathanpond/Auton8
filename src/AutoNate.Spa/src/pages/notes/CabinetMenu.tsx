import { useEffect, useRef, useState } from "react";
import { notesTheme } from "./notesTheme";

type Props = {
  isArchived: boolean;
  onRename: () => void;
  onArchive: () => void;
  onDelete: () => void;
};

// Kebab popover for the cabinet header. Built as a small absolute-positioned
// menu rather than a real <Menu/> because everything else in /notes is plain
// inline-styled to stay close to the design prototype.
export function CabinetMenu({ isArchived, onRename, onArchive, onDelete }: Props) {
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

  return (
    <div ref={wrapRef} style={{ position: "relative", display: "inline-flex" }}>
      <button
        type="button"
        title="Cabinet options"
        onClick={() => setOpen((o) => !o)}
        style={{
          background: open ? notesTheme.hover : "transparent",
          border: "none",
          cursor: "pointer",
          color: notesTheme.muted,
          width: 24,
          height: 24,
          borderRadius: 3,
          fontSize: 11
        }}
      >
        <i className="fa fa-ellipsis" />
      </button>

      {open && (
        <div
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
            label="Delete cabinet"
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
