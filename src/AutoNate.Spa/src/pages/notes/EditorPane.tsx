import { useEffect, useState } from "react";
import { CabinetDto, NoteDto, PageDto } from "@/api/content";
import { NOTE_KIND_META, cabinetColorFor, defaultCabinetIcon, notesTheme } from "./notesTheme";
import { EditorTab, NotebookWithPages, PageTreeNode } from "./types";
import { PageOverview } from "./PageOverview";
import { VisualTextEditor } from "./VisualTextEditor";
import { NapkinEditor } from "./NapkinEditor";
import { DiagramEditor } from "./DiagramEditor";

type Props = {
  page: PageDto | null;
  pageNode: PageTreeNode | null;
  cabinet: CabinetDto | null;
  notebook: NotebookWithPages | null;
  tabs: EditorTab[];
  activeTabId: string;
  notes: NoteDto[];
  onSwitchTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
  onNewNote: () => void;
};

export function EditorPane({
  page,
  pageNode,
  cabinet,
  notebook,
  tabs,
  activeTabId,
  notes,
  onSwitchTab,
  onCloseTab,
  onNewNote
}: Props) {
  // All hooks must run on every render — the empty-state early return below
  // must not skip them. Bug we hit before: useState/useEffect lived after the
  // null-page early return, so hook call counts differed between "no page"
  // and "page selected" renders and React logged "Expected static flag was
  // missing".
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? tabs[0];
  const onPageTab = activeTab?.kind === "page";
  const [pageEditMode, setPageEditMode] = useState(false);

  // Reset to view mode when the active page changes or when the user navigates
  // away from the page tab — otherwise "edit mode" would silently persist
  // into the next page or note context.
  useEffect(() => {
    setPageEditMode(false);
  }, [page?.id, onPageTab]);

  if (!page || !cabinet || !notebook) {
    return (
      <main
        style={{
          flex: 1,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#fff",
          color: notesTheme.muted,
          fontSize: 13
        }}
      >
        <div style={{ textAlign: "center", maxWidth: 360, padding: 24 }}>
          <i
            className="fa fa-book-open"
            style={{ fontSize: 32, color: notesTheme.border, display: "block", marginBottom: 12 }}
          />
          <div style={{ fontWeight: 700, color: notesTheme.dark, marginBottom: 4 }}>
            Pick a page to get started
          </div>
          <div>
            Select a cabinet on the left, expand a notebook, and choose a page — the editor
            will load here.
          </div>
        </div>
      </main>
    );
  }

  const cabinetColor = cabinetColorFor(cabinet.id);
  const cabinetIcon = cabinet.icon ?? defaultCabinetIcon();
  const activeNote =
    activeTab && activeTab.kind !== "page"
      ? notes.find((n) => n.id === (activeTab as Extract<EditorTab, { noteId: string }>).noteId) ?? null
      : null;

  return (
    <main
      style={{
        flex: 1,
        display: "flex",
        flexDirection: "column",
        minWidth: 0,
        background: "#fff"
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          padding: "6px 10px 6px 16px",
          borderBottom: `1px solid ${notesTheme.border}`,
          background: "#fff",
          flexShrink: 0
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 6,
            color: notesTheme.muted,
            fontSize: 11,
            flex: 1,
            minWidth: 0,
            overflow: "hidden"
          }}
        >
          <i className={`fa ${cabinetIcon}`} style={{ color: cabinetColor, fontSize: 10 }} />
          <span>{cabinet.name}</span>
          <i className="fa fa-chevron-right" style={{ fontSize: 8 }} />
          <span>{notebook.name}</span>
          <i className="fa fa-chevron-right" style={{ fontSize: 8 }} />
          <strong style={{ color: notesTheme.dark, fontWeight: 700 }}>{page.title}</strong>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 2 }}>
          {onPageTab && (
            <HBtn
              icon="fa-pen"
              title={pageEditMode ? "Stop editing" : "Edit page"}
              active={pageEditMode}
              onClick={() => setPageEditMode((m) => !m)}
            />
          )}
          <HBtn icon="fa-star" title="Pin page" />
          <HBtn icon="fa-share-nodes" title="Share" />
          <HBtn icon="fa-clock-rotate-left" title="History" />
          <HBtn icon="fa-ellipsis" title="More" />
        </div>
      </div>

      <TabStrip
        tabs={tabs}
        activeTabId={activeTab?.id}
        onSwitchTab={onSwitchTab}
        onCloseTab={onCloseTab}
        onNewNote={onNewNote}
      />

      {activeTab?.kind === "page" && (
        <PageOverview page={page} mode={pageEditMode ? "edit" : "view"} />
      )}
      {activeTab?.kind === "richtext" && (
        <VisualTextEditor note={activeNote} noteName={activeTab.name} />
      )}
      {activeTab?.kind === "drawing" && (
        <NapkinEditor note={activeNote} noteName={activeTab.name} />
      )}
      {activeTab?.kind === "diagram" && (
        <DiagramEditor note={activeNote} noteName={activeTab.name} />
      )}
    </main>
  );
}

function HBtn({
  icon,
  title,
  active,
  onClick
}: {
  icon: string;
  title: string;
  active?: boolean;
  onClick?: () => void;
}) {
  const [hover, setHover] = useState(false);
  const background = active
    ? notesTheme.selected
    : hover
      ? notesTheme.rowHover
      : "transparent";
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        width: 28,
        height: 28,
        border: "none",
        borderRadius: 3,
        background,
        color: active ? notesTheme.primary : notesTheme.dark,
        cursor: "pointer",
        fontSize: 12
      }}
    >
      <i className={`fa ${icon}`} />
    </button>
  );
}

function TabStrip({
  tabs,
  activeTabId,
  onSwitchTab,
  onCloseTab,
  onNewNote
}: {
  tabs: EditorTab[];
  activeTabId: string | undefined;
  onSwitchTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
  onNewNote: () => void;
}) {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "flex-end",
        gap: 2,
        padding: "0 12px",
        borderBottom: `1px solid ${notesTheme.border}`,
        background: notesTheme.hover,
        height: 38,
        flexShrink: 0
      }}
    >
      {tabs.map((t) => (
        <Tab
          key={t.id}
          tab={t}
          active={t.id === activeTabId}
          onSwitch={() => onSwitchTab(t.id)}
          onClose={() => onCloseTab(t.id)}
        />
      ))}
      <button
        type="button"
        onClick={onNewNote}
        title="New note"
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: 6,
          background: "transparent",
          border: "none",
          padding: "0 12px",
          height: 28,
          marginBottom: 0,
          color: notesTheme.muted,
          cursor: "pointer",
          fontSize: 11.5,
          fontWeight: 700,
          fontFamily: "inherit"
        }}
      >
        <i className="fa fa-plus" style={{ fontSize: 10 }} />
        New note
      </button>
    </div>
  );
}

function Tab({
  tab,
  active,
  onSwitch,
  onClose
}: {
  tab: EditorTab;
  active: boolean;
  onSwitch: () => void;
  onClose: () => void;
}) {
  const [hover, setHover] = useState(false);
  const isPage = tab.kind === "page";
  const meta = isPage ? null : NOTE_KIND_META[tab.kind];
  const icon = isPage ? "fa-file-lines" : meta?.icon ?? "fa-file";
  const iconColor = isPage ? notesTheme.primary : meta?.color ?? notesTheme.muted;

  return (
    <div
      onClick={onSwitch}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 7,
        padding: "0 10px",
        height: 30,
        background: active ? "#fff" : hover ? notesTheme.rowHover : "transparent",
        boxShadow: active ? `inset 0 -2px 0 ${notesTheme.primary}` : "none",
        borderTopLeftRadius: 4,
        borderTopRightRadius: 4,
        cursor: "pointer",
        fontSize: 12,
        color: active ? notesTheme.dark : notesTheme.muted,
        fontWeight: active ? 700 : 600,
        position: "relative",
        top: 1,
        borderLeft: "1px solid transparent",
        borderRight: "1px solid transparent"
      }}
    >
      <i className={`fa ${icon}`} style={{ fontSize: 11, color: iconColor }} />
      <span>{tab.name}</span>
      {!isPage && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onClose();
          }}
          title="Close note"
          style={{
            border: "none",
            background: "transparent",
            cursor: "pointer",
            color: notesTheme.muted,
            width: 16,
            height: 16,
            borderRadius: 3,
            padding: 0,
            marginLeft: 2
          }}
        >
          <i className="fa fa-xmark" style={{ fontSize: 10 }} />
        </button>
      )}
    </div>
  );
}
