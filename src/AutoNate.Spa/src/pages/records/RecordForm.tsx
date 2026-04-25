import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { RecordTypeField } from "@/types/records";
import { buildDefaultValues, buildRecordZodSchema, getRenderer } from "./fields/registry";

type Props = {
  fields: RecordTypeField[];
  initialName?: string;
  initialValues?: Record<string, unknown>;
  submitLabel: string;
  onSubmit: (input: { name: string; values: Record<string, unknown> }) => Promise<void> | void;
  onCancel?: () => void;
  busy?: boolean;
  topLevelError?: string | null;
};

type FormShape = { __name: string } & Record<string, unknown>;

export default function RecordForm({
  fields,
  initialName,
  initialValues,
  submitLabel,
  onSubmit,
  onCancel,
  busy,
  topLevelError
}: Props) {
  const visibleFields = useMemo(() => fields.filter((f) => !f.isArchived), [fields]);

  const valueSchema = useMemo(() => buildRecordZodSchema(visibleFields), [visibleFields]);
  const fullSchema = useMemo(
    () => z.object({ __name: z.string().min(1, "Name is required") }).merge(valueSchema),
    [valueSchema]
  );

  const defaults = useMemo<FormShape>(
    () => ({
      __name: initialName ?? "",
      ...buildDefaultValues(visibleFields),
      ...(initialValues ?? {})
    }) as FormShape,
    [visibleFields, initialName, initialValues]
  );

  const {
    control,
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<FormShape>({
    resolver: zodResolver(fullSchema as never),
    defaultValues: defaults
  });

  const submit = handleSubmit(async (values) => {
    const { __name, ...rest } = values;
    await onSubmit({ name: __name, values: rest });
  });

  return (
    <form onSubmit={submit} noValidate>
      {topLevelError && <div className="alert alert-danger">{topLevelError}</div>}

      <div className="mb-3">
        <label className="form-label">Name</label>
        <input
          className={`form-control ${errors.__name ? "is-invalid" : ""}`}
          {...register("__name")}
        />
        {errors.__name && (
          <div className="invalid-feedback">{(errors.__name as { message?: string }).message}</div>
        )}
      </div>

      <div className="row g-3">
        {visibleFields.map((field) => {
          const renderer = getRenderer(field.dataType);
          if (!renderer) {
            return (
              <div key={field.id} className="col-12">
                <label className="form-label">
                  {field.displayName}
                  <span className="text-warning ms-1">(unsupported type {field.dataType})</span>
                </label>
              </div>
            );
          }
          const FormImpl = renderer.Form;
          return (
            <div key={field.id} className="col-md-6">
              <label className="form-label">
                {field.displayName}
                {field.isRequired && <span className="text-danger ms-1">*</span>}
              </label>
              <FormImpl field={field} control={control as never} />
            </div>
          );
        })}
      </div>

      <div className="mt-4 d-flex justify-content-end gap-2">
        {onCancel && (
          <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>
            Cancel
          </button>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy || isSubmitting}>
          {submitLabel}
        </button>
      </div>
    </form>
  );
}
