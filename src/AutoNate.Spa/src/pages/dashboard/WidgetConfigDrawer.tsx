import { useEffect, useMemo, useState } from "react";
import { Alert, Button, Drawer, Group, Stack, TextInput } from "@mantine/core";
import type { z } from "zod";
import type { DashboardWidget } from "@/api/dashboards";
import { AutoConfigForm } from "@/widgets/AutoConfigForm";
import { getWidget, mergeWidgetConfig } from "@/widgets";

type Props = {
  opened: boolean;
  widget: DashboardWidget | null;
  onClose: () => void;
  onSave: (next: { title: string | null; config: Record<string, unknown> }) => void;
};

// Right-side drawer that renders either the widget's bespoke ConfigForm or
// the auto-generated form derived from its Zod schema. Submit validates
// against the schema; errors map by Zod path back to per-field display.
function mergeStoredOrDefault(
  defaults: unknown,
  stored: unknown
): Record<string, unknown> {
  const fallback = (defaults as Record<string, unknown>) ?? {};
  if (!stored) return { ...fallback };
  return mergeWidgetConfig(fallback, stored) as Record<string, unknown>;
}

export function WidgetConfigDrawer({ opened, widget, onClose, onSave }: Props) {
  const definition = widget ? getWidget(widget.widgetType) : undefined;
  const schema = definition?.schema;

  const [title, setTitle] = useState<string>(widget?.title ?? "");
  const [value, setValue] = useState<Record<string, unknown>>(() =>
    mergeStoredOrDefault(definition?.defaultConfig, widget?.config)
  );
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    setTitle(widget?.title ?? "");
    setValue(mergeStoredOrDefault(definition?.defaultConfig, widget?.config));
    setErrors({});
  }, [widget, definition]);

  const isAutoForm = useMemo(
    () => Boolean(schema) && !definition?.ConfigForm,
    [schema, definition]
  );

  const submit = () => {
    if (!schema) {
      onSave({ title: title.trim() || null, config: value });
      return;
    }
    const result = (schema as z.ZodType).safeParse(value);
    if (!result.success) {
      const nextErrors: Record<string, string> = {};
      for (const issue of result.error.issues) {
        const key = issue.path.join(".") || "_root";
        nextErrors[key] = issue.message;
      }
      setErrors(nextErrors);
      return;
    }
    setErrors({});
    onSave({ title: title.trim() || null, config: result.data as Record<string, unknown> });
  };

  const CustomForm = definition?.ConfigForm;

  return (
    <Drawer
      opened={opened}
      onClose={onClose}
      title={widget ? `Configure: ${definition?.title ?? widget.widgetType}` : "Configure widget"}
      position="right"
      size="md"
      zIndex={1070}
    >
      <Stack gap="md">
        <TextInput
          label="Widget title"
          description={`Defaults to '${definition?.title ?? widget?.widgetType ?? ""}' when empty.`}
          placeholder={definition?.title ?? widget?.widgetType ?? ""}
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
        />

        {!definition ? (
          <Alert color="yellow" variant="light">
            No widget definition registered for <code>{widget?.widgetType}</code>.
          </Alert>
        ) : CustomForm ? (
          <CustomForm
            value={value as never}
            onChange={(next) => setValue(next as Record<string, unknown>)}
            errors={errors}
          />
        ) : isAutoForm && schema ? (
          <AutoConfigForm
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            schema={schema as any}
            value={value}
            onChange={setValue}
            errors={errors}
          />
        ) : null}

        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={onClose}>Cancel</Button>
          <Button onClick={submit}>Save</Button>
        </Group>
      </Stack>
    </Drawer>
  );
}
