import { useMemo } from "react";
import { Select, Stack, TextInput } from "@mantine/core";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { DataSourcePicker } from "@/widgets/DataSourcePicker";
import type { WidgetConfigFormProps } from "@/widgets/registry";
import { ASSIGNEE_COUNT_GROUP_BY, CUSTOM_FIELD_GROUP_BY_PREFIX } from "./groupBy";
import type { MantineChartWidgetConfig } from "./MantineChartWidget.config";

const CHART_TYPE_OPTIONS = [
  { value: "bar", label: "Bar" },
  { value: "line", label: "Line" },
  { value: "area", label: "Area" },
  { value: "donut", label: "Donut" }
];

// Built-in RecordModel properties usable as a group-by axis. Excludes
// fields that aren't useful to bucket on (id, createdBy, etc.).
const BUILTIN_RECORD_GROUP_BY_OPTIONS = [
  { value: "status", label: "Status" },
  { value: "name", label: "Name" },
  { value: "key", label: "Key" },
  { value: "dueDate", label: "Due date" },
  { value: ASSIGNEE_COUNT_GROUP_BY, label: "Assignee count" }
];

const WORKFLOW_GROUP_BY_OPTIONS = [
  { value: "status", label: "Status" },
  { value: "model", label: "Model" }
];

export function MantineChartConfigForm({
  value,
  onChange,
  errors
}: WidgetConfigFormProps<MantineChartWidgetConfig>) {
  const isRecords = value.dataSource.type === "records";
  const recordTypeId = isRecords ? value.dataSource.recordTypeId : "";

  // Only fetch fields when a specific record type is selected. "All
  // records" gives no field list, so the dropdown falls back to the
  // built-in options only.
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);

  const recordGroupByOptions = useMemo(() => {
    const groups = [
      { group: "Built-in", items: BUILTIN_RECORD_GROUP_BY_OPTIONS }
    ];
    const fields = fieldsQuery.data ?? [];
    if (fields.length > 0) {
      groups.push({
        group: "Custom fields",
        items: fields
          .filter((f) => !f.isArchived)
          .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName))
          .map((f) => ({
            value: `${CUSTOM_FIELD_GROUP_BY_PREFIX}${f.fieldKey}`,
            label: f.displayName
          }))
      });
    }
    return groups;
  }, [fieldsQuery.data]);

  // If the currently-selected groupBy points at a custom field the new
  // record type doesn't have, surface it as-is so the user sees what's
  // saved and can change it. Mantine Select renders unknown values fine
  // as long as we don't strip them from `data`.
  const groupByValue = isRecords ? value.recordGroupBy : value.workflowGroupBy;

  return (
    <Stack gap="sm">
      <Select
        label="Chart type"
        data={CHART_TYPE_OPTIONS}
        value={value.chartType}
        onChange={(v) => v && onChange({ ...value, chartType: v as MantineChartWidgetConfig["chartType"] })}
        allowDeselect={false}
        error={errors.chartType}
        comboboxProps={{ zIndex: 1080 }}
      />
      <DataSourcePicker
        value={value.dataSource}
        onChange={(next) => onChange({ ...value, dataSource: next })}
        errors={{
          type: errors["dataSource.type"],
          recordTypeId: errors["dataSource.recordTypeId"],
          workflowModelId: errors["dataSource.workflowModelId"]
        }}
      />
      {isRecords ? (
        <Select
          label="Group by"
          description={
            !recordTypeId
              ? "Pick a record type to see custom fields."
              : fieldsQuery.isLoading
                ? "Loading fields…"
                : "How rows are bucketed along the chart's x-axis."
          }
          data={recordGroupByOptions}
          value={groupByValue}
          onChange={(v) => v && onChange({ ...value, recordGroupBy: v })}
          allowDeselect={false}
          searchable={(fieldsQuery.data?.length ?? 0) > 6}
          comboboxProps={{ zIndex: 1080 }}
        />
      ) : (
        <Select
          label="Group by"
          description="How rows are bucketed along the chart's x-axis."
          data={WORKFLOW_GROUP_BY_OPTIONS}
          value={groupByValue}
          onChange={(v) => v && onChange({ ...value, workflowGroupBy: v as MantineChartWidgetConfig["workflowGroupBy"] })}
          allowDeselect={false}
          comboboxProps={{ zIndex: 1080 }}
        />
      )}
      <TextInput
        label="Series label"
        value={value.seriesLabel}
        onChange={(e) => onChange({ ...value, seriesLabel: e.currentTarget.value })}
        error={errors.seriesLabel}
      />
      <TextInput
        label="Series color"
        description="Mantine color token (e.g. 'teal.6', 'blue.5')."
        value={value.seriesColor}
        onChange={(e) => onChange({ ...value, seriesColor: e.currentTarget.value })}
        error={errors.seriesColor}
      />
    </Stack>
  );
}
