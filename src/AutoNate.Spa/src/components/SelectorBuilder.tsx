import { useEffect, useMemo, useState } from "react";
import { Anchor, Box, Button, Code, Grid, Group, Select, Stack, Text, TextInput } from "@mantine/core";
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
  value: string;
  isUserValue: boolean;
  nestedEdgeKind?: string;
};

export type SelectorBuilderValue = {
  kind: string;
  idMode: "any" | "specific";
  ids: string;
  tags: TagFilter[];
};

type Props = {
  value: string;
  onChange: (next: string) => void;
  allowedKinds?: string[];
};

const MONOSPACE = { fontFamily: "var(--mantine-font-family-monospace)" } as const;

export default function SelectorBuilder({ value, onChange, allowedKinds }: Props) {
  const { data, isLoading } = useRegistry();
  const [mode, setMode] = useState<"visual" | "raw">("visual");
  const [state, setState] = useState<SelectorBuilderValue>(() => parseSelector(value));

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
      <Stack gap="xs">
        <TextInput
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder="/record/*[assignee=user]"
          styles={{ input: MONOSPACE }}
        />
        <Anchor component="button" type="button" onClick={() => setMode("visual")}>
          ← back to visual builder
        </Anchor>
      </Stack>
    );
  }

  if (isLoading) {
    return (
      <Text c="dimmed" size="sm">
        Loading registry…
      </Text>
    );
  }

  return (
    <Stack gap="xs">
      <Grid>
        <Grid.Col span={{ base: 12, sm: 3 }}>
          <Select
            label="Kind"
            size="xs"
            value={state.kind || null}
            onChange={(v) => apply({ ...state, kind: v ?? "", tags: [] })}
            placeholder="— pick a kind —"
            data={kinds.map((k) => k.kind)}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 3 }}>
          <Select
            label="IDs"
            size="xs"
            value={state.idMode}
            onChange={(v) => apply({ ...state, idMode: (v as "any" | "specific") ?? "any" })}
            data={[
              { value: "any", label: "any (*)" },
              { value: "specific", label: "specific…" }
            ]}
            allowDeselect={false}
          />
        </Grid.Col>
        {state.idMode === "specific" && (
          <Grid.Col span={{ base: 12, sm: 6 }}>
            <TextInput
              label="comma-separated"
              size="xs"
              value={state.ids}
              onChange={(e) => apply({ ...state, ids: e.currentTarget.value })}
              placeholder="abc-123, def-456"
              styles={{ input: MONOSPACE }}
            />
          </Grid.Col>
        )}
      </Grid>

      {currentKind && currentKind.tags.length > 0 && (
        <Box>
          <Text size="xs" fw={500} mb={4}>
            Tag predicates
          </Text>
          {state.tags.map((t, i) => (
            <Grid mb={4} align="center" key={i}>
              <Grid.Col span={{ base: 12, sm: 3 }}>
                <Select
                  size="xs"
                  value={t.tag || null}
                  onChange={(v) => {
                    const tags = [...state.tags];
                    tags[i] = { ...tags[i], tag: v ?? "" };
                    apply({ ...state, tags });
                  }}
                  placeholder="— tag —"
                  data={currentKind.tags as string[]}
                />
              </Grid.Col>
              <Grid.Col span={{ base: 12, sm: 1 }} ta="center">
                =
              </Grid.Col>
              <Grid.Col span={{ base: 12, sm: 2 }}>
                <Select
                  size="xs"
                  value={t.isUserValue ? "user" : "literal"}
                  onChange={(v) => {
                    const tags = [...state.tags];
                    const isUserValue = v === "user";
                    tags[i] = {
                      ...tags[i],
                      isUserValue,
                      nestedEdgeKind: isUserValue ? tags[i].nestedEdgeKind : undefined
                    };
                    apply({ ...state, tags });
                  }}
                  data={[
                    { value: "user", label: "current user" },
                    { value: "literal", label: "literal value" }
                  ]}
                  allowDeselect={false}
                />
              </Grid.Col>
              {!t.isUserValue && (
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <TextInput
                    size="xs"
                    value={t.value}
                    onChange={(e) => {
                      const tags = [...state.tags];
                      tags[i] = { ...tags[i], value: e.currentTarget.value };
                      apply({ ...state, tags });
                    }}
                    placeholder="value"
                    styles={{ input: MONOSPACE }}
                  />
                </Grid.Col>
              )}
              {t.isUserValue && (
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <Select
                    size="xs"
                    value={t.nestedEdgeKind ?? ""}
                    onChange={(v) => {
                      const tags = [...state.tags];
                      tags[i] = { ...tags[i], nestedEdgeKind: v ?? undefined };
                      apply({ ...state, tags });
                    }}
                    placeholder="— the actor themselves —"
                    clearable
                    data={NESTED_USER_EDGE_KINDS.map((edge) => ({
                      value: edge,
                      label: `someone the actor's ${edge} of (=user[${edge}=user])`
                    }))}
                    title="Optionally walk one more user→user edge from the actor."
                  />
                </Grid.Col>
              )}
              <Grid.Col span={{ base: 12, sm: 2 }}>
                <Button
                  size="xs"
                  variant="outline"
                  color="red"
                  fullWidth
                  onClick={() => {
                    const tags = state.tags.filter((_, idx) => idx !== i);
                    apply({ ...state, tags });
                  }}
                >
                  Remove
                </Button>
              </Grid.Col>
            </Grid>
          ))}
          <Button
            size="xs"
            variant="outline"
            color="gray"
            mt={4}
            onClick={() =>
              apply({
                ...state,
                tags: [...state.tags, { tag: currentKind.tags[0], value: "", isUserValue: true }]
              })
            }
          >
            + add tag predicate
          </Button>
        </Box>
      )}

      <Group justify="space-between" align="center" mt={4}>
        <Code c="dimmed">{value || "(empty)"}</Code>
        <Anchor component="button" type="button" size="sm" onClick={() => setMode("raw")}>
          edit raw →
        </Anchor>
      </Group>
    </Stack>
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

export function parseSelector(raw: string): SelectorBuilderValue {
  const empty: SelectorBuilderValue = { kind: "", idMode: "any", ids: "", tags: [] };
  if (!raw) return empty;

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
      if (eq <= 0) return empty;
      const tag = p.slice(0, eq).trim();
      const rest = p.slice(eq + 1).trim();
      if (!tag) return empty;

      const nestedMatch = rest.match(/^user\[([a-zA-Z][\w-]*)=user\]$/);
      if (nestedMatch) {
        tags.push({ tag, value: "", isUserValue: true, nestedEdgeKind: nestedMatch[1] });
        continue;
      }

      const isUserValue = rest === "user";
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
