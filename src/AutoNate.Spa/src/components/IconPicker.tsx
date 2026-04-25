import { useEffect, useId, useMemo, useRef, useState } from "react";
import { FA_ICONS, FaIcon, findIcon, preferredStyle, searchIcons, stripFaPrefix } from "@/lib/faIcons";

interface IconPickerProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  id?: string;
}

const MAX_RESULTS = 60;

export default function IconPicker({ value, onChange, placeholder, id }: IconPickerProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const containerRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);

  const query = stripFaPrefix(value);
  const results = useMemo(() => {
    return query ? searchIcons(query, MAX_RESULTS) : FA_ICONS.slice(0, MAX_RESULTS);
  }, [query]);

  const matchedIcon = useMemo(() => findIcon(value), [value]);

  useEffect(() => {
    if (highlight >= results.length) setHighlight(0);
  }, [results, highlight]);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  const selectIcon = (icon: FaIcon) => {
    onChange(`fa-${icon.name}`);
    setOpen(false);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setOpen(true);
      const next = Math.min(highlight + 1, results.length - 1);
      setHighlight(next);
      scrollHighlightIntoView(next);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      const next = Math.max(highlight - 1, 0);
      setHighlight(next);
      scrollHighlightIntoView(next);
    } else if (e.key === "Enter") {
      if (open && results[highlight]) {
        e.preventDefault();
        selectIcon(results[highlight]);
      }
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  };

  const scrollHighlightIntoView = (idx: number) => {
    requestAnimationFrame(() => {
      const list = listRef.current;
      if (!list) return;
      const el = list.querySelector<HTMLElement>(`[data-idx="${idx}"]`);
      el?.scrollIntoView({ block: "nearest" });
    });
  };

  const previewClass = matchedIcon
    ? `${preferredStyle(matchedIcon)} fa-${matchedIcon.name}`
    : value
    ? `fa ${value.startsWith("fa-") ? value : `fa-${value}`}`
    : "fa fa-question text-body text-opacity-25";

  return (
    <div ref={containerRef} className="position-relative">
      <div className="input-group">
        <span className="input-group-text" style={{ width: "2.75rem", justifyContent: "center" }}>
          <i className={previewClass} aria-hidden="true"></i>
        </span>
        <input
          id={inputId}
          className="form-control"
          autoComplete="off"
          spellCheck={false}
          placeholder={placeholder ?? "Search icons (e.g. building)"}
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
            setOpen(true);
            setHighlight(0);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
        />
      </div>
      {open && (
        <div
          ref={listRef}
          className="position-absolute bg-body border rounded shadow-sm w-100"
          style={{ zIndex: 1080, maxHeight: "18rem", overflowY: "auto", top: "100%" }}
          role="listbox"
        >
          {results.length === 0 ? (
            <div className="p-3 text-body text-opacity-50 small">No matching icons.</div>
          ) : (
            results.map((icon, idx) => {
              const style = preferredStyle(icon);
              const isHighlighted = idx === highlight;
              return (
                <button
                  key={icon.name}
                  type="button"
                  data-idx={idx}
                  role="option"
                  aria-selected={isHighlighted}
                  className={`d-flex align-items-center gap-2 w-100 text-start px-3 py-2 border-0 ${
                    isHighlighted ? "bg-primary bg-opacity-10" : "bg-transparent"
                  }`}
                  onMouseEnter={() => setHighlight(idx)}
                  onMouseDown={(e) => {
                    e.preventDefault();
                    selectIcon(icon);
                  }}
                >
                  <i
                    className={`${style} fa-${icon.name}`}
                    style={{ width: "1.25rem", textAlign: "center" }}
                    aria-hidden="true"
                  ></i>
                  <span className="font-monospace small">fa-{icon.name}</span>
                </button>
              );
            })
          )}
        </div>
      )}
    </div>
  );
}
