import type { CSSProperties } from "react";
import {
  NoteVersionSummary,
  PageVersionSummary
} from "@/api/content";
import { useNoteVersions, usePageVersions } from "@/hooks/useContent";
import { NotesModal } from "./NotesModal";
import { notesTheme } from "./notesTheme";

// Common shape over PageVersionSummary | NoteVersionSummary so the row
// renderer doesn't have to branch on `kind`. titleDisplay defaults to a
// generic "Note" for note rows where title may be null.
type Row = {
  id: string;
  versionNumber: number;
  titleDisplay: string;
  kind: string;
  note: string | null;
  createdAtUtc: string;
  createdByName: string | null;
};

type Props =
  | {
      kind: "page";
      pageId: string;
      currentTitle: string;
      // Live page's updated_at_utc — drives the timestamp on the synthetic
      // "Current draft" row pinned at the top of the list. Under
      // session-rollup, no version row ever matches the live state, so the
      // live state is surfaced as its own pinned entry instead of a pill.
      currentUpdatedAtUtc: string;
      onSelect: (versionNumber: number) => void;
      onClose: () => void;
    }
  | {
      kind: "note";
      noteId: string;
      currentTitle: string;
      currentUpdatedAtUtc: string;
      onSelect: (versionNumber: number) => void;
      onClose: () => void;
    };

export function HistoryModal(props: Props) {
  const pageQuery = usePageVersions(
    props.kind === "page" ? props.pageId : null,
    props.kind === "page"
  );
  const noteQuery = useNoteVersions(
    props.kind === "note" ? props.noteId : null,
    props.kind === "note"
  );
  const isLoading = props.kind === "page" ? pageQuery.isLoading : noteQuery.isLoading;
  const error = props.kind === "page" ? pageQuery.error : noteQuery.error;
  const items: Row[] =
    props.kind === "page"
      ? (pageQuery.data?.items ?? []).map((v) => toRow(v))
      : (noteQuery.data?.items ?? []).map((v) =>
          toRowNote(v, props.currentTitle)
        );

  return (
    <NotesModal
      onClose={props.onClose}
      title={
        <span style={{ display: "flex", flexDirection: "column", minWidth: 0 }}>
          <span>Revision history</span>
          <span
            style={{
              fontSize: 11.5,
              fontWeight: 400,
              color: notesTheme.muted,
              marginTop: 2,
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap"
            }}
          >
            {props.currentTitle}
          </span>
        </span>
      }
      icon="fa-clock-rotate-left"
      width="min(560px, 100%)"
    >
      {/* The negative margin cancels the shell body's 20px padding so the
          revision rows stay full-bleed (each row carries its own 18px
          inset), and the explicit maxHeight keeps the list bounded the way
          the old panel's 80vh cap did rather than growing the dialog. */}
      <div
        // Focus opens on the list, not the close button: browsing revisions
        // is what the dialog is for, and Tab from here lands on the first
        // revision row.
        data-autofocus
        tabIndex={-1}
        style={{
          margin: -20,
          padding: "4px 0",
          maxHeight: "60vh",
          overflowY: "auto",
          outline: "none"
        }}
      >
        {isLoading && (
          <div style={emptyStyle}>
            <i className="fa fa-spinner fa-spin" style={{ marginRight: 6 }} />
            Loading revisions…
          </div>
        )}
        {error && !isLoading && (
          <div style={{ ...emptyStyle, color: notesTheme.danger }}>
            <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
            Couldn&apos;t load revisions.
          </div>
        )}
        {!isLoading && !error && (
          <ul style={{ listStyle: "none", margin: 0, padding: 0 }}>
            <CurrentDraftRow updatedAtUtc={props.currentUpdatedAtUtc} />
            {items.length === 0 ? (
              <li style={emptyStyle}>No earlier revisions yet.</li>
            ) : (
              items.map((row) => (
                <RevisionRow
                  key={row.id}
                  row={row}
                  onClick={() => {
                    props.onSelect(row.versionNumber);
                  }}
                />
              ))
            )}
          </ul>
        )}
      </div>
    </NotesModal>
  );
}

function RevisionRow({ row, onClick }: { row: Row; onClick: () => void }) {
  const meta = kindMeta(row.kind);
  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        style={{
          width: "100%",
          textAlign: "left",
          background: "transparent",
          border: "none",
          padding: "10px 18px",
          fontFamily: "inherit",
          cursor: "pointer",
          display: "flex",
          alignItems: "center",
          gap: 12,
          borderBottom: `1px solid ${notesTheme.border}`
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.background = notesTheme.hover;
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.background = "transparent";
        }}
      >
        <div
          style={{
            width: 28,
            height: 28,
            borderRadius: 4,
            background: meta.bg,
            color: meta.color,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 11,
            flexShrink: 0
          }}
          title={meta.label}
        >
          <i className={`fa ${meta.icon}`} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: 8,
              fontSize: 12.5,
              color: notesTheme.dark
            }}
          >
            <span style={{ fontWeight: 700 }}>v{row.versionNumber}</span>
            <span
              style={{
                color: notesTheme.muted,
                fontWeight: 400,
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
                flex: 1
              }}
            >
              {row.titleDisplay}
            </span>
          </div>
          <div
            style={{
              fontSize: 11,
              color: notesTheme.muted,
              marginTop: 2,
              display: "flex",
              alignItems: "center",
              gap: 6
            }}
          >
            <span>{formatDateTime(row.createdAtUtc)}</span>
            <span>·</span>
            <span>{meta.label}</span>
            <span>·</span>
            <span
              style={{
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
                maxWidth: 140,
                color: row.createdByName ? notesTheme.dark : notesTheme.muted,
                fontWeight: row.createdByName ? 600 : 400
              }}
              title={row.createdByName ?? "Unknown user"}
            >
              <i className="fa fa-user" style={{ marginRight: 4, fontSize: 9 }} />
              {row.createdByName ?? "Unknown"}
            </span>
            {row.note && (
              <>
                <span>·</span>
                <span
                  style={{
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                    maxWidth: 220
                  }}
                  title={row.note}
                >
                  {row.note}
                </span>
              </>
            )}
          </div>
        </div>
        <i
          className="fa fa-chevron-right"
          style={{ fontSize: 10, color: notesTheme.muted, flexShrink: 0 }}
        />
      </button>
    </li>
  );
}

// Pinned non-clickable row representing the live editor state. Under
// session-rollup the live state never has a corresponding version_row, so
// the "Current" indicator is surfaced here instead of as a pill on a
// historical row. The user closes the modal (or hits Exit on the revision
// banner) to return to it.
function CurrentDraftRow({ updatedAtUtc }: { updatedAtUtc: string }) {
  return (
    // aria-current exposes "this is where you are" programmatically; the
    // highlight background alone said it only to sighted users.
    <li aria-current="true">
      <div
        style={{
          width: "100%",
          padding: "10px 18px",
          display: "flex",
          alignItems: "center",
          gap: 12,
          borderBottom: `1px solid ${notesTheme.border}`,
          background: notesTheme.selected
        }}
      >
        <div
          style={{
            width: 28,
            height: 28,
            borderRadius: 4,
            background: notesTheme.primary + "20",
            color: notesTheme.primary,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 11,
            flexShrink: 0
          }}
          title="Current draft"
        >
          <i className="fa fa-pen-to-square" />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: 8,
              fontSize: 12.5,
              color: notesTheme.dark
            }}
          >
            <span style={{ fontWeight: 700 }}>Current draft</span>
            <span
              style={{
                fontSize: 10,
                fontWeight: 700,
                background: notesTheme.primary,
                color: "#fff",
                padding: "1px 6px",
                borderRadius: 3,
                textTransform: "uppercase",
                letterSpacing: 0.5
              }}
            >
              Working copy
            </span>
          </div>
          <div
            style={{
              fontSize: 11,
              color: notesTheme.muted,
              marginTop: 2
            }}
          >
            Last edited {formatDateTime(updatedAtUtc)}
          </div>
        </div>
      </div>
    </li>
  );
}

function toRow(v: PageVersionSummary): Row {
  return {
    id: v.id,
    versionNumber: v.versionNumber,
    titleDisplay: v.title || "(untitled)",
    kind: v.kind,
    note: v.note,
    createdAtUtc: v.createdAtUtc,
    createdByName: v.createdByName
  };
}

function toRowNote(v: NoteVersionSummary, fallbackTitle: string): Row {
  return {
    id: v.id,
    versionNumber: v.versionNumber,
    titleDisplay: v.title || fallbackTitle,
    kind: v.kind,
    note: v.note,
    createdAtUtc: v.createdAtUtc,
    createdByName: v.createdByName
  };
}

function kindMeta(kind: string): { icon: string; label: string; color: string; bg: string } {
  switch (kind) {
    case "manual":
      return {
        icon: "fa-floppy-disk",
        label: "Manual save",
        color: notesTheme.primary,
        bg: notesTheme.primary + "20"
      };
    case "restore":
      return {
        icon: "fa-rotate-left",
        label: "Restored",
        color: notesTheme.warning,
        bg: notesTheme.warning + "25"
      };
    case "autosave":
    default:
      return {
        icon: "fa-cloud-arrow-up",
        label: "Auto-saved",
        color: notesTheme.muted,
        bg: "#eef0f2"
      };
  }
}

function formatDateTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit"
    });
  } catch {
    return iso;
  }
}

const emptyStyle: CSSProperties = {
  padding: 28,
  textAlign: "center",
  fontSize: 12.5,
  color: notesTheme.muted
};
