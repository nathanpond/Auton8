import { useEffect, useMemo } from "react";
import { MultiSelect, Select, Stack, Text, TextInput } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import { DataSourcePicker } from "@/widgets/DataSourcePicker";
import type { WidgetConfigFormProps } from "@/widgets/registry";
import { ASSIGNEE_COUNT_GROUP_BY, CUSTOM_FIELD_GROUP_BY_PREFIX } from "./groupBy";
import type { MantineChartWidgetConfig, MantineChartType } from "./MantineChartWidget.config";

// Mirrors DRILL_CAPABLE_CHARTS in MantineChartWidget.tsx; duplicated here
// so the config form can hide the drill picker for chart types that
// won't fire per-segment click events.
const DRILL_CAPABLE: ReadonlySet<MantineChartType> = new Set([
  "bar",
  "pie",
  "donut",
  "treemap",
  "bars-list"
]);

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

const VALUE_COLUMN_COUNT_SENTINEL = "__count__";

export function MantineChartConfigForm({
  value,
  onChange,
  errors
}: WidgetConfigFormProps<MantineChartWidgetConfig>) {
  const sourceType = value.dataSource.type;
  const isRecords = sourceType === "records";
  const isWorkflows = sourceType === "workflows";
  const isSavedQuery = sourceType === "savedQuery";

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

  // Saved-query column probing. Resolving label/value columns needs the
  // query's column list, which is only known after execution. We share the
  // same react-query key the runtime uses so this is a cache hit when the
  // user is configuring an already-rendered widget.
  const savedQueriesQuery = useSavedQueries();
  const savedQueryId = isSavedQuery ? value.dataSource.savedQueryId : "";
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );
  const savedQueryText = savedQuery?.queryText ?? "";
  const probeQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "saved-query-chart", savedQueryId, savedQueryText],
    queryFn: ({ signal }) => executeQuery(savedQueryText, signal),
    enabled: isSavedQuery && Boolean(savedQueryId) && Boolean(savedQueryText),
    staleTime: 30_000
  });

  const savedQueryColumns = probeQuery.data?.columns ?? [];
  const labelColumnOptions = useMemo(
    () => savedQueryColumns.map((c) => ({ value: c.name, label: c.name })),
    [savedQueryColumns]
  );
  // Value column is optional: blank → count rows per label.
  const valueColumnOptions = useMemo(
    () => [
      { value: VALUE_COLUMN_COUNT_SENTINEL, label: "Count rows" },
      ...savedQueryColumns
        .filter((c) => c.dataType === "number")
        .map((c) => ({ value: c.name, label: c.name }))
    ],
    [savedQueryColumns]
  );

  // When the column list resolves for the first time and no label column
  // is set yet, seed it with the first column so the runtime can render
  // without forcing the user to open settings just to pick a default.
  useEffect(() => {
    if (!isSavedQuery) return;
    if (savedQueryColumns.length === 0) return;
    if (value.savedQueryLabelColumn) return;
    onChange({ ...value, savedQueryLabelColumn: savedQueryColumns[0].name });
    // Including `value` in deps would loop on every keystroke — onChange
    // is referentially stable enough in practice and we guard above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isSavedQuery, savedQueryColumns]);

  // If the currently-selected groupBy points at a custom field the new
  // record type doesn't have, surface it as-is so the user sees what's
  // saved and can change it. Mantine Select renders unknown values fine
  // as long as we don't strip them from `data`.
  const groupByValue = isRecords ? value.recordGroupBy : value.workflowGroupBy;

  // Drill-into options for records: same set as group-by, minus the
  // assignee-count derived bucket (no equivalent filter clause) and
  // minus the currently-chosen initial axis (can't drill into itself).
  // Order of selected entries IS the drill order — Mantine MultiSelect
  // preserves selection order, which we document in the description.
  const recordDrillOptions = useMemo(() => {
    const builtIns = BUILTIN_RECORD_GROUP_BY_OPTIONS.filter(
      (o) => o.value !== ASSIGNEE_COUNT_GROUP_BY && o.value !== value.recordGroupBy
    );
    const groups: { group: string; items: { value: string; label: string }[] }[] = [
      { group: "Built-in", items: builtIns }
    ];
    const fields = fieldsQuery.data ?? [];
    if (fields.length > 0) {
      const customs = fields
        .filter((f) => !f.isArchived)
        .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName))
        .map((f) => ({
          value: `${CUSTOM_FIELD_GROUP_BY_PREFIX}${f.fieldKey}`,
          label: f.displayName
        }))
        .filter((o) => o.value !== value.recordGroupBy);
      if (customs.length > 0) groups.push({ group: "Custom fields", items: customs });
    }
    return groups;
  }, [fieldsQuery.data, value.recordGroupBy]);

  const workflowDrillOptions = useMemo(
    () => WORKFLOW_GROUP_BY_OPTIONS.filter((o) => o.value !== value.workflowGroupBy),
    [value.workflowGroupBy]
  );

  const drillSupported = DRILL_CAPABLE.has(value.chartType);

  // The schema can store drill axes that no longer exist as options
  // (e.g. a record type field that was archived after the widget was
  // saved). Mantine MultiSelect's strict mode would drop those — keep
  // them visible so the user can see and remove them deliberately.
  const recordDrillValueSet = useMemo(() => {
    const known = new Set<string>();
    for (const g of recordDrillOptions) for (const it of g.items) known.add(it.value);
    return known;
  }, [recordDrillOptions]);
  const recordDrillData = useMemo(() => {
    const groups = recordDrillOptions.map((g) => ({ ...g, items: [...g.items] }));
    const orphans = value.recordDrillBy.filter((v) => !recordDrillValueSet.has(v));
    if (orphans.length > 0) {
      groups.push({ group: "Saved (not available)", items: orphans.map((v) => ({ value: v, label: v })) });
    }
    return groups;
  }, [recordDrillOptions, recordDrillValueSet, value.recordDrillBy]);

  return (
    <Stack gap="sm">
      <DataSourcePicker
        value={value.dataSource}
        onChange={(next) => onChange({ ...value, dataSource: next })}
        errors={{
          type: errors["dataSource.type"],
          recordTypeId: errors["dataSource.recordTypeId"],
          workflowModelId: errors["dataSource.workflowModelId"],
          savedQueryId: errors["dataSource.savedQueryId"]
        }}
      />
      {isRecords && (
        <>
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
          {drillSupported && (
            <MultiSelect
              label="Drill into"
              description="Click a segment to filter to that value and re-bucket by the next axis. Selection order is the drill order."
              data={recordDrillData}
              value={value.recordDrillBy}
              onChange={(next) => onChange({ ...value, recordDrillBy: next })}
              clearable
              searchable
              comboboxProps={{ zIndex: 1080 }}
            />
          )}
        </>
      )}
      {isWorkflows && (
        <>
          <Select
            label="Group by"
            description="How rows are bucketed along the chart's x-axis."
            data={WORKFLOW_GROUP_BY_OPTIONS}
            value={groupByValue}
            onChange={(v) => v && onChange({ ...value, workflowGroupBy: v as MantineChartWidgetConfig["workflowGroupBy"] })}
            allowDeselect={false}
            comboboxProps={{ zIndex: 1080 }}
          />
          {drillSupported && (
            <MultiSelect
              label="Drill into"
              description="Click a segment to filter to that value and re-bucket by the next axis."
              data={workflowDrillOptions}
              value={value.workflowDrillBy}
              onChange={(next) =>
                onChange({
                  ...value,
                  workflowDrillBy: next as MantineChartWidgetConfig["workflowDrillBy"]
                })
              }
              clearable
              comboboxProps={{ zIndex: 1080 }}
            />
          )}
        </>
      )}
      {isSavedQuery && (
        <>
          <Select
            label="Label column"
            description={
              !savedQueryId
                ? "Pick a saved query first."
                : probeQuery.isLoading
                  ? "Loading query columns…"
                  : "Column used as the x-axis bucket or slice name."
            }
            data={labelColumnOptions}
            value={value.savedQueryLabelColumn || null}
            onChange={(v) => onChange({ ...value, savedQueryLabelColumn: v ?? "" })}
            allowDeselect={false}
            searchable={(labelColumnOptions.length ?? 0) > 6}
            disabled={!savedQueryId || probeQuery.isLoading}
            comboboxProps={{ zIndex: 1080 }}
          />
          <Select
            label="Value column"
            description={
              !savedQueryId
                ? "Pick a saved query first."
                : "Numeric column to sum per bucket. Pick 'Count rows' to count occurrences instead."
            }
            data={valueColumnOptions}
            value={value.savedQueryValueColumn || VALUE_COLUMN_COUNT_SENTINEL}
            onChange={(v) =>
              onChange({
                ...value,
                savedQueryValueColumn: v === VALUE_COLUMN_COUNT_SENTINEL ? "" : (v ?? "")
              })
            }
            allowDeselect={false}
            disabled={!savedQueryId || probeQuery.isLoading}
            comboboxProps={{ zIndex: 1080 }}
          />
          {probeQuery.isError && (
            <Text c="red" size="xs">
              Could not load query columns — the saved query failed to execute.
            </Text>
          )}
        </>
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
