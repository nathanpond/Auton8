import { useState } from "react";
import { FilterOperatorWire, RecordTypeField, SearchFilterClause } from "@/types/records";
import "./fields/renderers";
import { defaultOperator, operatorsFor } from "./fields/operators";
import { getOptionChoices } from "./fields/registry";

/**
 * The internal draft shape: `value` is whatever the input emits (string, number,
 * bool, etc.). We coerce it to the wire shape on apply.
 */
type DraftClause = {
  fieldKey: string;
  op: FilterOperatorWire;
  value: unknown;
};

type Props = {
  fields: RecordTypeField[];
  initialFilters: SearchFilterClause[];
  onApply: (filters: SearchFilterClause[]) => void;
  onClear: () => void;
};

export default function RecordFilterBuilder({ fields, initialFilters, onApply, onClear }: Props) {
  const filterableFields = fields.filter((f) => !f.isArchived);
  const [drafts, setDrafts] = useState<DraftClause[]>(() =>
    initialFilters.map((f) => ({ fieldKey: f.fieldKey, op: f.op, value: f.value }))
  );

  const fieldByKey = new Map(filterableFields.map((f) => [f.fieldKey, f] as const));

  const addClause = () => {
    if (filterableFields.length === 0) return;
    const field = filterableFields[0];
    setDrafts((d) => [
      ...d,
      { fieldKey: field.fieldKey, op: defaultOperator(field), value: clauseDefault(field) }
    ]);
  };

  const updateClause = (index: number, patch: Partial<DraftClause>) => {
    setDrafts((d) => d.map((c, i) => (i === index ? { ...c, ...patch } : c)));
  };

  const removeClause = (index: number) => {
    setDrafts((d) => d.filter((_, i) => i !== index));
  };

  const apply = () => {
    const valid = drafts.filter((c) => isValuePresent(c.value));
    onApply(valid.map((c) => ({ fieldKey: c.fieldKey, op: c.op, value: c.value })));
  };

  if (filterableFields.length === 0) {
    return (
      <p className="text-body text-opacity-50 mb-0">
        No fields are defined yet — add fields to the record type to enable filtering.
      </p>
    );
  }

  return (
    <div className="vstack gap-2">
      {drafts.length === 0 && (
        <p className="text-body text-opacity-50 mb-0 small">
          No filters. Click "Add filter" to narrow the list.
        </p>
      )}
      {drafts.map((clause, i) => {
        const field = fieldByKey.get(clause.fieldKey) ?? filterableFields[0];
        const operators = operatorsFor(field);
        return (
          <div key={i} className="d-flex gap-2 align-items-start">
            <select
              className="form-select form-select-sm"
              style={{ maxWidth: "14rem" }}
              value={clause.fieldKey}
              onChange={(e) => {
                const nextField =
                  filterableFields.find((f) => f.fieldKey === e.target.value) ?? filterableFields[0];
                updateClause(i, {
                  fieldKey: nextField.fieldKey,
                  op: defaultOperator(nextField),
                  value: clauseDefault(nextField)
                });
              }}
            >
              {filterableFields.map((f) => (
                <option key={f.id} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
            <select
              className="form-select form-select-sm"
              style={{ maxWidth: "8rem" }}
              value={clause.op}
              onChange={(e) => updateClause(i, { op: e.target.value as FilterOperatorWire })}
            >
              {operators.map((op) => (
                <option key={op.value} value={op.value}>
                  {op.label}
                </option>
              ))}
            </select>
            <div className="flex-grow-1">
              <ClauseValueInput
                field={field}
                value={clause.value}
                onChange={(v) => updateClause(i, { value: v })}
              />
            </div>
            <button
              type="button"
              className="btn btn-outline-danger btn-sm"
              onClick={() => removeClause(i)}
              aria-label="Remove filter"
            >
              <i className="fa fa-times"></i>
            </button>
          </div>
        );
      })}
      <div className="d-flex justify-content-between">
        <button type="button" className="btn btn-outline-secondary btn-sm" onClick={addClause}>
          <i className="fa fa-plus me-2"></i>Add filter
        </button>
        <div className="d-flex gap-2">
          {drafts.length > 0 && (
            <button
              type="button"
              className="btn btn-outline-secondary btn-sm"
              onClick={() => {
                setDrafts([]);
                onClear();
              }}
            >
              Clear
            </button>
          )}
          <button type="button" className="btn btn-primary btn-sm" onClick={apply}>
            Apply
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * A compact, type-aware value input for a single filter clause. Designed to
 * render inside a horizontal row alongside the field/operator selectors, so
 * it favors single-line variants regardless of the field's storage variant.
 */
function ClauseValueInput({
  field,
  value,
  onChange
}: {
  field: RecordTypeField;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  switch (field.dataType) {
    case "boolean":
      return (
        <select
          className="form-select form-select-sm"
          value={value === true ? "true" : value === false ? "false" : ""}
          onChange={(e) => onChange(e.target.value === "true")}
        >
          <option value="true">true</option>
          <option value="false">false</option>
        </select>
      );
    case "number":
      return (
        <input
          type="number"
          className="form-control form-control-sm"
          value={value === null || value === undefined ? "" : String(value)}
          onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        />
      );
    case "date": {
      const variant = (field.config as { variant?: string }).variant ?? "date";
      return (
        <input
          type={variant === "datetime" ? "datetime-local" : "date"}
          className="form-control form-control-sm"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.target.value || null)}
        />
      );
    }
    case "option": {
      const choices = getOptionChoices(field);
      return (
        <select
          className="form-select form-select-sm"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.target.value || null)}
        >
          <option value="">(any)</option>
          {choices.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
      );
    }
    default:
      return (
        <input
          type={field.dataType === "email" ? "email" : "text"}
          className="form-control form-control-sm"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.target.value)}
        />
      );
  }
}

function clauseDefault(field: RecordTypeField): unknown {
  switch (field.dataType) {
    case "boolean":
      return true;
    case "number":
      return null;
    case "date":
      return "";
    case "option":
      return getOptionChoices(field)[0]?.value ?? "";
    default:
      return "";
  }
}

function isValuePresent(value: unknown): boolean {
  if (value === null || value === undefined) return false;
  if (typeof value === "string") return value.length > 0;
  if (Array.isArray(value)) return value.length > 0;
  return true;
}
