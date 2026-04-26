import { useEffect, useMemo, useState } from "react";
import { useUpdateExecutionVariables } from "@/hooks/useExecutions";
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

type VariableKind = "string" | "number" | "boolean" | "json";

// Decide the editor input type based on Flowable's reported variable type plus
// the rendered value as a fallback. The diagram-detail GET flattens typed
// values to a string via FormatVariableValue (FlowableClient.cs:333) so we use
// the type hint where possible and sniff the value text otherwise.
function classifyVariable(variable: FlowableProcessVariable): VariableKind {
  const t = (variable.type ?? "").toLowerCase();
  if (t === "boolean") return "boolean";
  if (t === "integer" || t === "long" || t === "double" || t === "short" || t === "number") return "number";
  if (t === "json") return "json";
  if (variable.value !== null && variable.value !== undefined) {
    const trimmed = variable.value.trim();
    if (trimmed.startsWith("{") || trimmed.startsWith("[")) return "json";
  }
  return "string";
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

  const updateVariables = useUpdateExecutionVariables(processInstanceId);

  // Re-snapshot the draft from the live variables whenever editing starts or
  // the variables list refreshes while we're not editing.
  useEffect(() => {
    if (editing) return;
    const snapshot: Record<string, string> = {};
    for (const v of variables) snapshot[v.name] = v.value ?? "";
    setDraft(snapshot);
    setDirty(new Set());
    setErrors({});
  }, [variables, editing]);

  const kindByName = useMemo(() => {
    const map = new Map<string, VariableKind>();
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
    setEditing(true);
  };

  const cancelEdit = () => {
    setEditing(false);
    setDirty(new Set());
    setErrors({});
  };

  const saveEdits = async () => {
    const updates: ProcessVariableUpdate[] = [];
    const localErrors: Record<string, string> = {};

    for (const name of dirty) {
      const kind = kindByName.get(name) ?? "string";
      const text = draft[name] ?? "";
      const trimmed = text.trim();

      if (kind === "boolean") {
        if (trimmed === "") {
          updates.push({ name, value: null, type: "boolean" });
          continue;
        }
        if (trimmed !== "true" && trimmed !== "false") {
          localErrors[name] = "Must be true or false.";
          continue;
        }
        updates.push({ name, value: trimmed === "true", type: "boolean" });
        continue;
      }

      if (kind === "number") {
        if (trimmed === "") {
          updates.push({ name, value: null, type: variables.find((v) => v.name === name)?.type ?? "integer" });
          continue;
        }
        const parsed = Number(trimmed);
        if (Number.isNaN(parsed)) {
          localErrors[name] = "Must be a number.";
          continue;
        }
        updates.push({
          name,
          value: parsed,
          type: variables.find((v) => v.name === name)?.type ?? "integer"
        });
        continue;
      }

      if (kind === "json") {
        if (trimmed === "") {
          updates.push({ name, value: null, type: "json" });
          continue;
        }
        try {
          updates.push({ name, value: JSON.parse(trimmed), type: "json" });
        } catch (err) {
          localErrors[name] = err instanceof Error ? err.message : "Invalid JSON.";
        }
        continue;
      }

      // String. Empty input clears the variable.
      updates.push({ name, value: text === "" ? null : text, type: "string" });
    }

    if (Object.keys(localErrors).length > 0) {
      setErrors(localErrors);
      return;
    }

    try {
      await updateVariables.mutateAsync(updates);
      setEditing(false);
      setDirty(new Set());
      setErrors({});
      onSaved?.();
    } catch (err) {
      onError(err instanceof Error ? err.message : String(err));
    }
  };

  const hasErrors = Object.keys(errors).length > 0;
  const saving = updateVariables.isPending;

  return (
    <aside className="workflow-execution-variables-panel" aria-label="Process variables">
      <div className="workflow-execution-variables-header">
        <h3 className="workflow-execution-variables-title">Process Variables</h3>
        {!editing && canOverride && variables.length > 0 && (
          <button
            type="button"
            className="btn btn-sm btn-outline-primary"
            onClick={beginEdit}
          >
            Edit
          </button>
        )}
        {editing && (
          <div className="workflow-execution-variables-actions">
            <button
              type="button"
              className="btn btn-sm btn-outline-secondary"
              onClick={cancelEdit}
              disabled={saving}
            >
              Cancel
            </button>
            <button
              type="button"
              className="btn btn-sm btn-primary"
              onClick={saveEdits}
              disabled={saving || hasErrors || dirty.size === 0}
            >
              {saving ? "Saving…" : "Save"}
            </button>
          </div>
        )}
      </div>

      {variables.length === 0 ? (
        <p className="workflow-execution-variables-empty">
          No variables have been set on this execution.
        </p>
      ) : (
        <ul className="workflow-execution-variables-list">
          {variables.map((variable) => {
            const kind = kindByName.get(variable.name) ?? "string";
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
                    {kind === "boolean" ? (
                      <select
                        className="form-select form-select-sm"
                        value={value}
                        onChange={(e) => handleFieldChange(variable.name, e.target.value)}
                      >
                        <option value="">(unset)</option>
                        <option value="true">true</option>
                        <option value="false">false</option>
                      </select>
                    ) : kind === "number" ? (
                      <input
                        type="number"
                        className="form-control form-control-sm"
                        value={value}
                        onChange={(e) => handleFieldChange(variable.name, e.target.value)}
                      />
                    ) : kind === "json" ? (
                      <textarea
                        className="form-control form-control-sm font-monospace"
                        rows={Math.min(8, Math.max(2, value.split("\n").length))}
                        value={value}
                        onChange={(e) => handleFieldChange(variable.name, e.target.value)}
                      />
                    ) : (
                      <input
                        type="text"
                        className="form-control form-control-sm"
                        value={value}
                        onChange={(e) => handleFieldChange(variable.name, e.target.value)}
                      />
                    )}
                    {error && <div className="invalid-feedback d-block small">{error}</div>}
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
    </aside>
  );
}
