import { useDeferredValue, useEffect, useMemo, useState } from "react";
import { notesTheme } from "./notesTheme";

// Slim catalog row generated at build-time from FA's icon-families.json.
type FaIconRow = { name: string; label: string; terms: string[] };

type Props = {
  value: string;
  onChange: (faKey: string) => void;
};

// Searchable picker that covers the full FontAwesome 7 Free solid set
// (≈1,400 glyphs). The catalog is dynamic-imported so the JSON only loads
// when the picker actually mounts, keeping it out of the main bundle.
export function FaIconPicker({ value, onChange }: Props) {
  const [query, setQuery] = useState("");
  const deferred = useDeferredValue(query);
  const [catalog, setCatalog] = useState<FaIconRow[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    import("./faIconCatalog.generated.json")
      .then((mod) => {
        if (cancelled) return;
        const data = (mod.default ?? mod) as FaIconRow[];
        setCatalog(data);
      })
      .catch((err) => {
        if (cancelled) return;
        setLoadError(err instanceof Error ? err.message : "Failed to load icons");
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const filtered = useMemo(() => {
    if (!catalog) return [];
    const q = deferred.trim().toLowerCase();
    if (!q) return catalog;
    // Match against name first (cheapest), then label, then any search term.
    return catalog.filter((row) => {
      if (row.name.includes(q)) return true;
      if (row.label.toLowerCase().includes(q)) return true;
      return row.terms.some((t) => t.toLowerCase().includes(q));
    });
  }, [catalog, deferred]);

  // Cap visible results so the DOM stays cheap. 600 ≈ 60 rows of 10 cols and
  // is plenty for any sensible search — typing a few characters narrows below
  // this in practice.
  const VISIBLE_LIMIT = 600;
  const shown = filtered.slice(0, VISIBLE_LIMIT);
  const overflow = Math.max(0, filtered.length - VISIBLE_LIMIT);

  const selectedRow = catalog?.find((r) => `fa-${r.name}` === value) ?? null;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "6px 10px",
          border: `1px solid ${notesTheme.border}`,
          borderRadius: 4,
          background: "#fff"
        }}
      >
        <div
          style={{
            width: 36,
            height: 36,
            borderRadius: 6,
            background: notesTheme.selected,
            color: notesTheme.primary,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 16,
            flexShrink: 0
          }}
        >
          <i className={`fa ${value}`} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 12, fontWeight: 700, color: notesTheme.dark }}>
            {selectedRow?.label ?? value}
          </div>
          <div style={{ fontSize: 10.5, color: notesTheme.muted }}>
            <code style={{ background: notesTheme.hover, padding: "1px 5px", borderRadius: 3 }}>
              {value}
            </code>
          </div>
        </div>
      </div>

      <div style={{ position: "relative" }}>
        <i
          className="fa fa-magnifying-glass"
          style={{
            position: "absolute",
            left: 9,
            top: "50%",
            transform: "translateY(-50%)",
            color: notesTheme.muted,
            fontSize: 11
          }}
        />
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          // The visible label belongs to the picker as a whole (a group of
          // buttons), so this search box carries its own name rather than
          // borrowing one — otherwise it announces as "edit, blank" (#9).
          aria-label="Search icons"
          placeholder="Search icons (e.g. 'gear', 'user', 'rocket')"
          style={{
            width: "100%",
            border: `1px solid ${notesTheme.border}`,
            borderRadius: 4,
            padding: "7px 10px 7px 28px",
            fontSize: 12,
            outline: "none",
            fontFamily: "inherit",
            background: "#fff"
          }}
        />
      </div>

      <div
        style={{
          maxHeight: 240,
          overflowY: "auto",
          border: `1px solid ${notesTheme.border}`,
          borderRadius: 4,
          background: "#fff",
          padding: 6
        }}
      >
        {!catalog && !loadError && (
          <div style={loadingStyle}>
            <i className="fa fa-spinner fa-spin" style={{ marginRight: 6 }} />
            Loading icons…
          </div>
        )}
        {loadError && (
          <div style={{ ...loadingStyle, color: notesTheme.danger }}>
            <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
            {loadError}
          </div>
        )}
        {catalog && shown.length === 0 && (
          <div style={loadingStyle}>No icons match “{deferred}”.</div>
        )}
        {catalog && shown.length > 0 && (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(36px, 1fr))",
              gap: 4
            }}
          >
            {shown.map((row) => {
              const key = `fa-${row.name}`;
              const active = key === value;
              return (
                <button
                  key={row.name}
                  type="button"
                  onClick={() => onChange(key)}
                  // The glyph is decorative markup with no text, so without
                  // this the button has no accessible name at all; `title`
                  // alone is unreliable and invisible to touch users.
                  aria-label={row.label}
                  // Selection was signalled by border + background colour
                  // only — nothing a screen reader or a colour-blind user
                  // could perceive (#8, WCAG 1.4.1).
                  aria-pressed={active}
                  title={`${row.label} (${key})`}
                  style={{
                    aspectRatio: "1 / 1",
                    border: `1px solid ${active ? notesTheme.primary : "transparent"}`,
                    background: active ? notesTheme.selected : "transparent",
                    borderRadius: 4,
                    cursor: "pointer",
                    color: active ? notesTheme.primary : notesTheme.dark,
                    fontSize: 14
                  }}
                >
                  <i className={`fa ${key}`} />
                </button>
              );
            })}
          </div>
        )}
        {overflow > 0 && (
          <div style={overflowStyle}>
            Showing first {VISIBLE_LIMIT} of {filtered.length} matches — narrow your search to see
            the rest.
          </div>
        )}
      </div>
    </div>
  );
}

const loadingStyle: React.CSSProperties = {
  padding: 14,
  fontSize: 12,
  color: notesTheme.muted,
  textAlign: "center"
};

const overflowStyle: React.CSSProperties = {
  padding: "6px 4px 0",
  fontSize: 10.5,
  color: notesTheme.muted,
  fontStyle: "italic"
};
