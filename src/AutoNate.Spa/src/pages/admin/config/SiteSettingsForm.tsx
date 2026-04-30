import { useEffect, useMemo, useState } from "react";
import {
  useAdminSiteSettings,
  useUpdateSiteSettings
} from "@/hooks/useSiteSettings";
import { SettingDefinition, SiteSettingGroup } from "@/api/siteSettings";

type Props = {
  group: SiteSettingGroup;
  title: string;
  blurb: string;
};

// Renders every SettingDefinition in `group` as an editable form. Adding new
// settings to that group only requires adding a SettingDefinition on the
// backend — no UI changes here.
export default function SiteSettingsForm({ group, title, blurb }: Props) {
  const { data, isLoading, isError, error } = useAdminSiteSettings();
  const update = useUpdateSiteSettings();

  const definitions = useMemo<SettingDefinition[]>(
    () => (Array.isArray(data?.definitions) ? data!.definitions : [])
      .filter((d) => d.group === group),
    [data, group]
  );

  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  // Hydrate the draft from server values once they load (and again any time
  // an update mutation refreshes the cache).
  useEffect(() => {
    if (!data || !Array.isArray(data.definitions)) return;
    const values = data.values ?? {};
    const next: Record<string, unknown> = {};
    for (const def of data.definitions) {
      if (def.group !== group) continue;
      next[def.key] = values[def.key];
    }
    setDraft(next);
  }, [data, group]);

  const dirty = useMemo(() => {
    if (!data) return false;
    for (const def of definitions) {
      if (!Object.is(draft[def.key], data.values[def.key])) return true;
    }
    return false;
  }, [data, definitions, draft]);

  const setValue = (key: string, value: unknown) => {
    setDraft((prev) => ({ ...prev, [key]: value }));
    setSaveMessage(null);
  };

  const handleSave = async () => {
    if (!data || !dirty) return;
    setSaveMessage(null);
    const updates: Record<string, unknown> = {};
    for (const def of definitions) {
      if (!Object.is(draft[def.key], data.values[def.key])) {
        updates[def.key] = draft[def.key];
      }
    }
    try {
      await update.mutateAsync(updates);
      setSaveMessage("Settings saved.");
    } catch (err) {
      setSaveMessage(err instanceof Error ? err.message : "Failed to save.");
    }
  };

  const handleReset = () => {
    if (!data) return;
    const next: Record<string, unknown> = {};
    for (const def of definitions) next[def.key] = data.values[def.key];
    setDraft(next);
    setSaveMessage(null);
  };

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">{title}</h1>
          <p className="page-head-copy">{blurb}</p>
        </div>
        <div className="page-head-actions d-flex gap-2">
          <button
            type="button"
            className="btn btn-outline-secondary"
            onClick={handleReset}
            disabled={!dirty || update.isPending}
          >
            Reset
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => void handleSave()}
            disabled={!dirty || update.isPending}
          >
            {update.isPending ? "Saving…" : "Save changes"}
          </button>
        </div>
      </div>

      {isLoading && <div className="panel panel-inverse"><div className="panel-body text-muted">Loading…</div></div>}

      {isError && (
        <div className="panel panel-inverse">
          <div className="panel-body text-danger">
            Failed to load settings: {(error as Error)?.message ?? "unknown error"}
          </div>
        </div>
      )}

      {!isLoading && !isError && definitions.length === 0 && (
        <div className="panel panel-inverse">
          <div className="panel-body text-muted">
            No settings in this group yet.
          </div>
        </div>
      )}

      {!isLoading && !isError && definitions.length > 0 && (
        <div className="panel panel-inverse">
          <div className="panel-body">
            {definitions.map((def, idx) => (
              <div
                key={def.key}
                className={idx > 0 ? "pt-3 mt-3 border-top" : ""}
              >
                <SettingControl
                  definition={def}
                  value={draft[def.key]}
                  onChange={(v) => setValue(def.key, v)}
                />
              </div>
            ))}
          </div>
        </div>
      )}

      {saveMessage && (
        <div className="mt-2 small text-muted" role="status">
          {saveMessage}
        </div>
      )}
    </>
  );
}

function SettingControl({
  definition,
  value,
  onChange
}: {
  definition: SettingDefinition;
  value: unknown;
  onChange: (next: unknown) => void;
}) {
  if (definition.type === "bool") {
    const checked = typeof value === "boolean" ? value : Boolean(definition.defaultValue);
    return (
      <div className="form-check form-switch">
        <input
          className="form-check-input"
          type="checkbox"
          role="switch"
          id={`setting-${definition.key}`}
          checked={checked}
          onChange={(e) => onChange(e.currentTarget.checked)}
        />
        <label className="form-check-label" htmlFor={`setting-${definition.key}`}>
          <strong>{definition.label}</strong>
        </label>
        {definition.description && (
          <div className="form-text">{definition.description}</div>
        )}
      </div>
    );
  }

  if (definition.type === "string") {
    const text = typeof value === "string" ? value : String(definition.defaultValue ?? "");
    return (
      <div>
        <label className="form-label" htmlFor={`setting-${definition.key}`}>
          <strong>{definition.label}</strong>
        </label>
        <input
          id={`setting-${definition.key}`}
          type="text"
          className="form-control"
          value={text}
          onChange={(e) => onChange(e.currentTarget.value)}
        />
        {definition.description && (
          <div className="form-text">{definition.description}</div>
        )}
      </div>
    );
  }

  if (definition.type === "int") {
    const num = typeof value === "number" ? value : Number(definition.defaultValue ?? 0);
    return (
      <div>
        <label className="form-label" htmlFor={`setting-${definition.key}`}>
          <strong>{definition.label}</strong>
        </label>
        <input
          id={`setting-${definition.key}`}
          type="number"
          className="form-control"
          value={Number.isFinite(num) ? num : 0}
          onChange={(e) => {
            const parsed = Number.parseInt(e.currentTarget.value, 10);
            onChange(Number.isFinite(parsed) ? parsed : 0);
          }}
        />
        {definition.description && (
          <div className="form-text">{definition.description}</div>
        )}
      </div>
    );
  }

  return (
    <div className="text-muted small">
      Unknown setting type for <code>{definition.key}</code>.
    </div>
  );
}
