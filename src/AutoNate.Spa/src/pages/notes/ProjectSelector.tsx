import { useEffect, useRef, useState } from "react";
import { ProjectDto } from "@/api/content";
import { cabinetColorFor, notesTheme, projectInitials } from "./notesTheme";

type Props = {
  projects: ProjectDto[];
  project: ProjectDto;
  onPick: (project: ProjectDto) => void;
};

export function ProjectSelector({ projects, project, onPick }: Props) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Click-outside closes the dropdown. Mounted only while open so we don't
  // leave a document-level listener live for the whole page lifecycle.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        style={{
          width: "100%",
          display: "flex",
          alignItems: "center",
          gap: 9,
          background: "#fff",
          border: `1px solid ${notesTheme.border}`,
          borderRadius: 4,
          padding: "7px 10px",
          cursor: "pointer",
          fontFamily: "inherit",
          textAlign: "left"
        }}
      >
        <Avatar initials={projectInitials(project.name)} color={cabinetColorFor(project.id)} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              fontSize: 11.5,
              fontWeight: 700,
              color: notesTheme.dark,
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis"
            }}
          >
            {project.name}
          </div>
          <div style={{ fontSize: 10, color: notesTheme.muted, marginTop: 1 }}>
            {project.description ?? "—"}
          </div>
        </div>
        <i className="fa fa-chevron-down" style={{ fontSize: 9, color: notesTheme.muted }} />
      </button>

      {open && (
        <div
          style={{
            position: "absolute",
            top: "calc(100% + 4px)",
            left: 0,
            right: 0,
            zIndex: 50,
            background: "#fff",
            border: `1px solid ${notesTheme.border}`,
            borderRadius: 4,
            boxShadow: "0 6px 18px rgba(0,0,0,0.12)",
            padding: 4
          }}
        >
          {projects.map((p) => (
            <ProjectOption
              key={p.id}
              project={p}
              active={p.id === project.id}
              onPick={() => {
                onPick(p);
                setOpen(false);
              }}
            />
          ))}
          {projects.length === 0 && (
            <div style={{ padding: 12, fontSize: 11, color: notesTheme.muted }}>
              No projects yet — create one to get started.
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function ProjectOption({
  project,
  active,
  onPick
}: {
  project: ProjectDto;
  active: boolean;
  onPick: () => void;
}) {
  const [hover, setHover] = useState(false);
  return (
    <button
      type="button"
      onClick={onPick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        width: "100%",
        display: "flex",
        alignItems: "center",
        gap: 8,
        background: active ? notesTheme.selected : hover ? notesTheme.hover : "transparent",
        border: "none",
        borderRadius: 4,
        padding: "6px 8px",
        cursor: "pointer",
        fontFamily: "inherit",
        textAlign: "left"
      }}
    >
      <Avatar initials={projectInitials(project.name)} color={cabinetColorFor(project.id)} size={24} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 11.5, fontWeight: 700, color: notesTheme.dark }}>{project.name}</div>
        <div style={{ fontSize: 10, color: notesTheme.muted }}>{project.description ?? "—"}</div>
      </div>
      {active && (
        <i className="fa fa-check" style={{ fontSize: 10, color: notesTheme.primary }} />
      )}
    </button>
  );
}

function Avatar({
  initials,
  color,
  size = 28
}: {
  initials: string;
  color: string;
  size?: number;
}) {
  return (
    <div
      style={{
        width: size,
        height: size,
        borderRadius: 4,
        background: color,
        color: "#fff",
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: size * 0.36,
        fontWeight: 700,
        flexShrink: 0
      }}
    >
      {initials}
    </div>
  );
}
