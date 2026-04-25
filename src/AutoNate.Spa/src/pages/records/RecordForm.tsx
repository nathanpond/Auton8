import { useMemo } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { RecordTypeField } from "@/types/records";
import AssigneePicker from "@/components/AssigneePicker";
import { buildDefaultValues, buildRecordZodSchema, getRenderer } from "./fields/registry";

type Props = {
  fields: RecordTypeField[];
  initialName?: string;
  initialStatus?: string | null;
  initialDueDate?: string | null;
  initialValues?: Record<string, unknown>;
  initialAssigneeIds?: string[];
  submitLabel: string;
  onSubmit: (input: {
    name: string;
    status: string | null;
    dueDate: string | null;
    values: Record<string, unknown>;
    assigneeIds: string[];
  }) => Promise<void> | void;
  onCancel?: () => void;
  busy?: boolean;
  topLevelError?: string | null;
};

type FormShape = {
  __name: string;
  __status: string;
  __dueDate: string;
  __assigneeIds: string[];
} & Record<string, unknown>;

export default function RecordForm({
  fields,
  initialName,
  initialStatus,
  initialDueDate,
  initialValues,
  initialAssigneeIds,
  submitLabel,
  onSubmit,
  onCancel,
  busy,
  topLevelError
}: Props) {
  const visibleFields = useMemo(() => fields.filter((f) => !f.isArchived), [fields]);

  const valueSchema = useMemo(() => buildRecordZodSchema(visibleFields), [visibleFields]);
  const fullSchema = useMemo(
    () =>
      z
        .object({
          __name: z.string().min(1, "Name is required"),
          __status: z.string(),
          __dueDate: z.string(),
          __assigneeIds: z.array(z.string())
        })
        .merge(valueSchema),
    [valueSchema]
  );

  const defaults = useMemo<FormShape>(
    () => ({
      __name: initialName ?? "",
      __status: initialStatus ?? "",
      __dueDate: initialDueDate ?? "",
      __assigneeIds: initialAssigneeIds ?? [],
      ...buildDefaultValues(visibleFields),
      ...(initialValues ?? {})
    }) as FormShape,
    [visibleFields, initialName, initialStatus, initialDueDate, initialValues, initialAssigneeIds]
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
    const { __name, __status, __dueDate, __assigneeIds, ...rest } = values;
    const trimmedStatus = __status.trim();
    await onSubmit({
      name: __name,
      status: trimmedStatus.length === 0 ? null : trimmedStatus,
      dueDate: __dueDate.length === 0 ? null : __dueDate,
      values: rest,
      assigneeIds: __assigneeIds
    });
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

      <div className="row g-3 mb-3">
        <div className="col-md-6">
          <label className="form-label">Status</label>
          <input
            type="text"
            className={`form-control ${errors.__status ? "is-invalid" : ""}`}
            placeholder="e.g. Open, In progress"
            {...register("__status")}
          />
        </div>
        <div className="col-md-6">
          <label className="form-label">Due Date</label>
          <input
            type="date"
            className={`form-control ${errors.__dueDate ? "is-invalid" : ""}`}
            {...register("__dueDate")}
          />
        </div>
      </div>

      <div className="mb-3">
        <label className="form-label">Assignees</label>
        <Controller
          name="__assigneeIds"
          control={control}
          render={({ field: f }) => (
            <AssigneePicker
              value={(f.value as string[] | undefined) ?? []}
              onChange={f.onChange}
              disabled={busy}
            />
          )}
        />
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
