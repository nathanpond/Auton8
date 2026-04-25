import { z } from "zod";
import { ComponentType } from "react";
import { Control } from "react-hook-form";
import { FieldDataType, OptionChoice, RecordTypeField } from "@/types/records";

export type FieldFormProps = {
  field: RecordTypeField;
  control: Control<Record<string, unknown>>;
  // react-hook-form's Controller renders this as the input. Each renderer
  // accepts the field definition + the form control and emits its own input.
};

export type FieldDisplayProps = {
  field: RecordTypeField;
  value: unknown;
};

export type FieldRenderer = {
  dataType: FieldDataType;
  Form: ComponentType<FieldFormProps>;
  Display: ComponentType<FieldDisplayProps>;
  zodSchema: (field: RecordTypeField) => z.ZodTypeAny;
  defaultValue: (field: RecordTypeField) => unknown;
  formatValue: (field: RecordTypeField, value: unknown) => string;
};

export const fieldRegistry: Record<string, FieldRenderer> = {};

export function registerRenderer(renderer: FieldRenderer) {
  fieldRegistry[renderer.dataType] = renderer;
}

export function getRenderer(dataType: FieldDataType): FieldRenderer | null {
  return fieldRegistry[dataType] ?? null;
}

export function buildRecordZodSchema(fields: RecordTypeField[]): z.ZodObject<z.ZodRawShape> {
  const shape: Record<string, z.ZodTypeAny> = {};
  for (const field of fields) {
    if (field.isArchived) continue;
    const renderer = getRenderer(field.dataType);
    if (!renderer) {
      // Unknown type: accept anything so the user isn't blocked from saving.
      shape[field.fieldKey] = z.any();
      continue;
    }
    shape[field.fieldKey] = renderer.zodSchema(field);
  }
  return z.object(shape);
}

export function buildDefaultValues(fields: RecordTypeField[]): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const field of fields) {
    if (field.isArchived) continue;
    const renderer = getRenderer(field.dataType);
    out[field.fieldKey] = renderer ? renderer.defaultValue(field) : null;
  }
  return out;
}

export function getOptionChoices(field: RecordTypeField): OptionChoice[] {
  const config = field.config as { choices?: OptionChoice[] };
  return Array.isArray(config.choices) ? config.choices : [];
}
