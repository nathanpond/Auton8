import { useEffect, useMemo, useState } from "react";
import { useForm } from "@mantine/form";
import { zod4Resolver as zodResolver } from "mantine-form-zod-resolver";
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

  // controlled mode is required: field renderers destructure `value` from
  // getInputProps and pass it to Mantine inputs, which won't pick up updates
  // in uncontrolled mode.
  const form = useForm<FormShape>({
    mode: "controlled",
    initialValues: defaults,
    validate: zodResolver(fullSchema as never)
  });

  // Re-seed when defaults change. The parent mounts us with fields=[] and
  // populates them async; @mantine/form only reads initialValues at mount,
  // so without this re-seed the dynamic field keys never land in state and
  // zod reports them as undefined on submit. Skip if the user has typed.
  useEffect(() => {
    if (form.isDirty()) return;
    form.setInitialValues(defaults);
    form.setValues(defaults);
    // form ref is stable from useForm; intentionally not in deps to avoid
    // the setValues → re-render → re-run loop.
  }, [defaults]); // eslint-disable-line react-hooks/exhaustive-deps -- `form` is stable from useForm; including it loops setValues -> render -> re-run

  const [isSubmitting, setIsSubmitting] = useState(false);

  const submit = form.onSubmit(async (values) => {
    const { __name, __status, __dueDate, __assigneeIds, ...rest } = values;
    const trimmedStatus = __status.trim();
    setIsSubmitting(true);
    try {
      await onSubmit({
        name: __name,
        status: trimmedStatus.length === 0 ? null : trimmedStatus,
        dueDate: __dueDate.length === 0 ? null : __dueDate,
        values: rest,
        assigneeIds: __assigneeIds
      });
    } finally {
      setIsSubmitting(false);
    }
  });

  return (
    <Box component="form" onSubmit={submit} noValidate>
      <Stack gap="md">
        {topLevelError && (
          <Alert color="red" variant="light">
            {topLevelError}
          </Alert>
        )}

        <TextInput label="Name" {...form.getInputProps("__name")} />

        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
          <TextInput
            label="Status"
            placeholder="e.g. Open, In progress"
            {...form.getInputProps("__status")}
          />
          <TextInput
            label="Due Date"
            type="date"
            {...form.getInputProps("__dueDate")}
          />
        </SimpleGrid>

        <Input.Wrapper label="Assignees">
          <AssigneePicker
            value={(form.getValues().__assigneeIds as string[] | undefined) ?? []}
            onChange={(next) => form.setFieldValue("__assigneeIds", next)}
            disabled={busy}
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
                <FormImpl field={field} form={form as never} />
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
