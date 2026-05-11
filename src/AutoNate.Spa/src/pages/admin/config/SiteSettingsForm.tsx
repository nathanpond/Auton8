import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Card,
  Group,
  Stack,
  Switch,
  Text,
  TextInput
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useAdminSiteSettings,
  useUpdateSiteSettings
} from "@/hooks/useSiteSettings";
import { SettingDefinition, SiteSettingGroup } from "@/api/siteSettings";

type Props = {
  group: SiteSettingGroup;
  title: string;
  blurb: string;
};

// Renders every SettingDefinition in `group` as an editable form. Adding new
// settings to that group only requires adding a SettingDefinition on the
// backend — no UI changes here.
export default function SiteSettingsForm({ group, title, blurb }: Props) {
  const { data, isLoading, isError, error } = useAdminSiteSettings();
  const update = useUpdateSiteSettings();

  const definitions = useMemo<SettingDefinition[]>(
    () => (Array.isArray(data?.definitions) ? data!.definitions : [])
      .filter((d) => d.group === group),
    [data, group]
  );

  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!data || !Array.isArray(data.definitions)) return;
    const values = data.values ?? {};
    const next: Record<string, unknown> = {};
    for (const def of data.definitions) {
      if (def.group !== group) continue;
      next[def.key] = values[def.key];
    }
    setDraft(next);
  }, [data, group]);

  const dirty = useMemo(() => {
    if (!data) return false;
    for (const def of definitions) {
      if (!Object.is(draft[def.key], data.values[def.key])) return true;
    }
    return false;
  }, [data, definitions, draft]);

  const setValue = (key: string, value: unknown) => {
    setDraft((prev) => ({ ...prev, [key]: value }));
    setSaveMessage(null);
  };

  const handleSave = async () => {
    if (!data || !dirty) return;
    setSaveMessage(null);
    const updates: Record<string, unknown> = {};
    for (const def of definitions) {
      if (!Object.is(draft[def.key], data.values[def.key])) {
        updates[def.key] = draft[def.key];
      }
    }
    try {
      await update.mutateAsync(updates);
      setSaveMessage("Settings saved.");
    } catch (err) {
      setSaveMessage(err instanceof Error ? err.message : "Failed to save.");
    }
  };

  const handleReset = () => {
    if (!data) return;
    const next: Record<string, unknown> = {};
    for (const def of definitions) next[def.key] = data.values[def.key];
    setDraft(next);
    setSaveMessage(null);
  };

  return (
    <>
      <PageHeader
        title={title}
        description={blurb}
        actions={
          <Group gap="xs">
            <Button
              variant="default"
              onClick={handleReset}
              disabled={!dirty || update.isPending}
            >
              Reset
            </Button>
            <Button
              onClick={() => void handleSave()}
              loading={update.isPending}
              disabled={!dirty}
            >
              Save changes
            </Button>
          </Group>
        }
      />

      {isLoading && (
        <Card withBorder shadow="sm">
          <Text c="dimmed">Loading…</Text>
        </Card>
      )}

      {isError && (
        <Alert color="red" variant="light">
          Failed to load settings: {(error as Error)?.message ?? "unknown error"}
        </Alert>
      )}

      {!isLoading && !isError && definitions.length === 0 && (
        <Card withBorder shadow="sm">
          <Text c="dimmed">No settings in this group yet.</Text>
        </Card>
      )}

      {!isLoading && !isError && definitions.length > 0 && (
        <Card withBorder shadow="sm">
          <Stack gap={0}>
            {definitions.map((def, idx) => (
              <Box
                key={def.key}
                pt={idx > 0 ? "md" : 0}
                mt={idx > 0 ? "md" : 0}
                style={
                  idx > 0
                    ? { borderTop: "1px solid var(--mantine-color-default-border)" }
                    : undefined
                }
              >
                <SettingControl
                  definition={def}
                  value={draft[def.key]}
                  onChange={(v) => setValue(def.key, v)}
                />
              </Box>
            ))}
          </Stack>
        </Card>
      )}

      {saveMessage && (
        <Text size="sm" c="dimmed" mt="xs" role="status">
          {saveMessage}
        </Text>
      )}
    </>
  );
}

function SettingControl({
  definition,
  value,
  onChange
}: {
  definition: SettingDefinition;
  value: unknown;
  onChange: (next: unknown) => void;
}) {
  if (definition.type === "bool") {
    const checked = typeof value === "boolean" ? value : Boolean(definition.defaultValue);
    return (
      <Switch
        id={`setting-${definition.key}`}
        checked={checked}
        onChange={(e) => onChange(e.currentTarget.checked)}
        label={<strong>{definition.label}</strong>}
        description={definition.description}
      />
    );
  }

  if (definition.type === "string") {
    const text = typeof value === "string" ? value : String(definition.defaultValue ?? "");
    return (
      <TextInput
        id={`setting-${definition.key}`}
        type="text"
        label={<strong>{definition.label}</strong>}
        description={definition.description}
        value={text}
        onChange={(e) => onChange(e.currentTarget.value)}
      />
    );
  }

  if (definition.type === "int") {
    const num = typeof value === "number" ? value : Number(definition.defaultValue ?? 0);
    return (
      <TextInput
        id={`setting-${definition.key}`}
        type="number"
        label={<strong>{definition.label}</strong>}
        description={definition.description}
        value={Number.isFinite(num) ? num : 0}
        onChange={(e) => {
          const parsed = Number.parseInt(e.currentTarget.value, 10);
          onChange(Number.isFinite(parsed) ? parsed : 0);
        }}
      />
    );
  }

  return (
    <Text c="dimmed" size="sm">
      Unknown setting type for <code>{definition.key}</code>.
    </Text>
  );
}
