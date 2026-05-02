import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import type { PageTemplateInfo } from "@/api/pageTemplates";
import "./TemplatePickerModal.css";

type ViewMode = "thumbnail" | "list";

type Props = {
  templates: PageTemplateInfo[];
  selectedKey: string | null;
  onSelect: (template: PageTemplateInfo) => void;
  onCancel: () => void;
};

const UNCATEGORIZED = "Uncategorized";

type CategoryGroup = {
  category: string;
  items: PageTemplateInfo[];
};

function groupByCategory(templates: PageTemplateInfo[]): CategoryGroup[] {
  const map = new Map<string, PageTemplateInfo[]>();
  for (const t of templates) {
    const key = (t.category && t.category.trim()) || UNCATEGORIZED;
    const bucket = map.get(key) ?? [];
    bucket.push(t);
    map.set(key, bucket);
  }
  // Each bucket sorted by name; named categories alphabetical, Uncategorized last.
  for (const items of map.values()) {
    items.sort((a, b) => a.name.localeCompare(b.name));
  }
  const named = [...map.entries()]
    .filter(([k]) => k !== UNCATEGORIZED)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([category, items]) => ({ category, items }));
  const uncat = map.get(UNCATEGORIZED);
  if (uncat) named.push({ category: UNCATEGORIZED, items: uncat });
  return named;
}

function ThumbnailBox({
  template,
  size
}: {
  template: PageTemplateInfo;
  size: "card" | "selected";
}) {
  const className = size === "card" ? "tp-card-thumb" : "tp-selected-thumb";
  if (template.thumbnailUrl) {
    return (
      <div className={className}>
        <img src={template.thumbnailUrl} alt={`${template.name} preview`} />
      </div>
    );
  }
  return (
    <div className={className}>
      {size === "card" ? (
        <div className="tp-card-thumb-empty">
          <i className="fa fa-image" aria-hidden="true" />
          <span>No preview</span>
        </div>
      ) : (
        <i className="fa fa-image" aria-hidden="true" />
      )}
    </div>
  );
}

export default function TemplatePickerModal({
  templates,
  selectedKey,
  onSelect,
  onCancel
}: Props) {
  const [view, setView] = useState<ViewMode>("thumbnail");
  const [pendingKey, setPendingKey] = useState<string | null>(selectedKey);
  const [activeCategory, setActiveCategory] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const listRef = useRef<HTMLDivElement | null>(null);
  const sectionRefs = useRef<Record<string, HTMLElement | null>>({});

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return templates;
    return templates.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        (t.description ?? "").toLowerCase().includes(q) ||
        (t.category ?? "").toLowerCase().includes(q)
    );
  }, [search, templates]);

  const grouped = useMemo(() => groupByCategory(filtered), [filtered]);

  // If active category disappears (search filtered it out, or initial mount),
  // fall back to the first available group so the rail always highlights
  // something.
  useEffect(() => {
    if (grouped.length === 0) {
      if (activeCategory !== null) setActiveCategory(null);
      return;
    }
    if (!activeCategory || !grouped.find((g) => g.category === activeCategory)) {
      setActiveCategory(grouped[0].category);
    }
  }, [grouped, activeCategory]);

  // Scroll-spy: as the user scrolls the list, light up the matching category
  // in the rail. rootMargin pushes the trigger line to ~25% from the top so
  // the highlight changes as the next section starts to dominate the viewport.
  useEffect(() => {
    const root = listRef.current;
    if (!root) return;
    const headers = grouped
      .map((g) => sectionRefs.current[g.category])
      .filter((el): el is HTMLElement => el !== null && el !== undefined);
    if (headers.length === 0) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((e) => e.isIntersecting)
          .map((e) => ({
            cat: (e.target as HTMLElement).dataset.category ?? "",
            top: e.boundingClientRect.top
          }))
          .sort((a, b) => a.top - b.top);
        if (visible.length > 0 && visible[0].cat) {
          setActiveCategory(visible[0].cat);
        }
      },
      {
        root,
        rootMargin: "-10px 0px -75% 0px",
        threshold: 0
      }
    );
    headers.forEach((h) => observer.observe(h));
    return () => observer.disconnect();
  }, [grouped, view]);

  const jumpTo = useCallback((category: string) => {
    setActiveCategory(category);
    const el = sectionRefs.current[category];
    const root = listRef.current;
    if (!el || !root) return;
    // Use bounding-rect math so this works regardless of where the user is
    // currently scrolled (above or below the target) and regardless of
    // .tp-list's CSS position. offsetTop is unreliable here because the
    // section header is `position: sticky`, which can make offsetTop the
    // header's natural-position value rather than its visual offset within
    // the scroll container.
    const elRect = el.getBoundingClientRect();
    const rootRect = root.getBoundingClientRect();
    const target = root.scrollTop + (elRect.top - rootRect.top) - 4;
    root.scrollTo({ top: Math.max(0, target), behavior: "smooth" });
  }, []);

  const selectedTemplate = useMemo(
    () => templates.find((t) => t.key === pendingKey) ?? null,
    [templates, pendingKey]
  );

  const confirm = useCallback(() => {
    if (selectedTemplate) onSelect(selectedTemplate);
  }, [selectedTemplate, onSelect]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onCancel();
      } else if (e.key === "Enter" && selectedTemplate) {
        // Don't hijack Enter inside the search field — only confirm when the
        // active element isn't an input (let typing-then-enter still work for
        // refining the search).
        const tag = (document.activeElement?.tagName ?? "").toLowerCase();
        if (tag !== "input" && tag !== "textarea") {
          e.preventDefault();
          onSelect(selectedTemplate);
        }
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onCancel, onSelect, selectedTemplate]);

  return (
    <>
      <div
        className="modal show d-block"
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label="Choose a page template"
        // Bumped above the parent MenuItemEditModal (z-index 1055) so the
        // picker's own backdrop can dim the parent. Picker modal: 1065,
        // backdrop: 1060 — picker stays above its backdrop, backdrop sits
        // above the parent.
        style={{ zIndex: 1065 }}
      >
        <div
          className="modal-dialog modal-dialog-centered tp-modal-dialog"
          style={{
            maxWidth: "min(800px, calc(100vw - 32px))",
            width: "100%"
          }}
        >
          <div
            className="modal-content tp-modal-content"
            style={{ height: "90vh", maxHeight: "calc(100vh - 32px)" }}
          >
            <div className="modal-header">
              <div className="tp-modal-title-block">
                <h5 className="modal-title">Choose a page template</h5>
                <div className="text-muted small mt-1">
                  Pick a built-in starter to mount on this menu item.
                </div>
              </div>
              <div className="tp-header-right">
                <div className="tp-search">
                  <i className="fa fa-search tp-search-icon" aria-hidden="true" />
                  <input
                    type="text"
                    className="form-control form-control-sm"
                    placeholder="Search templates"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    aria-label="Search templates"
                  />
                </div>
                <div
                  className="btn-group btn-group-sm tp-view-toggle"
                  role="tablist"
                  aria-label="View"
                >
                  <button
                    type="button"
                    className={`btn btn-outline-secondary${
                      view === "thumbnail" ? " active" : ""
                    }`}
                    onClick={() => setView("thumbnail")}
                    aria-pressed={view === "thumbnail"}
                    title="Thumbnail view"
                  >
                    <i className="fa fa-th-large me-1" aria-hidden="true" />
                    Thumbnails
                  </button>
                  <button
                    type="button"
                    className={`btn btn-outline-secondary${
                      view === "list" ? " active" : ""
                    }`}
                    onClick={() => setView("list")}
                    aria-pressed={view === "list"}
                    title="List view"
                  >
                    <i className="fa fa-list me-1" aria-hidden="true" />
                    List
                  </button>
                </div>
                <button
                  type="button"
                  className="btn-close"
                  onClick={onCancel}
                  aria-label="Close"
                />
              </div>
            </div>

            <div className="modal-body tp-body">
              <aside className="tp-cats">
                <div className="tp-cats-header">Categories</div>
                {grouped.length === 0 && (
                  <div className="tp-empty" style={{ padding: "20px 12px" }}>
                    <i className="fa fa-folder-open" aria-hidden="true" />
                    No matches
                  </div>
                )}
                {grouped.map((g) => (
                  <button
                    key={g.category}
                    type="button"
                    className={`tp-cat${
                      activeCategory === g.category ? " active" : ""
                    }`}
                    onClick={() => jumpTo(g.category)}
                  >
                    <span className="tp-cat-name">{g.category}</span>
                    <span className="tp-cat-count">{g.items.length}</span>
                  </button>
                ))}
              </aside>

              <div className="tp-list" ref={listRef}>
                {grouped.length === 0 && (
                  <div className="tp-empty">
                    <i className="fa fa-search" aria-hidden="true" />
                    No templates match "{search}"
                  </div>
                )}

                {view === "thumbnail" &&
                  grouped.map((g) => (
                    <section key={g.category}>
                      <header
                        className="tp-section-header"
                        data-category={g.category}
                        ref={(el) => {
                          sectionRefs.current[g.category] = el;
                        }}
                      >
                        {g.category}
                        <span className="tp-section-count">
                          · {g.items.length}
                        </span>
                      </header>
                      <div className="tp-thumb-grid">
                        {g.items.map((t) => (
                          <button
                            key={t.key}
                            type="button"
                            className={`tp-card${
                              pendingKey === t.key ? " selected" : ""
                            }`}
                            onClick={() => setPendingKey(t.key)}
                            onDoubleClick={() => onSelect(t)}
                            aria-pressed={pendingKey === t.key}
                          >
                            <ThumbnailBox template={t} size="card" />
                            <div className="tp-card-body">
                              <h3 className="tp-card-title">{t.name}</h3>
                              <p className="tp-card-desc">
                                {t.description ?? (
                                  <em className="text-muted">
                                    No description
                                  </em>
                                )}
                              </p>
                              <div className="tp-card-foot">
                                <span className="tp-pill">
                                  {t.category ?? UNCATEGORIZED}
                                </span>
                              </div>
                            </div>
                          </button>
                        ))}
                      </div>
                    </section>
                  ))}

                {view === "list" &&
                  grouped.map((g) => (
                    <section key={g.category}>
                      <header
                        className="tp-section-header"
                        data-category={g.category}
                        ref={(el) => {
                          sectionRefs.current[g.category] = el;
                        }}
                      >
                        {g.category}
                        <span className="tp-section-count">
                          · {g.items.length}
                        </span>
                      </header>
                      <div className="tp-list-grid">
                        {g.items.map((t) => (
                          <button
                            key={t.key}
                            type="button"
                            className={`tp-list-row${
                              pendingKey === t.key ? " selected" : ""
                            }`}
                            onClick={() => setPendingKey(t.key)}
                            onDoubleClick={() => onSelect(t)}
                            aria-pressed={pendingKey === t.key}
                          >
                            <h3 className="tp-list-title">{t.name}</h3>
                            <p className="tp-list-desc">
                              {t.description ?? (
                                <em className="text-muted">No description</em>
                              )}
                            </p>
                          </button>
                        ))}
                      </div>
                    </section>
                  ))}
              </div>
            </div>

            <div className="modal-footer">
              <div className="tp-footer-meta">
                {selectedTemplate ? (
                  <>
                    Selected: <strong>{selectedTemplate.name}</strong>
                    {selectedTemplate.category && (
                      <>
                        {" "}
                        · {selectedTemplate.category}
                      </>
                    )}
                  </>
                ) : (
                  <>Pick a template to continue · double-click to use immediately</>
                )}
              </div>
              <button
                type="button"
                className="btn btn-outline-secondary"
                onClick={onCancel}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-theme"
                disabled={!selectedTemplate}
                onClick={confirm}
              >
                Use template
              </button>
            </div>
          </div>
        </div>
      </div>
      <div
        className="modal-backdrop fade show"
        onClick={onCancel}
        style={{ zIndex: 1060 }}
      />
    </>
  );
}
