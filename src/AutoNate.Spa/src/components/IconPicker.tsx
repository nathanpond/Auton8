import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Box, Input, TextInput } from "@mantine/core";
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
    : "fa fa-question";
  const previewOpacity = matchedIcon || value ? 1 : 0.25;

  return (
    <Box ref={containerRef} style={{ position: "relative" }}>
      <TextInput
        id={inputId}
        autoComplete="off"
        spellCheck={false}
        placeholder={placeholder ?? "Search icons (e.g. building)"}
        value={value}
        onChange={(e) => {
          onChange(e.currentTarget.value);
          setOpen(true);
          setHighlight(0);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
        leftSection={
          <i className={previewClass} aria-hidden="true" style={{ opacity: previewOpacity }} />
        }
      />
      {open && (
        <Box
          ref={listRef}
          role="listbox"
          style={{
            position: "absolute",
            top: "100%",
            left: 0,
            right: 0,
            zIndex: 1080,
            maxHeight: "18rem",
            overflowY: "auto",
            background: "var(--mantine-color-body)",
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-default)",
            boxShadow: "var(--mantine-shadow-sm)",
            marginTop: 4
          }}
        >
          {results.length === 0 ? (
            <Input.Description p="sm">No matching icons.</Input.Description>
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
                  onMouseEnter={() => setHighlight(idx)}
                  onMouseDown={(e) => {
                    e.preventDefault();
                    selectIcon(icon);
                  }}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    width: "100%",
                    textAlign: "left",
                    padding: "0.5rem 0.75rem",
                    border: 0,
                    background: isHighlighted
                      ? "var(--mantine-color-default-hover)"
                      : "transparent",
                    color: "inherit",
                    cursor: "pointer"
                  }}
                >
                  <i
                    className={`${style} fa-${icon.name}`}
                    style={{ width: "1.25rem", textAlign: "center" }}
                    aria-hidden="true"
                  />
                  <Box component="span" style={{ fontFamily: "var(--mantine-font-family-monospace)", fontSize: 13 }}>
                    fa-{icon.name}
                  </Box>
                </button>
              );
            })
          )}
        </Box>
      )}
    </Box>
  );
}
