import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { ProjectDto } from "@/api/content";
import { cabinetColorFor, notesTheme, projectInitials } from "./notesTheme";

const OPTION_ROW_HEIGHT = 36;
const MAX_VISIBLE_OPTIONS = 5;

type Props = {
  projects: ProjectDto[];
  project: ProjectDto;
  onPick: (project: ProjectDto) => void;
  onNewProject?: () => void;
};

export function ProjectSelector({ projects, project, onPick, onNewProject }: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const ref = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);

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

  // Reset the filter every time the dropdown closes and focus the search
  // input as soon as it opens — opening should land the caret in the search
  // box so the user can start typing immediately.
  useEffect(() => {
    if (open) {
      searchRef.current?.focus();
    } else {
      setQuery("");
    }
  }, [open]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return projects;
    return projects.filter((p) => p.name.toLowerCase().includes(q));
  }, [projects, query]);

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
          <div style={{ position: "relative", margin: "2px 2px 4px" }}>
            <i
              className="fa fa-magnifying-glass"
              style={{
                position: "absolute",
                left: 8,
                top: "50%",
                transform: "translateY(-50%)",
                color: notesTheme.muted,
                fontSize: 10.5,
                pointerEvents: "none"
              }}
            />
            <input
              ref={searchRef}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Escape") {
                  e.preventDefault();
                  setOpen(false);
                  return;
                }
                if (e.key === "Enter" && filtered.length > 0) {
                  e.preventDefault();
                  const first = filtered[0];
                  onPick(first);
                  setOpen(false);
                }
              }}
              placeholder="Search projects…"
              style={{
                width: "100%",
                border: `1px solid ${notesTheme.border}`,
                borderRadius: 4,
                padding: "6px 10px 6px 26px",
                fontSize: 11.5,
                outline: "none",
                fontFamily: "inherit",
                background: "#fff",
                boxSizing: "border-box"
              }}
            />
          </div>

          <div
            style={{
              maxHeight: OPTION_ROW_HEIGHT * MAX_VISIBLE_OPTIONS,
              overflowY: "auto"
            }}
          >
            {filtered.map((p) => (
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
            {projects.length > 0 && filtered.length === 0 && (
              <div style={{ padding: 12, fontSize: 11, color: notesTheme.muted }}>
                No projects match “{query}”.
              </div>
            )}
          </div>

          <div
            style={{
              height: 1,
              background: notesTheme.border,
              margin: "4px 4px"
            }}
          />
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              gap: 8
            }}
          >
            {onNewProject ? (
              <AddProjectButton
                onClick={() => {
                  onNewProject();
                  setOpen(false);
                }}
              />
            ) : (
              <span />
            )}
            <Link
              to="/projects"
              onClick={() => setOpen(false)}
              style={{
                flexShrink: 0,
                padding: "6px 8px",
                fontSize: 11,
                fontWeight: 700,
                color: notesTheme.primary,
                textDecoration: "none",
                whiteSpace: "nowrap",
                fontFamily: "inherit",
                display: "inline-flex",
                alignItems: "center",
                gap: 4
              }}
            >
              All projects
              <i className="fa fa-arrow-right" style={{ fontSize: 9 }} />
            </Link>
          </div>
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

function AddProjectButton({ onClick }: { onClick: () => void }) {
  const [hover, setHover] = useState(false);
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        flex: 1,
        minWidth: 0,
        display: "flex",
        alignItems: "center",
        gap: 8,
        background: hover ? notesTheme.hover : "transparent",
        border: "none",
        borderRadius: 4,
        padding: "8px 8px",
        cursor: "pointer",
        fontFamily: "inherit",
        textAlign: "left",
        color: notesTheme.primary,
        fontSize: 11.5,
        fontWeight: 700
      }}
    >
      <div
        style={{
          width: 24,
          height: 24,
          borderRadius: 4,
          border: `1px dashed ${notesTheme.border}`,
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          color: notesTheme.primary,
          fontSize: 10,
          flexShrink: 0
        }}
      >
        <i className="fa fa-plus" />
      </div>
      Add project
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
