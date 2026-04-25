import { z } from "zod";
import { Controller } from "react-hook-form";
import { FieldFormProps, FieldRenderer, getOptionChoices, registerRenderer } from "./registry";
import { OptionChoice } from "@/types/records";

// ---- Text ----
const textRenderer: FieldRenderer = {
  dataType: "text",
  Form: ({ field, control }: FieldFormProps) => {
    const isMulti = (field.config as { variant?: string }).variant === "multi";
    return (
      <Controller
        name={field.fieldKey}
        control={control}
        render={({ field: f, fieldState }) =>
          isMulti ? (
            <>
              <textarea
                className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
                rows={4}
                value={(f.value as string | null) ?? ""}
                onChange={(e) => f.onChange(e.target.value)}
                onBlur={f.onBlur}
              />
              {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
            </>
          ) : (
            <>
              <input
                type="text"
                className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
                value={(f.value as string | null) ?? ""}
                onChange={(e) => f.onChange(e.target.value)}
                onBlur={f.onBlur}
              />
              {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
            </>
          )
        }
      />
    );
  },
  Display: ({ value }) => <span>{(value as string | null) ?? ""}</span>,
  zodSchema: (field) => {
    const max = Number((field.config as { maxLength?: number }).maxLength ?? 4000);
    let schema: z.ZodTypeAny = z.string().max(max);
    if (!field.isRequired) schema = schema.optional().nullable().or(z.literal(""));
    else schema = (schema as z.ZodString).min(1, "Required");
    return schema;
  },
  defaultValue: () => "",
  formatValue: (_field, value) => ((value as string | null) ?? "")
};

// ---- Number ----
const numberRenderer: FieldRenderer = {
  dataType: "number",
  Form: ({ field, control }: FieldFormProps) => (
    <Controller
      name={field.fieldKey}
      control={control}
      render={({ field: f, fieldState }) => (
        <>
          <input
            type="number"
            className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
            step={
              (field.config as { variant?: string }).variant === "integer"
                ? 1
                : 1 / Math.pow(10, Number((field.config as { precision?: number }).precision ?? 2))
            }
            value={f.value === null || f.value === undefined ? "" : String(f.value)}
            onChange={(e) => f.onChange(e.target.value === "" ? null : Number(e.target.value))}
            onBlur={f.onBlur}
          />
          {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
        </>
      )}
    />
  ),
  Display: ({ value }) => <span>{value === null || value === undefined ? "" : String(value)}</span>,
  zodSchema: (field) => {
    const cfg = field.config as { variant?: string; min?: number | null; max?: number | null };
    let schema: z.ZodNumber = z.number();
    if (cfg.variant === "integer") schema = schema.int();
    if (typeof cfg.min === "number") schema = schema.min(cfg.min);
    if (typeof cfg.max === "number") schema = schema.max(cfg.max);
    return field.isRequired ? schema : schema.nullable().optional();
  },
  defaultValue: () => null,
  formatValue: (_field, value) => (value === null || value === undefined ? "" : String(value))
};

// ---- Date ----
const dateRenderer: FieldRenderer = {
  dataType: "date",
  Form: ({ field, control }: FieldFormProps) => {
    const variant = (field.config as { variant?: string }).variant ?? "date";
    return (
      <Controller
        name={field.fieldKey}
        control={control}
        render={({ field: f, fieldState }) => {
          if (variant === "range") {
            const range = (f.value as { start: string; end: string } | null) ?? { start: "", end: "" };
            return (
              <div>
                <div className="row g-2">
                  <div className="col">
                    <input
                      type="date"
                      className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
                      value={range.start ?? ""}
                      onChange={(e) => f.onChange({ ...range, start: e.target.value })}
                    />
                  </div>
                  <div className="col">
                    <input
                      type="date"
                      className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
                      value={range.end ?? ""}
                      onChange={(e) => f.onChange({ ...range, end: e.target.value })}
                    />
                  </div>
                </div>
                {fieldState.error && <div className="invalid-feedback d-block">{fieldState.error.message}</div>}
              </div>
            );
          }
          return (
            <>
              <input
                type={variant === "datetime" ? "datetime-local" : "date"}
                className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
                value={(f.value as string | null) ?? ""}
                onChange={(e) => f.onChange(e.target.value || null)}
                onBlur={f.onBlur}
              />
              {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
            </>
          );
        }}
      />
    );
  },
  Display: ({ field, value }) => {
    const variant = (field.config as { variant?: string }).variant ?? "date";
    if (variant === "range") {
      const r = value as { start?: string; end?: string } | null;
      return <span>{r ? `${r.start ?? "?"} → ${r.end ?? "?"}` : ""}</span>;
    }
    return <span>{(value as string | null) ?? ""}</span>;
  },
  zodSchema: (field) => {
    const variant = (field.config as { variant?: string }).variant ?? "date";
    if (variant === "range") {
      const inner = z.object({ start: z.string().min(1, "Start required"), end: z.string().min(1, "End required") });
      return field.isRequired ? inner : inner.nullable().optional();
    }
    let s: z.ZodTypeAny = z.string();
    if (!field.isRequired) s = s.optional().nullable().or(z.literal(""));
    else s = (s as z.ZodString).min(1, "Required");
    return s;
  },
  defaultValue: (field) => {
    const variant = (field.config as { variant?: string }).variant ?? "date";
    return variant === "range" ? { start: "", end: "" } : "";
  },
  formatValue: (field, value) => {
    const variant = (field.config as { variant?: string }).variant ?? "date";
    if (variant === "range") {
      const r = value as { start?: string; end?: string } | null;
      return r ? `${r.start ?? "?"} → ${r.end ?? "?"}` : "";
    }
    return (value as string | null) ?? "";
  }
};

// ---- Phone ----
const phoneRenderer: FieldRenderer = {
  dataType: "phone",
  Form: ({ field, control }: FieldFormProps) => (
    <Controller
      name={field.fieldKey}
      control={control}
      render={({ field: f, fieldState }) => (
        <>
          <input
            type="tel"
            className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
            placeholder="+1 415 555 2671"
            value={(f.value as string | null) ?? ""}
            onChange={(e) => f.onChange(e.target.value)}
            onBlur={f.onBlur}
          />
          {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
        </>
      )}
    />
  ),
  Display: ({ value }) => <span>{(value as string | null) ?? ""}</span>,
  zodSchema: (field) => {
    let s: z.ZodTypeAny = z.string();
    if (!field.isRequired) s = s.optional().nullable().or(z.literal(""));
    else s = (s as z.ZodString).min(1, "Required");
    return s;
  },
  defaultValue: () => "",
  formatValue: (_field, value) => ((value as string | null) ?? "")
};

// ---- Email ----
const emailRenderer: FieldRenderer = {
  dataType: "email",
  Form: ({ field, control }: FieldFormProps) => (
    <Controller
      name={field.fieldKey}
      control={control}
      render={({ field: f, fieldState }) => (
        <>
          <input
            type="email"
            className={`form-control ${fieldState.error ? "is-invalid" : ""}`}
            value={(f.value as string | null) ?? ""}
            onChange={(e) => f.onChange(e.target.value)}
            onBlur={f.onBlur}
          />
          {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
        </>
      )}
    />
  ),
  Display: ({ value }) => <span>{(value as string | null) ?? ""}</span>,
  zodSchema: (field) => {
    let s: z.ZodTypeAny = z.string().email("Invalid email").or(z.literal(""));
    if (field.isRequired) s = z.string().email("Invalid email").min(1, "Required");
    return field.isRequired ? s : s.optional().nullable();
  },
  defaultValue: () => "",
  formatValue: (_field, value) => ((value as string | null) ?? "")
};

// ---- Option ----
const optionRenderer: FieldRenderer = {
  dataType: "option",
  Form: ({ field, control }: FieldFormProps) => {
    const isMulti = Boolean((field.config as { multi?: boolean }).multi);
    const choices = getOptionChoices(field);
    return (
      <Controller
        name={field.fieldKey}
        control={control}
        render={({ field: f, fieldState }) =>
          isMulti ? (
            <>
              <div className={`d-flex flex-wrap gap-2 ${fieldState.error ? "is-invalid" : ""}`}>
                {choices.map((c) => {
                  const arr = Array.isArray(f.value) ? (f.value as string[]) : [];
                  const checked = arr.includes(c.value);
                  return (
                    <div key={c.value} className="form-check">
                      <input
                        className="form-check-input"
                        type="checkbox"
                        id={`${field.fieldKey}-${c.value}`}
                        checked={checked}
                        onChange={(e) => {
                          const next = e.target.checked
                            ? [...arr, c.value]
                            : arr.filter((v) => v !== c.value);
                          f.onChange(next);
                        }}
                      />
                      <label className="form-check-label" htmlFor={`${field.fieldKey}-${c.value}`}>
                        {c.label}
                      </label>
                    </div>
                  );
                })}
              </div>
              {fieldState.error && <div className="text-danger small mt-1">{fieldState.error.message}</div>}
            </>
          ) : (
            <>
              <select
                className={`form-select ${fieldState.error ? "is-invalid" : ""}`}
                value={(f.value as string | null) ?? ""}
                onChange={(e) => f.onChange(e.target.value || null)}
                onBlur={f.onBlur}
              >
                <option value="">{field.isRequired ? "Select..." : "(none)"}</option>
                {choices.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
              {fieldState.error && <div className="invalid-feedback">{fieldState.error.message}</div>}
            </>
          )
        }
      />
    );
  },
  Display: ({ field, value }) => {
    const choices = getOptionChoices(field);
    const labelOf = (v: string) => choices.find((c: OptionChoice) => c.value === v)?.label ?? v;
    if (Array.isArray(value)) {
      return <span>{(value as string[]).map(labelOf).join(", ")}</span>;
    }
    return <span>{value ? labelOf(String(value)) : ""}</span>;
  },
  zodSchema: (field) => {
    const isMulti = Boolean((field.config as { multi?: boolean }).multi);
    const choices = getOptionChoices(field);
    const allowed = choices.map((c: OptionChoice) => c.value);
    if (isMulti) {
      const arr = z.array(z.string().refine((v) => allowed.includes(v), "Invalid choice"));
      return field.isRequired ? arr.min(1, "Required") : arr.optional();
    }
    const single = z.string().refine((v) => v === "" || allowed.includes(v), "Invalid choice");
    return field.isRequired
      ? z.string().min(1, "Required").refine((v) => allowed.includes(v), "Invalid choice")
      : single.optional().nullable();
  },
  defaultValue: (field) =>
    (field.config as { multi?: boolean }).multi ? [] : "",
  formatValue: (field, value) => {
    const choices = getOptionChoices(field);
    const labelOf = (v: string) => choices.find((c: OptionChoice) => c.value === v)?.label ?? v;
    if (Array.isArray(value)) return (value as string[]).map(labelOf).join(", ");
    return value ? labelOf(String(value)) : "";
  }
};

// ---- Boolean ----
const booleanRenderer: FieldRenderer = {
  dataType: "boolean",
  Form: ({ field, control }: FieldFormProps) => (
    <Controller
      name={field.fieldKey}
      control={control}
      render={({ field: f }) => (
        <div className="form-check form-switch">
          <input
            className="form-check-input"
            type="checkbox"
            id={`bool-${field.fieldKey}`}
            checked={Boolean(f.value)}
            onChange={(e) => f.onChange(e.target.checked)}
          />
          <label className="form-check-label" htmlFor={`bool-${field.fieldKey}`}>
            {field.displayName}
          </label>
        </div>
      )}
    />
  ),
  Display: ({ value }) => <span>{value === true ? "Yes" : value === false ? "No" : ""}</span>,
  zodSchema: () => z.boolean().nullable().optional(),
  defaultValue: () => false,
  formatValue: (_field, value) => (value === true ? "Yes" : value === false ? "No" : "")
};

[textRenderer, numberRenderer, dateRenderer, phoneRenderer, emailRenderer, optionRenderer, booleanRenderer].forEach(
  registerRenderer
);
