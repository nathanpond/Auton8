import { FieldDataType, FilterOperatorWire, RecordTypeField } from "@/types/records";

export type OperatorOption = {
  value: FilterOperatorWire;
  label: string;
};

const equality: OperatorOption[] = [
  { value: "eq", label: "equals" },
  { value: "neq", label: "not equals" }
];

const ordering: OperatorOption[] = [
  { value: "gt", label: ">" },
  { value: "gte", label: "≥" },
  { value: "lt", label: "<" },
  { value: "lte", label: "≤" }
];

const contains: OperatorOption[] = [{ value: "contains", label: "contains" }];

/**
 * Operators a given field supports in the search UI. Mirrors the per-type
 * filter logic in the backend's IFieldType.BuildFilter implementations.
 */
export function operatorsFor(field: RecordTypeField): OperatorOption[] {
  switch (field.dataType as FieldDataType) {
    case "text":
    case "phone":
      return [...equality, ...contains];
    case "email":
      return [...equality, ...contains];
    case "number":
    case "date":
      return [...equality, ...ordering];
    case "option": {
      const isMulti = Boolean((field.config as { multi?: boolean }).multi);
      // Multi-select: use 'contains' as "value-set contains the operand".
      // Single-select: equality only.
      return isMulti ? [{ value: "contains", label: "contains" }] : equality;
    }
    case "boolean":
      return equality;
    default:
      return equality;
  }
}

/**
 * Default operator for a freshly added clause. Picks the first entry from
 * {@link operatorsFor}, which is "equals" for every supported type.
 */
export function defaultOperator(field: RecordTypeField): FilterOperatorWire {
  return operatorsFor(field)[0]?.value ?? "eq";
}
