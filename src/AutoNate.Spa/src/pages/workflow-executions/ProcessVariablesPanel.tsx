import { useEffect, useMemo, useState } from "react";
import { Button, NativeSelect, Textarea, TextInput } from "@mantine/core";
import {
  useAddExecutionVariables,
  useUpdateExecutionVariables
} from "@/hooks/useExecutions";
import {
  FlowableProcessVariable,
  ProcessVariableUpdate
} from "@/types/flowable";

type Props = {
  processInstanceId: string;
  variables: readonly FlowableProcessVariable[];
  canOverride: boolean;
  onError: (message: string) => void;
  onSaved?: () => void;
};

// Wire types we expose in the type dropdown for *new* variables. Mirrors the
// scalar types Flowable's variable type registry ships with — see
// org.flowable.variable.service.impl.types.* in the Flowable source. Binary
// kinds (bytes, serializable) are intentionally omitted because we have no
// way to enter them in a text UI; bigjson piggybacks on json server-side.
const ADDABLE_TYPES = [
  "string",
  "short",
  "integer",
  "long",
  "double",
  "boolean",
  "date",
  "instant",
  "localdate",
  "localdatetime",
  "json",
  "uuid"
] as const;
type AddableType = (typeof ADDABLE_TYPES)[number];

// Editor kind drives which input element renders. Every Flowable type maps to
// one of these; unknown / unrecognised types fall back to "text" so the
// admin can still hand-edit the value while we preserve the original wire
// type on save.
type EditorKind = "text" | "integer" | "decimal" | "boolean" | "date-utc" | "local-date" | "local-datetime" | "json";

function editorKindFor(type: string | null | undefined): EditorKind {
  const t = (type ?? "").toLowerCase();
  if (t === "boolean") return "boolean";
  if (t === "integer" || t === "long" || t === "short") return "integer";
  if (t === "double" || t === "float" || t === "number") return "decimal";
  // `date` and `instant` carry full UTC timestamps — render via datetime-local
  // and round-trip through ISO 8601. `localdate` is calendar-day only;
  // `localdatetime` is wall-clock with no zone.
  if (t === "date" || t === "instant") return "date-utc";
  if (t === "localdate") return "local-date";
  if (t === "localdatetime") return "local-datetime";
  if (t === "json" || t === "bigjson") return "json";
  return "text";
}

// Sniff a JSON-shaped value so legacy variables that arrived without a `type`
// hint still get a sensible editor. Used only when classifyVariable falls
// through to the default branch.
function looksLikeJson(value: string | null | undefined): boolean {
  if (!value) return false;
  const trimmed = value.trim();
  return trimmed.startsWith("{") || trimmed.startsWith("[");
}

// Normalize what we display in the existing-variable input. The diagram-detail
// GET serializes typed values to a string via FormatVariableValue
// (FlowableClient.cs), so the value string is always editable text — we just
// reformat date strings into the YYYY-MM-DDTHH:mm shape <input type="datetime-
// local"> wants when the value would otherwise reject.
function formatForDateTimeLocalInput(raw: string): string {
  if (!raw) return "";
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return raw;
  // datetime-local wants local-zone "YYYY-MM-DDTHH:mm". Build manually rather
  // than slicing toISOString so we honor the user's local zone offset.
  const pad = (n: number) => n.toString().padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function formatForDateInput(raw: string): string {
  if (!raw) return "";
  // Either an ISO date "2024-05-06" passes through, or a full ISO timestamp
  // gets sliced down to the date portion in local time.
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return raw;
  const pad = (n: number) => n.toString().padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

// Validate + serialize a typed value for a Flowable update/create payload.
// Returns either { value: <json-serializable>, type } on success or
// { error: <message> } so the UI can surface the failure inline. `allowNull`
// controls whether an empty input clears the variable (used during edit) or
// rejects (used during add — Flowable's create endpoint requires a value).
type SerializeOk = { value: unknown; type: string };
type SerializeErr = { error: string };
type SerializeResult = SerializeOk | SerializeErr;

function serializeValue(
  type: string,
  rawText: string,
  options: { allowNull: boolean }
): SerializeResult {
  const trimmed = rawText.trim();
  const lower = type.toLowerCase();

  if (trimmed === "" && options.allowNull) {
    return { value: null, type };
  }
  if (trimmed === "" && !options.allowNull) {
    return { error: "Value is required." };
  }

  if (lower === "boolean") {
    if (trimmed !== "true" && trimmed !== "false") {
      return { error: "Must be true or false." };
    }
    return { value: trimmed === "true", type };
  }

  if (lower === "integer" || lower === "long" || lower === "short") {
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed) || !Number.isInteger(parsed)) {
      return { error: "Must be a whole number." };
    }
    return { value: parsed, type };
  }

  if (lower === "double" || lower === "float" || lower === "number") {
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed)) {
      return { error: "Must be a number." };
    }
    return { value: parsed, type };
  }

  if (lower === "json" || lower === "bigjson") {
    try {
      return { value: JSON.parse(trimmed), type };
    } catch (err) {
      return { error: err instanceof Error ? err.message : "Invalid JSON." };
    }
  }

  if (lower === "date" || lower === "instant") {
    // datetime-local emits "YYYY-MM-DDTHH:mm[:ss]" without zone — interpret
    // as local time and convert to UTC ISO 8601. Flowable's REST layer
    // accepts ISO 8601 timestamps with explicit zone.
    const date = new Date(trimmed);
    if (Number.isNaN(date.getTime())) {
      return { error: "Must be a valid date / time." };
    }
    return { value: date.toISOString(), type };
  }

  if (lower === "localdate") {
    // Calendar day, no time / zone. Pass through if it already matches the
    // expected shape; otherwise reformat from any parseable input.
    if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
      return { value: trimmed, type };
    }
    const date = new Date(trimmed);
    if (Number.isNaN(date.getTime())) {
      return { error: "Must be YYYY-MM-DD." };
    }
    return { value: formatForDateInput(trimmed), type };
  }

  if (lower === "localdatetime") {
    // Wall-clock, no zone. datetime-local already emits this shape; pass it
    // through as-is so the engine doesn't shift the wall-clock value.
    if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/.test(trimmed)) {
      return { value: trimmed, type };
    }
    return { error: "Must be YYYY-MM-DDTHH:MM." };
  }

  // Default branch covers string / uuid / and any unrecognised type — we
  // forward the text verbatim and let Flowable's variable type registry
  // validate. UUID gets a light shape check so the operator gets immediate
  // feedback for a typo.
  if (lower === "uuid") {
    if (!/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(trimmed)) {
      return { error: "Must be a UUID (8-4-4-4-12 hex digits)." };
    }
    return { value: trimmed, type };
  }

  return { value: rawText, type };
}

// Used by the Add row's name validation. Flowable accepts any string, but
// we keep the SPA-side rule tight enough to avoid surprising EL collisions.
const NEW_VARIABLE_NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;

// Local row for the Add UX. Each new variable is unsaved until the panel's
// Save button POSTs the batch.
type NewVariableDraft = {
  // Stable client id so React rendering is independent of the (mutable) name.
  clientId: string;
  name: string;
  type: AddableType;
  value: string;
};

let newVariableCounter = 0;
function newDraftId(): string {
  newVariableCounter += 1;
  return `new-var-${newVariableCounter}`;
}

// Decide the type for an existing variable. We trust Flowable's reported type
// where available so an integer stays an integer (and a double stays a double)
// across save round-trips. Sniff JSON only as a last resort.
function classifyVariable(variable: FlowableProcessVariable): string {
  const t = (variable.type ?? "").trim();
  if (t) return t;
  if (looksLikeJson(variable.value)) return "json";
  return "string";
}

// Renders a value-input element appropriate for the given editor kind. The
// existing-variable and new-variable UIs both use this so the two surfaces
// can't drift.
function renderValueInput(args: {
  kind: EditorKind;
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
}) {
  const { kind, value, onChange, placeholder } = args;
  switch (kind) {
    case "boolean":
      return (
        <NativeSelect
          size="xs"
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          data={[
            { value: "", label: "(unset)" },
            { value: "true", label: "true" },
            { value: "false", label: "false" }
          ]}
        />
      );
    case "integer":
      return (
        <TextInput
          size="xs"
          type="number"
          step={1}
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder={placeholder}
        />
      );
    case "decimal":
      return (
        <TextInput
          size="xs"
          type="number"
          step="any"
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder={placeholder}
        />
      );
    case "date-utc":
      return (
        <TextInput
          size="xs"
          type="datetime-local"
          value={formatForDateTimeLocalInput(value)}
          onChange={(e) => onChange(e.currentTarget.value)}
        />
      );
    case "local-date":
      return (
        <TextInput
          size="xs"
          type="date"
          value={formatForDateInput(value)}
          onChange={(e) => onChange(e.currentTarget.value)}
        />
      );
    case "local-datetime":
      return (
        <TextInput
          size="xs"
          type="datetime-local"
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
        />
      );
    case "json":
      return (
        <Textarea
          size="xs"
          styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
          rows={Math.min(8, Math.max(2, value.split("\n").length))}
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder={placeholder}
        />
      );
    default:
      return (
        <TextInput
          size="xs"
          type="text"
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder={placeholder}
        />
      );
  }
}

export default function ProcessVariablesPanel({
  processInstanceId,
  variables,
  canOverride,
  onError,
  onSaved
}: Props) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [dirty, setDirty] = useState<Set<string>>(new Set());
  const [errors, setErrors] = useState<Record<string, string>>({});
  // Unsaved "new variable" rows. Keyed by clientId because the user-typed
  // name is mutable and may collide with an existing row mid-edit.
  const [newDrafts, setNewDrafts] = useState<NewVariableDraft[]>([]);
  const [newDraftErrors, setNewDraftErrors] = useState<Record<string, string>>({});

  const updateVariables = useUpdateExecutionVariables(processInstanceId);
  const addVariables = useAddExecutionVariables(processInstanceId);

  // Re-snapshot the draft from the live variables whenever editing starts or
  // the variables list refreshes while we're not editing.
  useEffect(() => {
    if (editing) return;
    const snapshot: Record<string, string> = {};
    for (const v of variables) snapshot[v.name] = v.value ?? "";
    setDraft(snapshot);
    setDirty(new Set());
    setErrors({});
    setNewDrafts([]);
    setNewDraftErrors({});
  }, [variables, editing]);

  const typeByName = useMemo(() => {
    const map = new Map<string, string>();
    for (const v of variables) map.set(v.name, classifyVariable(v));
    return map;
  }, [variables]);

  const handleFieldChange = (name: string, next: string) => {
    setDraft((d) => ({ ...d, [name]: next }));
    setDirty((s) => {
      const copy = new Set(s);
      copy.add(name);
      return copy;
    });
    // Clear any prior error for this field on every keystroke; revalidate on Save.
    setErrors((e) => {
      if (!e[name]) return e;
      const { [name]: _omit, ...rest } = e;
      return rest;
    });
  };

  const beginEdit = () => {
    const snapshot: Record<string, string> = {};
    for (const v of variables) snapshot[v.name] = v.value ?? "";
    setDraft(snapshot);
    setDirty(new Set());
    setErrors({});
    setNewDrafts([]);
    setNewDraftErrors({});
    setEditing(true);
  };

  const cancelEdit = () => {
    setEditing(false);
    setDirty(new Set());
    setErrors({});
    setNewDrafts([]);
    setNewDraftErrors({});
  };

  const addEmptyDraft = () => {
    setNewDrafts((rows) => [
      ...rows,
      { clientId: newDraftId(), name: "", type: "string", value: "" }
    ]);
  };

  const removeDraft = (clientId: string) => {
    setNewDrafts((rows) => rows.filter((r) => r.clientId !== clientId));
    setNewDraftErrors((e) => {
      if (!e[clientId]) return e;
      const { [clientId]: _omit, ...rest } = e;
      return rest;
    });
  };

  const updateDraftField = (clientId: string, patch: Partial<NewVariableDraft>) => {
    setNewDrafts((rows) =>
      rows.map((r) => (r.clientId === clientId ? { ...r, ...patch } : r))
    );
    setNewDraftErrors((e) => {
      if (!e[clientId]) return e;
      const { [clientId]: _omit, ...rest } = e;
      return rest;
    });
  };

  const saveEdits = async () => {
    const updates: ProcessVariableUpdate[] = [];
    const localErrors: Record<string, string> = {};

    for (const name of dirty) {
      const wireType = typeByName.get(name) ?? "string";
      const text = draft[name] ?? "";

      const result = serializeValue(wireType, text, { allowNull: true });
      if ("error" in result) {
        localErrors[name] = result.error;
        continue;
      }
      updates.push({ name, value: result.value, type: result.type });
    }

    // Validate and collect creates from the new-variable rows.
    const additions: ProcessVariableUpdate[] = [];
    const localNewErrors: Record<string, string> = {};
    const existingNames = new Set(variables.map((v) => v.name));
    const seenInDrafts = new Set<string>();

    for (const row of newDrafts) {
      const name = row.name.trim();

      if (name === "") {
        localNewErrors[row.clientId] = "Name is required.";
        continue;
      }
      if (!NEW_VARIABLE_NAME.test(name)) {
        localNewErrors[row.clientId] =
          "Names must start with a letter or underscore and use letters, digits, or underscores.";
        continue;
      }
      if (existingNames.has(name)) {
        localNewErrors[row.clientId] = "A variable with this name already exists — edit it above instead.";
        continue;
      }
      if (seenInDrafts.has(name)) {
        localNewErrors[row.clientId] = "Two new rows share this name.";
        continue;
      }
      seenInDrafts.add(name);

      // String / uuid: empty value is allowed for string (creates an empty
      // string), rejected for everything else. allowNull: false achieves the
      // latter; we special-case string up here so an empty new string row
      // doesn't get rejected.
      if (row.type === "string") {
        additions.push({ name, value: row.value, type: "string" });
        continue;
      }

      const result = serializeValue(row.type, row.value, { allowNull: false });
      if ("error" in result) {
        localNewErrors[row.clientId] = result.error;
        continue;
      }
      additions.push({ name, value: result.value, type: result.type });
    }

    if (Object.keys(localErrors).length > 0 || Object.keys(localNewErrors).length > 0) {
      setErrors(localErrors);
      setNewDraftErrors(localNewErrors);
      return;
    }

    try {
      // Order matters: do creates first so an "edit existing then add new"
      // round-trip can't end up with the new variable visible-before-edit
      // if the second call fails. If creates succeed but updates fail, the
      // user retries Save and the create is already there (POST is not
      // re-attempted because newDrafts is cleared on success).
      if (additions.length > 0) {
        await addVariables.mutateAsync(additions);
      }
      if (updates.length > 0) {
        await updateVariables.mutateAsync(updates);
      }
      setEditing(false);
      setDirty(new Set());
      setErrors({});
      setNewDrafts([]);
      setNewDraftErrors({});
      onSaved?.();
    } catch (err) {
      onError(err instanceof Error ? err.message : String(err));
    }
  };

  const hasErrors =
    Object.keys(errors).length > 0 || Object.keys(newDraftErrors).length > 0;
  const saving = updateVariables.isPending || addVariables.isPending;
  const hasPendingChanges = dirty.size > 0 || newDrafts.length > 0;

  return (
    <aside className="workflow-execution-variables-panel" aria-label="Process variables">
      <div className="workflow-execution-variables-header">
        <h3 className="workflow-execution-variables-title">Process Variables</h3>
        {!editing && canOverride && (
          <Button size="xs" variant="outline" onClick={beginEdit}>
            {variables.length > 0 ? "Edit" : "Add Variable"}
          </Button>
        )}
        {editing && (
          <div className="workflow-execution-variables-actions">
            <Button size="xs" variant="default" onClick={cancelEdit} disabled={saving}>
              Cancel
            </Button>
            <Button
              size="xs"
              onClick={saveEdits}
              loading={saving}
              disabled={hasErrors || !hasPendingChanges}
            >
              Save
            </Button>
          </div>
        )}
      </div>

      {variables.length === 0 && !editing && (
        <p className="workflow-execution-variables-empty">
          No variables have been set on this execution.
        </p>
      )}

      {variables.length > 0 && (
        <ul className="workflow-execution-variables-list">
          {variables.map((variable) => {
            const wireType = typeByName.get(variable.name) ?? "string";
            const editorKind = editorKindFor(wireType);
            const value = draft[variable.name] ?? variable.value ?? "";
            const error = errors[variable.name];
            return (
              <li key={variable.name} className="workflow-execution-variable">
                <div className="workflow-execution-variable-header">
                  <span className="workflow-execution-variable-name">{variable.name}</span>
                  {variable.type && (
                    <span className="workflow-execution-variable-type">{variable.type}</span>
                  )}
                </div>
                {editing ? (
                  <>
                    {renderValueInput({
                      kind: editorKind,
                      value,
                      onChange: (next) => handleFieldChange(variable.name, next)
                    })}
                    {error && <div style={{ color: "var(--mantine-color-red-filled)", fontSize: "0.875rem", marginTop: 4 }}>{error}</div>}
                  </>
                ) : (
                  <div className="workflow-execution-variable-value">
                    {variable.value ?? "null"}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {editing && (
        <div className="workflow-execution-variables-add">
          {newDrafts.length > 0 && (
            <ul className="workflow-execution-variables-list workflow-execution-variables-new-list">
              {newDrafts.map((row) => {
                const error = newDraftErrors[row.clientId];
                const editorKind = editorKindFor(row.type);
                return (
                  <li key={row.clientId} className="workflow-execution-variable workflow-execution-variable--new">
                    <div className="workflow-execution-variable-header">
                      <TextInput
                        size="xs"
                        className="workflow-execution-variable-new-name"
                        placeholder="Variable name"
                        value={row.name}
                        onChange={(e) => updateDraftField(row.clientId, { name: e.currentTarget.value })}
                      />
                      <NativeSelect
                        size="xs"
                        className="workflow-execution-variable-new-type"
                        value={row.type}
                        onChange={(e) => {
                          const nextType = e.currentTarget.value as AddableType;
                          // Reset the value when the editor element changes
                          // shape (boolean / date* / json) so the previous
                          // text doesn't render as an invalid selection.
                          const previousKind = editorKindFor(row.type);
                          const nextKind = editorKindFor(nextType);
                          const valueChanges =
                            previousKind !== nextKind &&
                            (nextKind === "boolean"
                              || nextKind === "date-utc"
                              || nextKind === "local-date"
                              || nextKind === "local-datetime");
                          updateDraftField(row.clientId, {
                            type: nextType,
                            ...(valueChanges ? { value: "" } : {})
                          });
                        }}
                        data={ADDABLE_TYPES.map((type) => ({ value: type, label: type }))}
                      />
                      <Button
                        size="xs"
                        variant="outline"
                        color="red"
                        onClick={() => removeDraft(row.clientId)}
                        aria-label={`Remove variable ${row.name || "(unnamed)"}`}
                        title="Remove"
                      >
                        ×
                      </Button>
                    </div>
                    {renderValueInput({
                      kind: editorKind,
                      value: row.value,
                      onChange: (next) => updateDraftField(row.clientId, { value: next }),
                      placeholder: editorKind === "json" ? '{ "example": true }' : undefined
                    })}
                    {error && <div style={{ color: "var(--mantine-color-red-filled)", fontSize: "0.875rem", marginTop: 4 }}>{error}</div>}
                  </li>
                );
              })}
            </ul>
          )}

          <Button
            size="xs"
            variant="default"
            className="workflow-execution-variables-add-button"
            onClick={addEmptyDraft}
            disabled={saving}
          >
            + Add Variable
          </Button>
        </div>
      )}
    </aside>
  );
}
