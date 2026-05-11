import { useMemo } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Alert,
  Box,
  Button,
  Group,
  Input,
  SimpleGrid,
  Stack,
  TextInput
} from "@mantine/core";
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
    <Box component="form" onSubmit={submit} noValidate>
      <Stack gap="md">
        {topLevelError && (
          <Alert color="red" variant="light">
            {topLevelError}
          </Alert>
        )}

        <TextInput
          label="Name"
          error={(errors.__name as { message?: string } | undefined)?.message}
          {...register("__name")}
        />

        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
          <TextInput
            label="Status"
            placeholder="e.g. Open, In progress"
            error={(errors.__status as { message?: string } | undefined)?.message}
            {...register("__status")}
          />
          <TextInput
            label="Due Date"
            type="date"
            error={(errors.__dueDate as { message?: string } | undefined)?.message}
            {...register("__dueDate")}
          />
        </SimpleGrid>

        <Input.Wrapper label="Assignees">
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
        </Input.Wrapper>

        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
          {visibleFields.map((field) => {
            const renderer = getRenderer(field.dataType);
            if (!renderer) {
              return (
                <Input.Wrapper
                  key={field.id}
                  label={
                    <>
                      {field.displayName}{" "}
                      <span style={{ color: "var(--mantine-color-yellow-7)" }}>
                        (unsupported type {field.dataType})
                      </span>
                    </>
                  }
                />
              );
            }
            const FormImpl = renderer.Form;
            return (
              <Input.Wrapper
                key={field.id}
                label={
                  <>
                    {field.displayName}
                    {field.isRequired && (
                      <span style={{ color: "var(--mantine-color-red-7)", marginLeft: 4 }}>*</span>
                    )}
                  </>
                }
              >
                <FormImpl field={field} control={control as never} />
              </Input.Wrapper>
            );
          })}
        </SimpleGrid>

        <Group justify="flex-end" gap="xs" mt="md">
          {onCancel && (
            <Button variant="default" onClick={onCancel}>
              Cancel
            </Button>
          )}
          <Button type="submit" loading={busy || isSubmitting}>
            {submitLabel}
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
