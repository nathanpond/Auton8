import { useMemo } from "react";
import { Select, Stack } from "@mantine/core";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { useWorkflows } from "@/hooks/useWorkflows";
import {
  ALL_MODELS_LABEL,
  ALL_RECORDS_LABEL,
  type DataSourceConfig,
  type DataSourceType
} from "./dataSource";

const ALL_VALUE = "__all__";

type Props = {
  value: DataSourceConfig;
  onChange: (next: DataSourceConfig) => void;
  // Optional error keyed by the same paths the widget's Zod issues use.
  errors?: { type?: string; recordTypeId?: string; workflowModelId?: string };
};

// Cascading dropdown: first picks the data-source type, second narrows it.
// "All records" / "All models" map to an empty string on the config so the
// widget runtime can treat them as "no filter".
export function DataSourcePicker({ value, onChange, errors }: Props) {
  const recordTypesQuery = useRecordTypes();
  const workflowsQuery = useWorkflows();

  const recordOptions = useMemo(
    () => [
      { value: ALL_VALUE, label: ALL_RECORDS_LABEL },
      ...(recordTypesQuery.data ?? []).map((rt) => ({
        value: rt.id,
        label: rt.name
      }))
    ],
    [recordTypesQuery.data]
  );

  const workflowOptions = useMemo(
    () => [
      { value: ALL_VALUE, label: ALL_MODELS_LABEL },
      ...(workflowsQuery.data ?? []).map((m) => ({ value: m.id, label: m.name }))
    ],
    [workflowsQuery.data]
  );

  const subValue =
    value.type === "records"
      ? (value.recordTypeId || ALL_VALUE)
      : (value.workflowModelId || ALL_VALUE);
  const subOptions = value.type === "records" ? recordOptions : workflowOptions;
  const subPlaceholder = value.type === "records" ? ALL_RECORDS_LABEL : ALL_MODELS_LABEL;
  const subLabel = value.type === "records" ? "Record type" : "Workflow model";
  const subLoading =
    value.type === "records" ? recordTypesQuery.isLoading : workflowsQuery.isLoading;
  const subError =
    value.type === "records" ? errors?.recordTypeId : errors?.workflowModelId;

  const handleTypeChange = (next: DataSourceType | null) => {
    if (!next) return;
    onChange({ ...value, type: next });
  };

  const handleSubChange = (next: string | null) => {
    const id = !next || next === ALL_VALUE ? "" : next;
    if (value.type === "records") {
      onChange({ ...value, recordTypeId: id });
    } else {
      onChange({ ...value, workflowModelId: id });
    }
  };

  return (
    <Stack gap="xs">
      <Select
        label="Data source"
        description="Where the widget pulls its rows from."
        data={[
          { value: "records", label: "Records" },
          { value: "workflows", label: "Workflows" }
        ]}
        value={value.type}
        onChange={(v) => handleTypeChange(v as DataSourceType | null)}
        allowDeselect={false}
        error={errors?.type}
        comboboxProps={{ zIndex: 1080 }}
      />
      <Select
        label={subLabel}
        description={subLoading ? "Loading…" : undefined}
        placeholder={subPlaceholder}
        data={subOptions}
        value={subValue}
        onChange={handleSubChange}
        allowDeselect={false}
        error={subError}
        searchable={(subOptions.length ?? 0) > 6}
        comboboxProps={{ zIndex: 1080 }}
      />
    </Stack>
  );
}
