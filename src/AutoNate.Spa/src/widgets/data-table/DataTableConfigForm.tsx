import { MultiSelect, NumberInput, Stack, Switch } from "@mantine/core";
import { DataSourcePicker } from "@/widgets/DataSourcePicker";
import type { WidgetConfigFormProps } from "@/widgets/registry";
import {
  RECORD_COLUMNS,
  WORKFLOW_COLUMNS,
  type DataTableWidgetConfig
} from "./DataTableWidget.config";

const RECORD_COLUMN_LABELS: Record<(typeof RECORD_COLUMNS)[number], string> = {
  key: "Key",
  name: "Name",
  status: "Status",
  dueDate: "Due date",
  assignees: "Assignees",
  updatedAtUtc: "Updated"
};

const WORKFLOW_COLUMN_LABELS: Record<(typeof WORKFLOW_COLUMNS)[number], string> = {
  name: "Run name",
  model: "Model",
  status: "Status",
  currentStep: "Current step",
  startedAtUtc: "Started",
  lastActivityAtUtc: "Last activity"
};

export function DataTableConfigForm({
  value,
  onChange,
  errors
}: WidgetConfigFormProps<DataTableWidgetConfig>) {
  const isRecords = value.dataSource.type === "records";
  const columnOptions = isRecords
    ? RECORD_COLUMNS.map((c) => ({ value: c, label: RECORD_COLUMN_LABELS[c] }))
    : WORKFLOW_COLUMNS.map((c) => ({ value: c, label: WORKFLOW_COLUMN_LABELS[c] }));
  const columnValue = isRecords ? value.recordColumns : value.workflowColumns;
  const columnError = isRecords ? errors.recordColumns : errors.workflowColumns;

  return (
    <Stack gap="sm">
      <DataSourcePicker
        value={value.dataSource}
        onChange={(next) => onChange({ ...value, dataSource: next })}
        allowedTypes={["records", "workflows"]}
        errors={{
          type: errors["dataSource.type"],
          recordTypeId: errors["dataSource.recordTypeId"],
          workflowModelId: errors["dataSource.workflowModelId"]
        }}
      />
      <MultiSelect
        label="Columns"
        description="Built-in columns shown in the table."
        data={columnOptions}
        value={columnValue}
        onChange={(next) =>
          onChange(
            isRecords
              ? { ...value, recordColumns: next as DataTableWidgetConfig["recordColumns"] }
              : { ...value, workflowColumns: next as DataTableWidgetConfig["workflowColumns"] }
          )
        }
        error={columnError}
        clearable={false}
        comboboxProps={{ zIndex: 1080 }}
      />
      <NumberInput
        label="Page size"
        description="Rows per page."
        value={value.pageSize}
        onChange={(v) =>
          onChange({ ...value, pageSize: typeof v === "number" ? v : 25 })
        }
        min={5}
        max={200}
        error={errors.pageSize}
      />
      {isRecords ? (
        <Switch
          label="Include archived"
          description="Show archived rows alongside active ones."
          checked={value.includeArchived}
          onChange={(e) => onChange({ ...value, includeArchived: e.currentTarget.checked })}
        />
      ) : null}
    </Stack>
  );
}
