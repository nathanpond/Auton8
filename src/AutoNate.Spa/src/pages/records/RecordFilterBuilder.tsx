import { useState } from "react";
import {
  ActionIcon,
  Box,
  Button,
  Group,
  NativeSelect,
  Stack,
  Text,
  TextInput
} from "@mantine/core";
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
      <Text c="dimmed">
        No fields are defined yet — add fields to the record type to enable filtering.
      </Text>
    );
  }

  return (
    <Stack gap="xs">
      {drafts.length === 0 && (
        <Text c="dimmed" size="sm">
          No filters. Click "Add filter" to narrow the list.
        </Text>
      )}
      {drafts.map((clause, i) => {
        const field = fieldByKey.get(clause.fieldKey) ?? filterableFields[0];
        const operators = operatorsFor(field);
        return (
          <Group key={i} gap="xs" align="flex-start" wrap="nowrap">
            <NativeSelect
              size="xs"
              style={{ maxWidth: "14rem" }}
              value={clause.fieldKey}
              onChange={(e) => {
                const nextField =
                  filterableFields.find((f) => f.fieldKey === e.currentTarget.value) ??
                  filterableFields[0];
                updateClause(i, {
                  fieldKey: nextField.fieldKey,
                  op: defaultOperator(nextField),
                  value: clauseDefault(nextField)
                });
              }}
              data={filterableFields.map((f) => ({ value: f.fieldKey, label: f.displayName }))}
            />
            <NativeSelect
              size="xs"
              style={{ maxWidth: "8rem" }}
              value={clause.op}
              onChange={(e) =>
                updateClause(i, { op: e.currentTarget.value as FilterOperatorWire })
              }
              data={operators.map((op) => ({ value: op.value, label: op.label }))}
            />
            <Box style={{ flex: 1 }}>
              <ClauseValueInput
                field={field}
                value={clause.value}
                onChange={(v) => updateClause(i, { value: v })}
              />
            </Box>
            <ActionIcon
              variant="outline"
              color="red"
              size="sm"
              onClick={() => removeClause(i)}
              aria-label="Remove filter"
            >
              <i className="fa fa-times" />
            </ActionIcon>
          </Group>
        );
      })}
      <Group justify="space-between">
        <Button
          size="xs"
          variant="default"
          leftSection={<i className="fa fa-plus" />}
          onClick={addClause}
        >
          Add filter
        </Button>
        <Group gap="xs">
          {drafts.length > 0 && (
            <Button
              size="xs"
              variant="default"
              onClick={() => {
                setDrafts([]);
                onClear();
              }}
            >
              Clear
            </Button>
          )}
          <Button size="xs" onClick={apply}>
            Apply
          </Button>
        </Group>
      </Group>
    </Stack>
  );
}

/**
 * A compact, type-aware value input for a single filter clause.
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
        <NativeSelect
          size="xs"
          value={value === true ? "true" : value === false ? "false" : ""}
          onChange={(e) => onChange(e.currentTarget.value === "true")}
          data={[
            { value: "true", label: "true" },
            { value: "false", label: "false" }
          ]}
        />
      );
    case "number":
      return (
        <TextInput
          size="xs"
          type="number"
          value={value === null || value === undefined ? "" : String(value)}
          onChange={(e) =>
            onChange(e.currentTarget.value === "" ? null : Number(e.currentTarget.value))
          }
        />
      );
    case "date": {
      const variant = (field.config as { variant?: string }).variant ?? "date";
      return (
        <TextInput
          size="xs"
          type={variant === "datetime" ? "datetime-local" : "date"}
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value || null)}
        />
      );
    }
    case "option": {
      const choices = getOptionChoices(field);
      return (
        <NativeSelect
          size="xs"
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value || null)}
          data={[
            { value: "", label: "(any)" },
            ...choices.map((c) => ({ value: c.value, label: c.label }))
          ]}
        />
      );
    }
    default:
      return (
        <TextInput
          size="xs"
          type={field.dataType === "email" ? "email" : "text"}
          value={(value as string | null) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value)}
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
