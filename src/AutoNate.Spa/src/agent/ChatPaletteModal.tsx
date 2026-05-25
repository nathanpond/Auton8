import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Badge, Box, Modal, ScrollArea, Text, TextInput, UnstyledButton } from "@mantine/core";
import { AgentConversation } from "./types";
import { listConversations } from "./api";
import { KNOWN_PAGE_KEYS, pageKeyCrumb, pageKeyLabel } from "./pageLabels";

type RangeKey = "7d" | "30d" | "all";

const RANGE_MS: Record<RangeKey, number | null> = {
  "7d": 7 * 24 * 60 * 60 * 1000,
  "30d": 30 * 24 * 60 * 60 * 1000,
  all: null
};

type Props = {
  open: boolean;
  initialQuery?: string;
  onClose: () => void;
  onPick: (chat: AgentConversation) => void;
};

export function ChatPaletteModal({ open, initialQuery, onClose, onPick }: Props) {
  const [query, setQuery] = useState("");
  const [scope, setScope] = useState<string>("all");
  const [range, setRange] = useState<RangeKey>("30d");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Fetch every chat for the user. Disabled while closed so we don't pay the
  // cost on every page load — only when the user actually opens the palette.
  const allChatsQuery = useQuery({
    queryKey: ["agent", "conversations", "all"],
    queryFn: ({ signal }) => listConversations(null, signal),
    enabled: open,
    staleTime: 30_000
  });

  useEffect(() => {
    if (!open) return;
    setQuery(initialQuery ?? "");
    setScope("all");
    setSelectedId(null);
    // Mantine's Modal focuses its first focusable element by default; give
    // the input a tick so the focus lands inside the palette search, not
    // the modal close button.
    const t = window.setTimeout(() => inputRef.current?.focus(), 60);
    return () => window.clearTimeout(t);
  }, [open, initialQuery]);

  const filtered = useMemo<AgentConversation[]>(() => {
    const rows = allChatsQuery.data ?? [];
    const q = query.trim().toLowerCase();
    const rangeMs = RANGE_MS[range];
    const cutoff = rangeMs == null ? null : Date.now() - rangeMs;
    return rows.filter((c) => {
      if (scope !== "all" && c.pageKey !== scope) return false;
      if (cutoff != null) {
        const ts = c.lastMessageAtUtc ?? c.updatedAtUtc;
        if (ts && new Date(ts).getTime() < cutoff) return false;
      }
      if (!q) return true;
      const hay = [c.title ?? "", c.pageKey, pageKeyLabel(c.pageKey)]
        .join(" ")
        .toLowerCase();
      return hay.includes(q);
    });
  }, [allChatsQuery.data, query, scope, range]);

  const grouped = useMemo(() => {
    const byKey = new Map<string, AgentConversation[]>();
    for (const c of filtered) {
      const key = c.pageKey || "default";
      const arr = byKey.get(key) ?? [];
      arr.push(c);
      byKey.set(key, arr);
    }
    return Array.from(byKey.entries()).map(([key, chats]) => ({ pageKey: key, chats }));
  }, [filtered]);

  // Auto-select the first result whenever the visible list changes.
  useEffect(() => {
    if (filtered.length === 0) {
      setSelectedId(null);
      return;
    }
    if (!filtered.find((c) => c.id === selectedId)) {
      setSelectedId(filtered[0].id);
    }
  }, [filtered, selectedId]);

  const move = useCallback(
    (dir: 1 | -1) => {
      if (filtered.length === 0) return;
      const idx = filtered.findIndex((c) => c.id === selectedId);
      const next =
        dir > 0 ? Math.min(filtered.length - 1, idx + 1) : Math.max(0, idx - 1);
      setSelectedId(filtered[Math.max(next, 0)].id);
    },
    [filtered, selectedId]
  );

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      move(1);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      move(-1);
    } else if (e.key === "Enter") {
      const chat = filtered.find((c) => c.id === selectedId);
      if (chat) {
        e.preventDefault();
        onPick(chat);
      }
    }
  };

  const totalChats = filtered.length;
  const pagesShown = grouped.length;

  return (
    <Modal
      opened={open}
      onClose={onClose}
      withCloseButton={false}
      size={760}
      padding={0}
      yOffset="80px"
      // Lift above the agent sidebar (z=1010/1030) and the top nav (z=1015)
      // so the palette is the only thing the user can see while it's open.
      zIndex={1100}
      classNames={{
        body: "chat-palette__body",
        content: "chat-palette__content"
      }}
      overlayProps={{ backgroundOpacity: 0.45, blur: 2 }}
      transitionProps={{ transition: "pop", duration: 160 }}
      aria-label="Find a chat"
    >
      <div className="chat-palette" onKeyDown={onKeyDown}>
        <div className="chat-palette__head">
          <TextInput
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.currentTarget.value)}
            placeholder="Search every chat by title or page…"
            leftSection={<i className="fa fa-magnifying-glass" />}
            variant="unstyled"
            size="md"
            aria-label="Search chats"
            className="chat-palette__search-input"
            styles={{ input: { fontSize: 15 } }}
          />
          <span className="chat-palette__kbd" aria-hidden>
            esc
          </span>
        </div>

        <div className="chat-palette__filters">
          <span className="chat-palette__label-pre">Page</span>
          <ScopeChip active={scope === "all"} onClick={() => setScope("all")}>
            All pages
          </ScopeChip>
          {KNOWN_PAGE_KEYS.map((k) => (
            <ScopeChip key={k} active={scope === k} onClick={() => setScope(k)}>
              {pageKeyLabel(k)}
            </ScopeChip>
          ))}
          <span className="chat-palette__label-pre" style={{ marginLeft: 8 }}>
            When
          </span>
          <ScopeChip active={range === "7d"} onClick={() => setRange("7d")}>
            7d
          </ScopeChip>
          <ScopeChip active={range === "30d"} onClick={() => setRange("30d")}>
            30d
          </ScopeChip>
          <ScopeChip active={range === "all"} onClick={() => setRange("all")}>
            All time
          </ScopeChip>
          <span className="chat-palette__filters-right">
            {totalChats} chat{totalChats === 1 ? "" : "s"} · {pagesShown} page
            {pagesShown === 1 ? "" : "s"}
          </span>
        </div>

        <ScrollArea.Autosize mah={460} className="chat-palette__results">
          {allChatsQuery.isLoading && (
            <Box p="lg">
              <Text c="dimmed" size="sm">
                Loading chats…
              </Text>
            </Box>
          )}
          {!allChatsQuery.isLoading && totalChats === 0 && (
            <div className="chat-palette__empty">
              <i className="fa-regular fa-circle-question" />
              <div className="chat-palette__empty-head">No chats match</div>
              <Text c="dimmed" size="sm">
                Try a different search term or widen the page scope.
              </Text>
            </div>
          )}
          {grouped.map((g) => (
            <div key={g.pageKey}>
              <div className="chat-palette__group">
                <PageCrumb pageKey={g.pageKey} />
                <span className="chat-palette__group-count">{g.chats.length}</span>
              </div>
              {g.chats.map((c) => {
                const ts = c.lastMessageAtUtc ?? c.updatedAtUtc;
                return (
                  <UnstyledButton
                    key={c.id}
                    className={
                      "chat-palette__result" +
                      (selectedId === c.id ? " chat-palette__result--selected" : "")
                    }
                    onMouseEnter={() => setSelectedId(c.id)}
                    onClick={() => onPick(c)}
                  >
                    <div className="chat-palette__result-main">
                      <div className="chat-palette__result-title">
                        {highlight(c.title ?? "Untitled chat", query)}
                      </div>
                      {c.lastMessagePreview && (
                        <div className="chat-palette__result-preview">
                          {highlight(c.lastMessagePreview, query)}
                        </div>
                      )}
                    </div>
                    <div className="chat-palette__result-meta">
                      <span className="chat-palette__result-date">
                        {formatTimestamp(ts)}
                      </span>
                      <span className="chat-palette__result-relative">
                        {formatRelative(ts)}
                      </span>
                    </div>
                  </UnstyledButton>
                );
              })}
            </div>
          ))}
        </ScrollArea.Autosize>

        <div className="chat-palette__foot">
          <span>
            <kbd>↑</kbd>
            <kbd>↓</kbd> navigate
          </span>
          <span>
            <kbd>↵</kbd> load chat into current page
          </span>
          <span>
            <kbd>esc</kbd> close
          </span>
          <span className="chat-palette__foot-grow" />
          <span style={{ color: "var(--mantine-color-dimmed)" }}>
            Loading a chat doesn't change which page owns it.
          </span>
        </div>
      </div>
    </Modal>
  );
}

function ScopeChip({
  active,
  onClick,
  children
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      className={"chat-palette__chip" + (active ? " chat-palette__chip--active" : "")}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

function PageCrumb({ pageKey }: { pageKey: string }) {
  const parts = pageKeyCrumb(pageKey);
  return (
    <span className="chat-palette__crumb">
      {parts.map((seg, i) => (
        <span key={i}>
          {i > 0 && <i className="fa-solid fa-chevron-right" aria-hidden />}
          <span
            className={
              "chat-palette__crumb-seg" +
              (i === parts.length - 1 ? " chat-palette__crumb-seg--active" : "")
            }
          >
            {seg}
          </span>
        </span>
      ))}
      {parts.length === 1 && pageKey === "default" && (
        <Badge size="xs" variant="light" color="gray" ml={6}>
          unscoped
        </Badge>
      )}
    </span>
  );
}

function highlight(text: string, query: string): React.ReactNode {
  const q = query.trim();
  if (!q) return text;
  const re = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")})`, "ig");
  const parts = text.split(re);
  return parts.map((p, i) =>
    re.test(p) ? (
      <mark key={i} className="chat-palette__mark">
        {p}
      </mark>
    ) : (
      <span key={i}>{p}</span>
    )
  );
}

function formatTimestamp(ts: string | null | undefined): string {
  if (!ts) return "—";
  const date = new Date(ts);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleString();
}

function formatRelative(ts: string | null | undefined): string {
  if (!ts) return "no activity";
  const date = new Date(ts);
  if (Number.isNaN(date.getTime())) return "no activity";
  const delta = Date.now() - date.getTime();
  const sec = Math.round(delta / 1000);
  if (sec < 60) return "just now";
  const min = Math.round(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.round(hr / 24);
  if (day < 30) return `${day}d ago`;
  const mon = Math.round(day / 30);
  if (mon < 12) return `${mon}mo ago`;
  const yr = Math.round(mon / 12);
  return `${yr}y ago`;
}
