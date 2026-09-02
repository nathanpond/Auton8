import { useEffect, useMemo, useState } from "react";
import { Button, Group, Modal, Switch as MantineSwitch, TextInput } from "@mantine/core";
import {
  ChatbotWindowMode,
  useUserPreferences
} from "./UserPreferencesContext";
import "./PreferencesModal.css";

// Settings catalog — supports nested children. Mirrors the design's CATEGORIES
// shape; future settings get added here. The `kind` field tells the right
// pane how to render a control beyond the simple inline-select fallback.
type SettingDef = {
  id: string;
  label: string;
  desc?: string;
  kind?: "window-mode" | "switch";
};

type ChildDef = {
  id: string;
  label: string;
  icon: string;
};

type CategoryDef = {
  id: string;
  label: string;
  icon: string;
  settings: SettingDef[];
  children?: ChildDef[];
};

const CATEGORIES: CategoryDef[] = [
  {
    id: "general",
    label: "General",
    icon: "fa-sliders",
    settings: []
  },
  {
    id: "chatbot",
    label: "Chatbot Settings",
    icon: "fa-comments",
    settings: [
      { id: "window", label: "Chatbot Window", kind: "window-mode" },
      {
        id: "overHeader",
        label: "Show Chatbot Over Header",
        kind: "switch",
        desc: "When enabled, the chatbot floats above the top navigation instead of sitting beneath it."
      }
    ],
    children: [
      { id: "chatbot.appearance", label: "Appearance", icon: "fa-palette" },
      { id: "chatbot.notifications", label: "Notifications", icon: "fa-bell" },
      { id: "chatbot.advanced", label: "Advanced", icon: "fa-gears" }
    ]
  }
];

type FlatSetting = SettingDef & {
  categoryId: string;
  categoryLabel: string;
};

function flatten(cats: CategoryDef[]): FlatSetting[] {
  const out: FlatSetting[] = [];
  for (const c of cats) {
    for (const s of c.settings) {
      out.push({ ...s, categoryId: c.id, categoryLabel: c.label });
    }
  }
  return out;
}

export default function PreferencesModal() {
  const {
    isModalOpen,
    closeModal,
    chatbotWindowMode,
    chatbotOverHeader,
    setChatbotWindowMode,
    setChatbotOverHeader
  } = useUserPreferences();

  // Edits live locally until Save so the user can cancel without committing.
  const [draftMode, setDraftMode] = useState<ChatbotWindowMode>(chatbotWindowMode);
  const [draftOverHeader, setDraftOverHeader] = useState<boolean>(chatbotOverHeader);
  const [activeCat, setActiveCat] = useState("chatbot");
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [query, setQuery] = useState("");

  // Reset drafts whenever the modal is reopened so a Cancel-then-Open returns
  // the form to the persisted state, not the abandoned drafts.
  useEffect(() => {
    if (isModalOpen) {
      setDraftMode(chatbotWindowMode);
      setDraftOverHeader(chatbotOverHeader);
      setActiveCat("chatbot");
      setExpanded({});
      setQuery("");
    }
  }, [isModalOpen, chatbotWindowMode, chatbotOverHeader]);

  // Esc closes; consistent with ConfirmModal / TemplatePickerModal.
  useEffect(() => {
    if (!isModalOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        closeModal();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isModalOpen, closeModal]);

  const allSettings = useMemo(() => flatten(CATEGORIES), []);
  const searching = query.trim().length > 0;
  const matches = useMemo(() => {
    if (!searching) return [];
    const q = query.toLowerCase();
    return allSettings.filter(
      (s) =>
        s.label.toLowerCase().includes(q) ||
        (s.desc ?? "").toLowerCase().includes(q) ||
        s.categoryLabel.toLowerCase().includes(q)
    );
  }, [query, allSettings, searching]);

  const currentCat =
    CATEGORIES.find((c) => c.id === activeCat) ?? CATEGORIES[0];

  if (!isModalOpen) return null;

  const onSave = () => {
    setChatbotWindowMode(draftMode);
    setChatbotOverHeader(draftOverHeader);
    closeModal();
  };

  return (
    <Modal
      opened={isModalOpen}
      onClose={closeModal}
      size="auto"
      withCloseButton={false}
      padding={0}
      zIndex={1065}
      styles={{
        content: { width: "min(1120px, calc(100vw - 32px))", height: "min(720px, calc(100vh - 32px))" },
        body: { display: "flex", flexDirection: "column", minHeight: 0, height: "100%" }
      }}
    >
      <div className="pref-header">
        <h5 id="prefs-modal-title" style={{ margin: 0 }}>
          Preferences
        </h5>
        <div className="pref-search">
          <i className="fa fa-search pref-search-icon" aria-hidden="true" />
          <TextInput
            size="xs"
            type="text"
            placeholder="Search settings…"
            value={query}
            onChange={(e) => setQuery(e.currentTarget.value)}
            aria-label="Search settings"
            style={{ width: "100%" }}
          />
          {query && (
            <button
              type="button"
              className="pref-search-clear"
              onClick={() => setQuery("")}
              aria-label="Clear search"
            >
              <i className="fa fa-times" aria-hidden="true" />
            </button>
          )}
        </div>
        <Button
          variant="subtle"
          color="gray"
          size="xs"
          onClick={closeModal}
          aria-label="Close"
        >
          <i className="fa fa-times" />
        </Button>
      </div>

      <div className="pref-body">
        <aside className="pref-cats">
          {CATEGORIES.map((cat) => (
            <CategoryRow
              key={cat.id}
              cat={cat}
              active={!searching && activeCat === cat.id}
              expanded={!!expanded[cat.id]}
              onSelect={() => {
                setActiveCat(cat.id);
                setQuery("");
              }}
              onToggleExpand={() =>
                setExpanded((e) => ({ ...e, [cat.id]: !e[cat.id] }))
              }
            />
          ))}
        </aside>

        <main className="pref-pane">
          {searching ? (
            <SearchResults
              query={query}
              matches={matches}
              onPick={(s) => {
                setActiveCat(s.categoryId);
                setQuery("");
              }}
            />
          ) : (
            <CategoryContent
              cat={currentCat}
              windowMode={draftMode}
              onChangeWindowMode={setDraftMode}
              overHeader={draftOverHeader}
              onChangeOverHeader={setDraftOverHeader}
            />
          )}
        </main>
      </div>

      <Group justify="flex-end" gap="xs" p="md" style={{ borderTop: "1px solid var(--mantine-color-default-border)" }}>
        <Button variant="default" onClick={closeModal}>
          Cancel
        </Button>
        <Button onClick={onSave}>Save changes</Button>
      </Group>
    </Modal>
  );
}

type CategoryRowProps = {
  cat: CategoryDef;
  active: boolean;
  expanded: boolean;
  onSelect: () => void;
  onToggleExpand: () => void;
};

function CategoryRow({
  cat,
  active,
  expanded,
  onSelect,
  onToggleExpand
}: CategoryRowProps) {
  const hasChildren = !!(cat.children && cat.children.length);
  return (
    <div>
      <button
        type="button"
        className={`pref-cat${active ? " active" : ""}`}
        onClick={onSelect}
      >
        <i className={`fa ${cat.icon} pref-cat-icon`} aria-hidden="true" />
        <span className="pref-cat-label">{cat.label}</span>
        {hasChildren && (
          <span
            role="button"
            tabIndex={0}
            aria-label={expanded ? "Collapse" : "Expand"}
            className="pref-cat-chevron"
            onClick={(e) => {
              e.stopPropagation();
              onToggleExpand();
            }}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                e.stopPropagation();
                onToggleExpand();
              }
            }}
          >
            <i
              className={`fa fa-chevron-${expanded ? "down" : "right"}`}
              aria-hidden="true"
            />
          </span>
        )}
      </button>

      {hasChildren && expanded && (
        <div className="pref-subrows">
          {cat.children!.map((ch) => (
            <div key={ch.id} className="pref-subrow">
              <i className={`fa ${ch.icon}`} aria-hidden="true" />
              {ch.label}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

type CategoryContentProps = {
  cat: CategoryDef;
  windowMode: ChatbotWindowMode;
  onChangeWindowMode: (mode: ChatbotWindowMode) => void;
  overHeader: boolean;
  onChangeOverHeader: (value: boolean) => void;
};

function CategoryContent({
  cat,
  windowMode,
  onChangeWindowMode,
  overHeader,
  onChangeOverHeader
}: CategoryContentProps) {
  const description =
    cat.id === "chatbot"
      ? "Control how the Auton8 chatbot appears and behaves across the product."
      : "Workspace-level preferences that apply across Auton8.";
  return (
    <div className="pref-content">
      <header className="pref-content-header">
        <h3>{cat.label}</h3>
        <p>{description}</p>
      </header>

      {cat.id === "chatbot" && (
        <div className="pref-section-list">
          <SettingBlock
            label="Chatbot Window"
            hint="Choose how the chatbot is presented when opened."
          >
            <div className="pref-mode-grid">
              <ModeCard
                selected={windowMode === "overlay"}
                onClick={() => onChangeWindowMode("overlay")}
                title="Overlay"
                desc="Pops out on top of the page, floating above content. Best when you don't want to disrupt the layout."
                preview="overlay"
              />
              <ModeCard
                selected={windowMode === "fill"}
                onClick={() => onChangeWindowMode("fill")}
                title="Fill"
                desc="Takes up space and pushes the page narrower so you can still see everything alongside the chat."
                preview="fill"
              />
            </div>
          </SettingBlock>

          <div className="pref-divider" />

          <InlineSetting
            label="Show Chatbot Over Header"
            desc="When enabled, the chatbot floats above the top navigation instead of sitting beneath it."
            control={
              <Switch
                id="pref-over-header"
                checked={overHeader}
                onChange={onChangeOverHeader}
              />
            }
          />
        </div>
      )}

      {cat.id === "general" && (
        <div className="pref-empty-state">
          <i className="fa fa-sliders" aria-hidden="true" />
          <div>General preferences will appear here.</div>
        </div>
      )}
    </div>
  );
}

function SettingBlock({
  label,
  hint,
  children
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="pref-block">
      <div className="pref-block-head">
        <div className="pref-block-label">{label}</div>
        {hint && <div className="pref-block-hint">{hint}</div>}
      </div>
      {children}
    </div>
  );
}

function InlineSetting({
  label,
  desc,
  control
}: {
  label: string;
  desc?: string;
  control: React.ReactNode;
}) {
  return (
    <div className="pref-inline">
      <div className="pref-inline-text">
        <div className="pref-inline-label">{label}</div>
        {desc && <div className="pref-inline-desc">{desc}</div>}
      </div>
      <div className="pref-inline-control">{control}</div>
    </div>
  );
}

function ModeCard({
  selected,
  onClick,
  title,
  desc,
  preview
}: {
  selected: boolean;
  onClick: () => void;
  title: string;
  desc: string;
  preview: ChatbotWindowMode;
}) {
  return (
    <button
      type="button"
      className={`pref-mode-card${selected ? " selected" : ""}`}
      onClick={onClick}
      aria-pressed={selected}
    >
      <ModePreview kind={preview} selected={selected} />
      <div className="pref-mode-head">
        <span className="pref-mode-title">{title}</span>
        <Radio selected={selected} />
      </div>
      <p className="pref-mode-desc">{desc}</p>
    </button>
  );
}

// Tiny mock of a page with the chatbot positioned per mode. Visual cue only —
// not a real layout preview.
function ModePreview({
  kind,
  selected
}: {
  kind: ChatbotWindowMode;
  selected: boolean;
}) {
  return (
    <div className={`pref-mode-preview${selected ? " selected" : ""}`}>
      <div className="pref-mode-preview-header">
        <div className="pref-mode-preview-dot" />
        <div className="pref-mode-preview-bar" />
      </div>
      <div className={`pref-mode-preview-body pref-mode-preview-body--${kind}`}>
        <div className="pref-mode-preview-line w70" />
        <div className="pref-mode-preview-line w90" />
        <div className="pref-mode-preview-line w55" />
        <div className="pref-mode-preview-tiles">
          <div />
          <div />
        </div>
      </div>
      <div className={`pref-mode-preview-bot pref-mode-preview-bot--${kind}`}>
        <div className="pref-mode-preview-bot-bar" />
        <div className="pref-mode-preview-line w90" />
        <div className="pref-mode-preview-line w70" />
        <div className="pref-mode-preview-bot-input" />
      </div>
    </div>
  );
}

function Radio({ selected }: { selected: boolean }) {
  return (
    <span
      className={`pref-radio${selected ? " selected" : ""}`}
      aria-hidden="true"
    />
  );
}

function Switch({
  id,
  checked,
  onChange
}: {
  id: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <MantineSwitch
      id={id}
      checked={checked}
      onChange={(e) => onChange(e.currentTarget.checked)}
    />
  );
}

function SearchResults({
  query,
  matches,
  onPick
}: {
  query: string;
  matches: FlatSetting[];
  onPick: (s: FlatSetting) => void;
}) {
  return (
    <div>
      <div className="pref-search-heading">
        <h3>
          {matches.length} {matches.length === 1 ? "result" : "results"} for
          “{query}”
        </h3>
      </div>
      {matches.length === 0 ? (
        <div className="pref-empty-state">
          <i className="fa fa-search" aria-hidden="true" />
          <div>No settings match your search.</div>
        </div>
      ) : (
        <div className="pref-search-results">
          {matches.map((m) => (
            <button
              key={m.id}
              type="button"
              className="pref-search-result"
              onClick={() => onPick(m)}
            >
              <div className="pref-search-result-text">
                <div className="pref-search-result-label">{m.label}</div>
                {m.desc && (
                  <div className="pref-search-result-desc">{m.desc}</div>
                )}
              </div>
              <span className="pref-search-result-cat">{m.categoryLabel}</span>
              <i className="fa fa-arrow-right" aria-hidden="true" />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
