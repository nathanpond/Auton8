import { ReactNode } from "react";
import {
  ColorInput,
  MultiSelect,
  NumberInput,
  Select,
  Stack,
  Switch,
  TextInput,
  Textarea,
  Title
} from "@mantine/core";
import { z } from "zod";

// Side metadata for fields whose UI hint can't be derived from the Zod
// schema alone (e.g. "this string is actually a color picker", "this
// number is a percentage", "this textarea should be a multi-line"). The
// metadata lives in a WeakMap keyed off the schema instance so the
// schema's runtime structure stays pure Zod.
const FIELD_META = new WeakMap<z.ZodType, FieldMeta>();

type FieldMeta = {
  label?: string;
  description?: string;
  placeholder?: string;
  uiHint?: "color" | "textarea" | "password";
  required?: boolean;
};

export function attachMeta<T extends z.ZodType>(schema: T, meta: FieldMeta): T {
  FIELD_META.set(schema, meta);
  return schema;
}

function readMeta(schema: z.ZodType): FieldMeta {
  return FIELD_META.get(schema) ?? {};
}

// Strip the Zod wrapper layers (optional, nullable, default) so we can
// pattern-match on the underlying concrete type. Returns the inner schema
// + a flag for whether the field was optional/nullable.
function unwrap(schema: z.ZodType): { inner: z.ZodType; optional: boolean } {
  let s: z.ZodType = schema;
  let optional = false;
  // Zod 4 keeps these wrapper names. We walk until we hit a concrete type.
  // (`z.ZodOptional`, `z.ZodNullable`, `z.ZodDefault` are still distinct
  // classes in v4.)
   
  while ((s as any)._def?.innerType) {
    optional = true;
     
    s = (s as any)._def.innerType as z.ZodType;
  }
  return { inner: s, optional };
}

type Renderer = (props: {
  fieldKey: string;
  schema: z.ZodType;
  value: unknown;
  onChange: (next: unknown) => void;
  error?: string;
  meta: FieldMeta;
  optional: boolean;
}) => ReactNode;

 
function typeName(schema: z.ZodType): string {
  // Zod 4 still names classes ZodString / ZodNumber / etc.
   
  return (schema as any).constructor?.name ?? "";
}

// Convert a camelCase field key to "Field Key" for the default label when
// the schema author didn't supply one via attachMeta.
function defaultLabel(key: string): string {
  const spaced = key.replace(/([A-Z])/g, " $1").trim();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

const RENDERERS: Record<string, Renderer> = {
  ZodString: ({ fieldKey, schema, value, onChange, error, meta, optional }) => {
    const label = meta.label ?? defaultLabel(fieldKey);
    const required = meta.required ?? !optional;
    if (meta.uiHint === "color") {
      return (
        <ColorInput
          key={fieldKey}
          label={label}
          description={meta.description}
          placeholder={meta.placeholder}
          value={(value as string) ?? ""}
          onChange={onChange}
          error={error}
          withAsterisk={required}
        />
      );
    }
    if (meta.uiHint === "textarea") {
      return (
        <Textarea
          key={fieldKey}
          label={label}
          description={meta.description}
          placeholder={meta.placeholder}
          value={(value as string) ?? ""}
          onChange={(e) => onChange(e.currentTarget.value)}
          error={error}
          withAsterisk={required}
          autosize
          minRows={2}
        />
      );
    }
    return (
      <TextInput
        key={fieldKey}
        label={label}
        description={meta.description}
        placeholder={meta.placeholder}
        type={meta.uiHint === "password" ? "password" : undefined}
        value={(value as string) ?? ""}
        onChange={(e) => onChange(e.currentTarget.value)}
        error={error}
        withAsterisk={required}
      />
    );
    void schema;
  },
  ZodNumber: ({ fieldKey, schema, value, onChange, error, meta, optional }) => {
    const label = meta.label ?? defaultLabel(fieldKey);
    const required = meta.required ?? !optional;
    // Zod 4 carries min/max as `_def.checks: { check, value }[]`.
     
    const checks = (schema as any)._def?.checks ?? [];
    type Check = { check?: string; value?: number };
    const min = (checks as Check[]).find((c) => c.check === "min")?.value;
    const max = (checks as Check[]).find((c) => c.check === "max")?.value;
    return (
      <NumberInput
        key={fieldKey}
        label={label}
        description={meta.description}
        placeholder={meta.placeholder}
        value={(value as number | undefined) ?? ""}
        onChange={(v) => onChange(typeof v === "number" ? v : Number(v) || 0)}
        min={min}
        max={max}
        error={error}
        withAsterisk={required}
      />
    );
  },
  ZodBoolean: ({ fieldKey, value, onChange, meta }) => (
    <Switch
      key={fieldKey}
      label={meta.label ?? defaultLabel(fieldKey)}
      description={meta.description}
      checked={Boolean(value)}
      onChange={(e) => onChange(e.currentTarget.checked)}
    />
  ),
  ZodEnum: ({ fieldKey, schema, value, onChange, error, meta, optional }) => {
    // Zod 4: `z.enum(['a','b'])._def.entries` is `{ a: 'a', b: 'b' }`.
     
    const entries = (schema as any)._def?.entries ?? {};
    const options = Object.keys(entries);
    return (
      <Select
        key={fieldKey}
        label={meta.label ?? defaultLabel(fieldKey)}
        description={meta.description}
        placeholder={meta.placeholder ?? "Select…"}
        data={options}
        value={(value as string) ?? null}
        onChange={(v) => onChange(v ?? undefined)}
        error={error}
        withAsterisk={meta.required ?? !optional}
        clearable={optional}
      />
    );
  },
  ZodArray: ({ fieldKey, schema, value, onChange, error, meta, optional }) => {
     
    const elementSchema = (schema as any)._def?.element ?? (schema as any)._def?.type;
    const elementInner = elementSchema ? unwrap(elementSchema).inner : null;
    if (elementInner && typeName(elementInner) === "ZodEnum") {
       
      const entries = (elementInner as any)._def?.entries ?? {};
      const options = Object.keys(entries);
      return (
        <MultiSelect
          key={fieldKey}
          label={meta.label ?? defaultLabel(fieldKey)}
          description={meta.description}
          placeholder={meta.placeholder}
          data={options}
          value={(value as string[]) ?? []}
          onChange={onChange}
          error={error}
          withAsterisk={meta.required ?? !optional}
        />
      );
    }
    // Other array shapes (objects, nested unions, etc.) need a bespoke
    // ConfigForm override — we don't attempt to render arbitrary record
    // lists. Show a placeholder so the field is at least visible.
    return (
      <TextInput
        key={fieldKey}
        label={meta.label ?? defaultLabel(fieldKey)}
        description={meta.description ?? "(complex list — use a custom config form)"}
        value=""
        disabled
        readOnly
      />
    );
  }
};

export type AutoConfigFormProps<TConfig extends Record<string, unknown>> = {
  schema: z.ZodObject<Record<string, z.ZodType>>;
  value: TConfig;
  onChange: (next: TConfig) => void;
  errors?: Record<string, string>;
  title?: string;
};

export function AutoConfigForm<TConfig extends Record<string, unknown>>({
  schema,
  value,
  onChange,
  errors = {},
  title
}: AutoConfigFormProps<TConfig>) {
  // Zod 4 exposes the object shape via `.shape`. Walk the keys and render
  // one field per entry.
  const shape = schema.shape as Record<string, z.ZodType>;
  const fields = Object.keys(shape).map((fieldKey) => {
    const fieldSchema = shape[fieldKey];
    const { inner, optional } = unwrap(fieldSchema);
    const meta = readMeta(fieldSchema);
    const innerMeta = readMeta(inner);
    const combinedMeta: FieldMeta = { ...innerMeta, ...meta };
    const renderer = RENDERERS[typeName(inner)];
    const onFieldChange = (next: unknown) => {
      onChange({ ...value, [fieldKey]: next } as TConfig);
    };
    if (!renderer) {
      // Unknown type — render disabled placeholder so the user notices
      // and the widget author can add a renderer or override.
      return (
        <TextInput
          key={fieldKey}
          label={combinedMeta.label ?? defaultLabel(fieldKey)}
          description={`(unsupported field type: ${typeName(inner) || "unknown"})`}
          value=""
          disabled
          readOnly
        />
      );
    }
    return renderer({
      fieldKey,
      schema: inner,
      value: value[fieldKey],
      onChange: onFieldChange,
      error: errors[fieldKey],
      meta: combinedMeta,
      optional
    });
  });

  return (
    <Stack gap="sm">
      {title ? (
        <Title order={5} mb={4}>
          {title}
        </Title>
      ) : null}
      {fields}
    </Stack>
  );
}
