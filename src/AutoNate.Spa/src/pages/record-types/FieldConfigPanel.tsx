import {
  ActionIcon,
  Box,
  Button,
  Grid,
  Group,
  NativeSelect,
  Stack,
  Switch,
  Text,
  TextInput
} from "@mantine/core";
import { FieldDataType, OptionChoice } from "@/types/records";

type Props = {
  dataType: FieldDataType;
  config: Record<string, unknown>;
  onChange: (next: Record<string, unknown>) => void;
};

export default function FieldConfigPanel({ dataType, config, onChange }: Props) {
  switch (dataType) {
    case "text":
      return <TextConfig config={config} onChange={onChange} />;
    case "number":
      return <NumberConfig config={config} onChange={onChange} />;
    case "date":
      return <DateConfig config={config} onChange={onChange} />;
    case "phone":
      return <PhoneConfig config={config} onChange={onChange} />;
    case "email":
      return (
        <Text size="sm" c="dimmed">
          No additional configuration.
        </Text>
      );
    case "option":
      return <OptionConfig config={config} onChange={onChange} />;
    case "boolean":
      return (
        <Text size="sm" c="dimmed">
          No additional configuration.
        </Text>
      );
    default:
      return (
        <Text size="sm" c="yellow">
          Unknown data type; config will be sent as-is.
        </Text>
      );
  }
}

function TextConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "single";
  const maxLength = Number(config.maxLength ?? 4000);
  return (
    <Stack gap="md">
      <NativeSelect
        label="Variant"
        value={variant}
        onChange={(e) => onChange({ ...config, variant: e.currentTarget.value })}
        data={[
          { value: "single", label: "Single-line" },
          { value: "multi", label: "Multi-line" }
        ]}
      />
      <TextInput
        label="Max length"
        type="number"
        min={1}
        max={65536}
        value={maxLength}
        onChange={(e) => onChange({ ...config, maxLength: Number(e.currentTarget.value) })}
      />
    </Stack>
  );
}

function NumberConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "decimal";
  const precision = Number(config.precision ?? 2);
  const min = config.min === null || config.min === undefined ? "" : String(config.min);
  const max = config.max === null || config.max === undefined ? "" : String(config.max);

  return (
    <Stack gap="md">
      <NativeSelect
        label="Variant"
        value={variant}
        onChange={(e) => onChange({ ...config, variant: e.currentTarget.value })}
        data={[
          { value: "decimal", label: "Decimal" },
          { value: "integer", label: "Integer" }
        ]}
      />
      {variant === "decimal" && (
        <TextInput
          label="Precision (decimal places)"
          type="number"
          min={0}
          max={12}
          value={precision}
          onChange={(e) => onChange({ ...config, precision: Number(e.currentTarget.value) })}
        />
      )}
      <Grid>
        <Grid.Col span={6}>
          <TextInput
            label="Min"
            type="number"
            value={min}
            onChange={(e) =>
              onChange({
                ...config,
                min: e.currentTarget.value === "" ? null : Number(e.currentTarget.value)
              })
            }
          />
        </Grid.Col>
        <Grid.Col span={6}>
          <TextInput
            label="Max"
            type="number"
            value={max}
            onChange={(e) =>
              onChange({
                ...config,
                max: e.currentTarget.value === "" ? null : Number(e.currentTarget.value)
              })
            }
          />
        </Grid.Col>
      </Grid>
    </Stack>
  );
}

function DateConfig({ config, onChange }: Omit<Props, "dataType">) {
  const variant = (config.variant as string) ?? "date";
  return (
    <NativeSelect
      label="Variant"
      value={variant}
      onChange={(e) => onChange({ ...config, variant: e.currentTarget.value })}
      data={[
        { value: "date", label: "Date only" },
        { value: "datetime", label: "Date & time" },
        { value: "range", label: "Date range" }
      ]}
    />
  );
}

function PhoneConfig({ config, onChange }: Omit<Props, "dataType">) {
  const region = (config.region as string) ?? "US";
  return (
    <TextInput
      label="Default region (ISO country code)"
      type="text"
      maxLength={3}
      value={region}
      onChange={(e) => onChange({ ...config, region: e.currentTarget.value.toUpperCase() })}
    />
  );
}

function OptionConfig({ config, onChange }: Omit<Props, "dataType">) {
  const multi = Boolean(config.multi);
  const choices = Array.isArray(config.choices) ? (config.choices as OptionChoice[]) : [];

  const updateChoice = (index: number, patch: Partial<OptionChoice>) => {
    const next = choices.map((c, i) => (i === index ? { ...c, ...patch } : c));
    onChange({ ...config, choices: next });
  };

  const addChoice = () => {
    const next = [
      ...choices,
      { value: `option_${choices.length + 1}`, label: `Option ${choices.length + 1}` }
    ];
    onChange({ ...config, choices: next });
  };

  const removeChoice = (index: number) => {
    onChange({ ...config, choices: choices.filter((_, i) => i !== index) });
  };

  return (
    <Stack gap="md">
      <Switch
        id="option-multi"
        checked={multi}
        onChange={(e) => onChange({ ...config, multi: e.currentTarget.checked })}
        label="Allow multiple selections"
      />
      <Box>
        <Text size="sm" fw={500} mb={4}>
          Choices
        </Text>
        <Stack gap="xs">
          {choices.map((choice, index) => (
            <Group key={index} gap="xs" wrap="nowrap">
              <TextInput
                label="value"
                size="xs"
                style={{ flex: 1 }}
                value={choice.value}
                onChange={(e) => updateChoice(index, { value: e.currentTarget.value })}
              />
              <TextInput
                label="label"
                size="xs"
                style={{ flex: 1 }}
                value={choice.label}
                onChange={(e) => updateChoice(index, { label: e.currentTarget.value })}
              />
              <ActionIcon
                variant="outline"
                color="red"
                size="lg"
                mt={20}
                onClick={() => removeChoice(index)}
                aria-label="Remove choice"
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </Group>
          ))}
        </Stack>
      </Box>
      <Button
        size="xs"
        variant="default"
        leftSection={<i className="fa fa-plus" />}
        onClick={addChoice}
      >
        Add choice
      </Button>
    </Stack>
  );
}
