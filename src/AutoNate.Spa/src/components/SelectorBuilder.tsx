import { useEffect, useMemo, useState } from "react";
import { RegistryKind } from "@/api/admin";
import { useRegistry } from "@/hooks/useAdmin";

// Visual selector builder. Emits canonical strings of the form
//
//     /<kind>/<idset>[<tag>=<value>;...]
//
// Falls back to a raw-string mode for advanced selectors the visual editor
// doesn't model. Multi-hop user predicates (e.g. supervisor walks) ARE
// modeled via the `nestedEdgeKind` field on TagFilter.

// Inner edge kinds the builder lets you nest from a `=user` value. The
// authorization engine accepts any user-to-user edge_kind, so this is
// just the curated set the dropdown surfaces.
const NESTED_USER_EDGE_KINDS = ["supervisor", "manager"] as const;

export type TagFilter = {
  tag: string;
  value: string;        // either "user" sentinel, a literal, or "*"
  isUserValue: boolean; // true => emit as `=user`; false => literal text
  nestedEdgeKind?: string; // when set with isUserValue, emits `=user[<edge>=user]`
};

export type SelectorBuilderValue = {
  kind: string;
  idMode: "any" | "specific";
  ids: string;          // comma-separated when specific
  tags: TagFilter[];
};

type Props = {
  // Raw selector string ("" for empty). Builder reflects + drives this.
  value: string;
  onChange: (next: string) => void;
  // Optional: restrict the kind dropdown to certain kinds.
  allowedKinds?: string[];
};

export default function SelectorBuilder({ value, onChange, allowedKinds }: Props) {
  const { data, isLoading } = useRegistry();
  const [mode, setMode] = useState<"visual" | "raw">("visual");
  const [state, setState] = useState<SelectorBuilderValue>(() => parseSelector(value));

  // Reflect external value changes back into the builder when in visual mode.
  // Uses the functional setState form so we can compare against the latest
  // state without capturing it in the effect's closure — that lets the dep
  // array stay [value, mode] without an eslint suppression and without
  // looping on our own setState calls.
  useEffect(() => {
    if (mode !== "visual") return;
    const parsed = parseSelector(value);
    setState((prev) => (sameState(parsed, prev) ? prev : parsed));
  }, [value, mode]);

  const kinds = useMemo<RegistryKind[]>(() => {
    const all = data?.kinds ?? [];
    return allowedKinds ? all.filter((k) => allowedKinds.includes(k.kind)) : all;
  }, [data, allowedKinds]);

  const currentKind = kinds.find((k) => k.kind === state.kind);

  const apply = (next: SelectorBuilderValue) => {
    setState(next);
    onChange(buildSelector(next));
  };

  if (mode === "raw") {
    return (
      <div className="d-flex flex-column gap-2">
        <input
          className="form-control font-monospace"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="/record/*[assignee=user]"
        />
        <div>
          <button type="button" className="btn btn-link p-0" onClick={() => setMode("visual")}>
            ← back to visual builder
          </button>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return <div className="text-muted">Loading registry…</div>;
  }

  return (
    <div className="d-flex flex-column gap-2">
      <div className="row g-2">
        <div className="col-sm-3">
          <label className="form-label small mb-1">Kind</label>
          <select
            className="form-select form-select-sm"
            value={state.kind}
            onChange={(e) => apply({ ...state, kind: e.target.value, tags: [] })}
          >
            <option value="">— pick a kind —</option>
            {kinds.map((k) => (
              <option key={k.kind} value={k.kind}>{k.kind}</option>
            ))}
          </select>
        </div>
        <div className="col-sm-3">
          <label className="form-label small mb-1">IDs</label>
          <select
            className="form-select form-select-sm"
            value={state.idMode}
            onChange={(e) => apply({ ...state, idMode: e.target.value as "any" | "specific" })}
          >
            <option value="any">any (*)</option>
            <option value="specific">specific…</option>
          </select>
        </div>
        {state.idMode === "specific" && (
          <div className="col-sm-6">
            <label className="form-label small mb-1">comma-separated</label>
            <input
              className="form-control form-control-sm font-monospace"
              value={state.ids}
              onChange={(e) => apply({ ...state, ids: e.target.value })}
              placeholder="abc-123, def-456"
            />
          </div>
        )}
      </div>

      {currentKind && currentKind.tags.length > 0 && (
        <div>
          <label className="form-label small mb-1">Tag predicates</label>
          {state.tags.map((t, i) => (
            <div key={i} className="row g-2 mb-1 align-items-center">
              <div className="col-sm-3">
                <select
                  className="form-select form-select-sm"
                  value={t.tag}
                  onChange={(e) => {
                    const tags = [...state.tags];
                    tags[i] = { ...tags[i], tag: e.target.value };
                    apply({ ...state, tags });
                  }}
                >
                  <option value="">— tag —</option>
                  {currentKind.tags.map((tg: string) => (
                    <option key={tg} value={tg}>{tg}</option>
                  ))}
                </select>
              </div>
              <div className="col-sm-1 text-center">=</div>
              <div className="col-sm-2">
                <select
                  className="form-select form-select-sm"
                  value={t.isUserValue ? "user" : "literal"}
                  onChange={(e) => {
                    const tags = [...state.tags];
                    const isUserValue = e.target.value === "user";
                    tags[i] = {
                      ...tags[i],
                      isUserValue,
                      // Clear nesting when leaving user mode.
                      nestedEdgeKind: isUserValue ? tags[i].nestedEdgeKind : undefined
                    };
                    apply({ ...state, tags });
                  }}
                >
                  <option value="user">current user</option>
                  <option value="literal">literal value</option>
                </select>
              </div>
              {!t.isUserValue && (
                <div className="col-sm-4">
                  <input
                    className="form-control form-control-sm font-monospace"
                    value={t.value}
                    onChange={(e) => {
                      const tags = [...state.tags];
                      tags[i] = { ...tags[i], value: e.target.value };
                      apply({ ...state, tags });
                    }}
                    placeholder="value"
                  />
                </div>
              )}
              {t.isUserValue && (
                <div className="col-sm-4">
                  <select
                    className="form-select form-select-sm"
                    value={t.nestedEdgeKind ?? ""}
                    onChange={(e) => {
                      const tags = [...state.tags];
                      const next = e.target.value;
                      tags[i] = { ...tags[i], nestedEdgeKind: next === "" ? undefined : next };
                      apply({ ...state, tags });
                    }}
                    title="Optionally walk one more user→user edge from the actor."
                  >
                    <option value="">— the actor themselves —</option>
                    {NESTED_USER_EDGE_KINDS.map((edge) => (
                      <option key={edge} value={edge}>
                        someone the actor's {edge} of (=user[{edge}=user])
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className="col-sm-2">
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger w-100"
                  onClick={() => {
                    const tags = state.tags.filter((_, idx) => idx !== i);
                    apply({ ...state, tags });
                  }}
                >
                  Remove
                </button>
              </div>
            </div>
          ))}
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary mt-1"
            onClick={() =>
              apply({ ...state, tags: [...state.tags, { tag: currentKind.tags[0], value: "", isUserValue: true }] })
            }
          >
            + add tag predicate
          </button>
        </div>
      )}

      <div className="d-flex justify-content-between align-items-center mt-1">
        <code className="text-muted small">{value || "(empty)"}</code>
        <button type="button" className="btn btn-link p-0 small" onClick={() => setMode("raw")}>
          edit raw →
        </button>
      </div>
    </div>
  );
}

// ---------- pure helpers ----------

export function buildSelector(state: SelectorBuilderValue): string {
  if (!state.kind) return "";

  const ids =
    state.idMode === "any"
      ? "*"
      : splitIds(state.ids).length === 1
        ? splitIds(state.ids)[0]
        : `{${splitIds(state.ids).join(",")}}`;

  const path = `/${state.kind}/${ids}`;

  const usable = state.tags.filter((t) => t.tag && (t.isUserValue || t.value));
  if (usable.length === 0) return path;

  const exprs = usable.map((t) => {
    if (t.isUserValue) {
      const inner = t.nestedEdgeKind?.trim();
      return inner ? `${t.tag}=user[${inner}=user]` : `${t.tag}=user`;
    }
    return `${t.tag}=${t.value}`;
  });
  return `${path}[${exprs.join(";")}]`;
}

function splitIds(raw: string): string[] {
  return raw
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

// Best-effort visual reflection of an incoming raw string. Recognizes the
// shapes the builder emits — including `<tag>=user[<edge>=user]` — and
// returns an empty state for anything more complex (which pops the user
// into raw mode if they want it).
export function parseSelector(raw: string): SelectorBuilderValue {
  const empty: SelectorBuilderValue = { kind: "", idMode: "any", ids: "", tags: [] };
  if (!raw) return empty;

  // Match the path part and the optional outer predicate body. The body may
  // contain nested `[…]` brackets for multi-hop forms, so capture greedily
  // up to the final `]`.
  const match = raw.match(/^\/([a-zA-Z][\w-]*)\/(?:\*|([^[/]+))(?:\[(.*)\])?$/);
  if (!match) return empty;

  const [, kind, idsRaw, predBody] = match;
  const idMode: "any" | "specific" = idsRaw ? "specific" : "any";
  let ids = "";
  if (idsRaw) {
    if (idsRaw.startsWith("{") && idsRaw.endsWith("}")) {
      ids = idsRaw.slice(1, -1);
    } else {
      ids = idsRaw;
    }
  }

  const tags: TagFilter[] = [];
  if (predBody) {
    const parts = splitTopLevel(predBody);
    for (const p of parts) {
      const eq = p.indexOf("=");
      if (eq <= 0) return empty; // unsupported shape
      const tag = p.slice(0, eq).trim();
      const rest = p.slice(eq + 1).trim();
      if (!tag) return empty;

      // Multi-hop shape: `user[<innerEdge>=user]`
      const nestedMatch = rest.match(/^user\[([a-zA-Z][\w-]*)=user\]$/);
      if (nestedMatch) {
        tags.push({ tag, value: "", isUserValue: true, nestedEdgeKind: nestedMatch[1] });
        continue;
      }

      const isUserValue = rest === "user";
      // Anything else with brackets is beyond what the visual builder models.
      if (rest.includes("[") || rest.includes("]")) return empty;

      tags.push({
        tag,
        value: isUserValue ? "" : rest,
        isUserValue
      });
    }
  }

  return { kind, idMode, ids, tags };
}

// Splits a predicate body on top-level `;` or `,`, ignoring separators that
// fall inside nested `[]` so shapes like `assignee=user[supervisor=user]`
// stay together.
function splitTopLevel(body: string): string[] {
  const out: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < body.length; i++) {
    const ch = body[i];
    if (ch === "[") depth++;
    else if (ch === "]") depth = Math.max(0, depth - 1);
    else if ((ch === ";" || ch === ",") && depth === 0) {
      out.push(body.slice(start, i));
      start = i + 1;
    }
  }
  out.push(body.slice(start));
  return out.map((p) => p.trim()).filter((p) => p.length > 0);
}

function sameState(a: SelectorBuilderValue, b: SelectorBuilderValue): boolean {
  if (a.kind !== b.kind || a.idMode !== b.idMode || a.ids !== b.ids) return false;
  if (a.tags.length !== b.tags.length) return false;
  for (let i = 0; i < a.tags.length; i++) {
    const x = a.tags[i];
    const y = b.tags[i];
    if (
      x.tag !== y.tag ||
      x.isUserValue !== y.isUserValue ||
      x.value !== y.value ||
      (x.nestedEdgeKind ?? "") !== (y.nestedEdgeKind ?? "")
    ) {
      return false;
    }
  }
  return true;
}
