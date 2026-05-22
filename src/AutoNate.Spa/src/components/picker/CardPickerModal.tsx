import {
  ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import { Button, Group, Modal, Text, TextInput } from "@mantine/core";
import "./CardPickerModal.css";

// Generic card-picker. Extracted from the original TemplatePickerModal so
// the widget picker (and any future "pick from a categorized catalog" UX)
// can reuse the same search + category rail + scroll-spy + thumbnail/list
// layout. Consumers map their domain objects to CardItem[] and supply a
// per-key onSelect callback.
export type CardItem = {
  key: string;
  name: string;
  description: string | null;
  category: string | null;
  // Either an image URL/dataURI or a fully-rendered preview node. Strings
  // are wrapped in <img>; nodes render as-is.
  thumbnail: string | ReactNode | null;
};

type ViewMode = "thumbnail" | "list";

const UNCATEGORIZED = "Uncategorized";

type CategoryGroup = { category: string; items: CardItem[] };

function groupByCategory(items: CardItem[]): CategoryGroup[] {
  const map = new Map<string, CardItem[]>();
  for (const t of items) {
    const key = (t.category && t.category.trim()) || UNCATEGORIZED;
    const bucket = map.get(key) ?? [];
    bucket.push(t);
    map.set(key, bucket);
  }
  for (const bucket of map.values()) {
    bucket.sort((a, b) => a.name.localeCompare(b.name));
  }
  const named = [...map.entries()]
    .filter(([k]) => k !== UNCATEGORIZED)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([category, bucket]) => ({ category, items: bucket }));
  const uncat = map.get(UNCATEGORIZED);
  if (uncat) named.push({ category: UNCATEGORIZED, items: uncat });
  return named;
}

function ThumbnailBox({
  item,
  size
}: {
  item: CardItem;
  size: "card" | "selected";
}) {
  const className = size === "card" ? "tp-card-thumb" : "tp-selected-thumb";
  if (typeof item.thumbnail === "string" && item.thumbnail) {
    return (
      <div className={className}>
        <img src={item.thumbnail} alt={`${item.name} preview`} />
      </div>
    );
  }
  if (item.thumbnail) {
    return <div className={className}>{item.thumbnail}</div>;
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

export type CardPickerModalProps = {
  items: CardItem[];
  selectedKey: string | null;
  title: string;
  subtitle?: string;
  searchPlaceholder?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  emptyHint?: string;
  onSelect: (item: CardItem) => void;
  onCancel: () => void;
  // Optional override for the metadata strip at the bottom-left. Receives
  // the selected item (or null) so consumers can show extra context.
  renderFooterMeta?: (item: CardItem | null) => ReactNode;
};

export function CardPickerModal({
  items,
  selectedKey,
  title,
  subtitle,
  searchPlaceholder = "Search",
  confirmLabel = "Use",
  cancelLabel = "Cancel",
  emptyHint,
  onSelect,
  onCancel,
  renderFooterMeta
}: CardPickerModalProps) {
  const [view, setView] = useState<ViewMode>("thumbnail");
  const [pendingKey, setPendingKey] = useState<string | null>(selectedKey);
  const [activeCategory, setActiveCategory] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const listRef = useRef<HTMLDivElement | null>(null);
  const sectionRefs = useRef<Record<string, HTMLElement | null>>({});

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        (t.description ?? "").toLowerCase().includes(q) ||
        (t.category ?? "").toLowerCase().includes(q)
    );
  }, [search, items]);

  const grouped = useMemo(() => groupByCategory(filtered), [filtered]);

  useEffect(() => {
    if (grouped.length === 0) {
      if (activeCategory !== null) setActiveCategory(null);
      return;
    }
    if (!activeCategory || !grouped.find((g) => g.category === activeCategory)) {
      setActiveCategory(grouped[0].category);
    }
  }, [grouped, activeCategory]);

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
      { root, rootMargin: "-10px 0px -75% 0px", threshold: 0 }
    );
    headers.forEach((h) => observer.observe(h));
    return () => observer.disconnect();
  }, [grouped, view]);

  const jumpTo = useCallback((category: string) => {
    setActiveCategory(category);
    const headerEl = sectionRefs.current[category];
    const root = listRef.current;
    if (!headerEl || !root) return;
    const sectionEl = headerEl.parentElement ?? headerEl;
    const elRect = sectionEl.getBoundingClientRect();
    const rootRect = root.getBoundingClientRect();
    const target = root.scrollTop + (elRect.top - rootRect.top) - 4;
    root.scrollTo({ top: Math.max(0, target), behavior: "smooth" });
  }, []);

  const selectedItem = useMemo(
    () => items.find((t) => t.key === pendingKey) ?? null,
    [items, pendingKey]
  );

  const confirm = useCallback(() => {
    if (selectedItem) onSelect(selectedItem);
  }, [selectedItem, onSelect]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onCancel();
      } else if (e.key === "Enter" && selectedItem) {
        const tag = (document.activeElement?.tagName ?? "").toLowerCase();
        if (tag !== "input" && tag !== "textarea") {
          e.preventDefault();
          onSelect(selectedItem);
        }
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onCancel, onSelect, selectedItem]);

  return (
    <Modal
      opened
      onClose={onCancel}
      title={
        <div className="tp-modal-title-block">
          <Text fw={600} size="lg">{title}</Text>
          {subtitle ? (
            <Text size="xs" c="dimmed" mt={4}>{subtitle}</Text>
          ) : null}
        </div>
      }
      size="xl"
      zIndex={1065}
      styles={{
        body: {
          padding: 0,
          display: "flex",
          flexDirection: "column",
          minHeight: 0,
          maxHeight: "70vh"
        }
      }}
    >
      <div
        className="tp-header-row"
        style={{
          display: "flex",
          gap: 8,
          padding: "8px 16px",
          alignItems: "center",
          borderBottom: "1px solid var(--mantine-color-default-border)"
        }}
      >
        <TextInput
          size="xs"
          leftSection={<i className="fa fa-search" aria-hidden="true" />}
          placeholder={searchPlaceholder}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          aria-label={searchPlaceholder}
          style={{ flex: 1, maxWidth: 320 }}
        />
        <Group gap={4} ml="auto">
          <Button
            size="xs"
            variant={view === "thumbnail" ? "filled" : "default"}
            onClick={() => setView("thumbnail")}
            aria-pressed={view === "thumbnail"}
            leftSection={<i className="fa fa-th-large" />}
          >
            Thumbnails
          </Button>
          <Button
            size="xs"
            variant={view === "list" ? "filled" : "default"}
            onClick={() => setView("list")}
            aria-pressed={view === "list"}
            leftSection={<i className="fa fa-list" />}
          >
            List
          </Button>
        </Group>
      </div>

      <div className="tp-body">
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
              className={`tp-cat${activeCategory === g.category ? " active" : ""}`}
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
              {search
                ? `No items match "${search}"`
                : emptyHint ?? "No items available"}
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
                  <span className="tp-section-count">· {g.items.length}</span>
                </header>
                <div className="tp-thumb-grid">
                  {g.items.map((t) => (
                    <button
                      key={t.key}
                      type="button"
                      className={`tp-card${pendingKey === t.key ? " selected" : ""}`}
                      onClick={() => setPendingKey(t.key)}
                      onDoubleClick={() => onSelect(t)}
                      aria-pressed={pendingKey === t.key}
                    >
                      <ThumbnailBox item={t} size="card" />
                      <div className="tp-card-body">
                        <h3 className="tp-card-title">{t.name}</h3>
                        <p className="tp-card-desc">
                          {t.description ?? <em>No description</em>}
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
                  <span className="tp-section-count">· {g.items.length}</span>
                </header>
                <div className="tp-list-grid">
                  {g.items.map((t) => (
                    <button
                      key={t.key}
                      type="button"
                      className={`tp-list-row${pendingKey === t.key ? " selected" : ""}`}
                      onClick={() => setPendingKey(t.key)}
                      onDoubleClick={() => onSelect(t)}
                      aria-pressed={pendingKey === t.key}
                    >
                      <h3 className="tp-list-title">{t.name}</h3>
                      <p className="tp-list-desc">
                        {t.description ?? <em>No description</em>}
                      </p>
                    </button>
                  ))}
                </div>
              </section>
            ))}
        </div>
      </div>

      <div
        className="tp-footer"
        style={{
          display: "flex",
          gap: 8,
          padding: "8px 16px",
          alignItems: "center",
          borderTop: "1px solid var(--mantine-color-default-border)"
        }}
      >
        <div
          className="tp-footer-meta"
          style={{ flex: 1, fontSize: 13, color: "var(--mantine-color-dimmed)" }}
        >
          {renderFooterMeta
            ? renderFooterMeta(selectedItem)
            : selectedItem ? (
                <>
                  Selected: <strong>{selectedItem.name}</strong>
                  {selectedItem.category && <> · {selectedItem.category}</>}
                </>
              ) : (
                <>Pick an item to continue · double-click to use immediately</>
              )}
        </div>
        <Button variant="default" onClick={onCancel}>{cancelLabel}</Button>
        <Button disabled={!selectedItem} onClick={confirm}>{confirmLabel}</Button>
      </div>
    </Modal>
  );
}
