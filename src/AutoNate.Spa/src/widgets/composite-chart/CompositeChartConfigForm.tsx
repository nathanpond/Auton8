import { useMemo } from "react";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Group,
  Select,
  Stack,
  Text,
  TextInput,
  Tooltip
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import { DataSourcePicker } from "@/widgets/DataSourcePicker";
import type { WidgetConfigFormProps } from "@/widgets/registry";
import {
  RECORD_BUILTIN_CATEGORY_OPTIONS,
  RECORD_BUILTIN_KEY_NUMBER,
  RECORD_CUSTOM_FIELD_PREFIX
} from "@/widgets/quadrant-chart/QuadrantChartWidget";
import type {
  CompositeAggregation,
  CompositeChartWidgetConfig,
  CompositeSeries,
  CompositeSeriesType
} from "./CompositeChartWidget.config";

const NO_SELECTION_SENTINEL = "__none__";

const SERIES_TYPE_OPTIONS: { value: CompositeSeriesType; label: string }[] = [
  { value: "bar", label: "Bar" },
  { value: "line", label: "Line" },
  { value: "area", label: "Area" }
];

const AGGREGATION_OPTIONS: { value: CompositeAggregation; label: string }[] = [
  { value: "count", label: "Count" },
  { value: "sum", label: "Sum" },
  { value: "avg", label: "Average" }
];

export function CompositeChartConfigForm({
  value,
  onChange,
  errors
}: WidgetConfigFormProps<CompositeChartWidgetConfig>) {
  const sourceType = value.dataSource.type;
  const isRecords = sourceType === "records";
  const isWorkflows = sourceType === "workflows";
  const isSavedQuery = sourceType === "savedQuery";
  const isAdHocAql = sourceType === "adHocAql";
  const isAqlSource = isSavedQuery || isAdHocAql;

  const recordTypeId = isRecords ? value.dataSource.recordTypeId : "";
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);

  // Bucket column options: any field for records (the X axis is
  // categorical, but a numeric field will read as discrete buckets too);
  // any column for AQL.
  const recordBucketOptions = useMemo(() => {
    const groups: { group: string; items: { value: string; label: string }[] }[] = [
      { group: "Built-in", items: RECORD_BUILTIN_CATEGORY_OPTIONS.map((o) => ({ ...o })) }
    ];
    const fields = (fieldsQuery.data ?? [])
      .filter((f) => !f.isArchived)
      .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName));
    if (fields.length > 0) {
      groups.push({
        group: "Custom fields",
        items: fields.map((f) => ({
          value: `${RECORD_CUSTOM_FIELD_PREFIX}${f.fieldKey}`,
          label: f.displayName
        }))
      });
    }
    return groups;
  }, [fieldsQuery.data]);

  const recordValueOptions = useMemo(() => {
    const groups: { group: string; items: { value: string; label: string }[] }[] = [
      {
        group: "Built-in",
        items: [{ value: RECORD_BUILTIN_KEY_NUMBER, label: "Key number" }]
      }
    ];
    const fields = (fieldsQuery.data ?? [])
      .filter((f) => !f.isArchived && f.dataType === "number")
      .sort((a, b) => a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName));
    if (fields.length > 0) {
      groups.push({
        group: "Custom numeric fields",
        items: fields.map((f) => ({
          value: `${RECORD_CUSTOM_FIELD_PREFIX}${f.fieldKey}`,
          label: f.displayName
        }))
      });
    }
    return groups;
  }, [fieldsQuery.data]);

  // AQL probe — same hook pattern as the other chart widgets so the
  // probe is a cache hit when the user is editing multiple chart
  // widgets backed by the same query.
  const savedQueriesQuery = useSavedQueries();
  const savedQueryId = isSavedQuery ? value.dataSource.savedQueryId : "";
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );
  const aqlQueryText = isSavedQuery
    ? (savedQuery?.queryText ?? "")
    : isAdHocAql
      ? value.dataSource.adHocAqlQuery
      : "";
  const probeKey = isSavedQuery
    ? ["widget", "saved-query-composite", savedQueryId, aqlQueryText]
    : ["widget", "ad-hoc-aql-composite", aqlQueryText];
  const probeQuery = useQuery<AqlQueryResponse>({
    queryKey: probeKey,
    queryFn: ({ signal }) => executeQuery(aqlQueryText, signal),
    enabled: isAqlSource && Boolean(aqlQueryText) && (isAdHocAql || Boolean(savedQueryId)),
    staleTime: 30_000
  });

  const aqlColumns = probeQuery.data?.columns ?? [];
  const aqlAllOptions = useMemo(
    () => aqlColumns.map((c) => ({ value: c.name, label: c.name })),
    [aqlColumns]
  );
  const aqlNumericOptions = useMemo(
    () =>
      aqlColumns.filter((c) => c.dataType === "number").map((c) => ({ value: c.name, label: c.name })),
    [aqlColumns]
  );

  const bucketOptions = isRecords ? recordBucketOptions : isAqlSource ? aqlAllOptions : [];
  const valueOptions = isRecords ? recordValueOptions : isAqlSource ? aqlNumericOptions : [];
  const disabled =
    (isRecords && (!recordTypeId || fieldsQuery.isLoading)) ||
    (isAqlSource && (!aqlQueryText || probeQuery.isLoading));

  const updateSeries = (index: number, patch: Partial<CompositeSeries>) => {
    const next = value.series.map((s, i) => (i === index ? { ...s, ...patch } : s));
    onChange({ ...value, series: next });
  };

  const addSeries = () => {
    if (value.series.length >= 4) return;
    const palette = ["teal.6", "blue.5", "orange.5", "green.6"];
    const color = palette[value.series.length % palette.length];
    onChange({
      ...value,
      series: [
        ...value.series,
        {
          name: `Series ${value.series.length + 1}`,
          type: "line",
          valueColumn: "",
          aggregation: "sum",
          color
        }
      ]
    });
  };

  const removeSeries = (index: number) => {
    if (value.series.length <= 1) return;
    onChange({ ...value, series: value.series.filter((_, i) => i !== index) });
  };

  return (
    <Stack gap="sm">
      <DataSourcePicker
        value={value.dataSource}
        onChange={(next) => onChange({ ...value, dataSource: next })}
        errors={{
          type: errors["dataSource.type"],
          recordTypeId: errors["dataSource.recordTypeId"],
          workflowModelId: errors["dataSource.workflowModelId"],
          savedQueryId: errors["dataSource.savedQueryId"],
          adHocAqlQuery: errors["dataSource.adHocAqlQuery"]
        }}
      />

      {isWorkflows && (
        <Alert color="yellow" variant="light" title="Composite chart needs aggregated data">
          Workflow executions don’t expose numeric fields directly. Use the <strong>Saved Query</strong> or
          <strong> Ad-hoc AQL</strong> source with a query that aggregates per bucket and map its columns to
          series here.
        </Alert>
      )}

      {!isWorkflows && (
        <>
          <Select
            label="Bucket column"
            description={
              isRecords
                ? !recordTypeId
                  ? "Pick a record type to see fields."
                  : fieldsQuery.isLoading
                    ? "Loading fields…"
                    : "Categorical field — drives the X-axis bucket."
                : !aqlQueryText
                  ? isSavedQuery
                    ? "Pick a saved query first."
                    : "Write an AQL query first."
                  : probeQuery.isLoading
                    ? "Loading query columns…"
                    : "Query column whose distinct values drive the X axis."
            }
            data={bucketOptions as never}
            value={value.bucketColumn || null}
            onChange={(v) => onChange({ ...value, bucketColumn: v ?? "" })}
            allowDeselect={false}
            searchable
            disabled={disabled}
            error={errors.bucketColumn}
            comboboxProps={{ zIndex: 1080 }}
          />

          <Stack gap="xs">
            <Group justify="space-between" align="center">
              <Text size="sm" fw={500}>Series ({value.series.length}/4)</Text>
              <Button
                size="compact-xs"
                variant="default"
                onClick={addSeries}
                disabled={value.series.length >= 4 || disabled}
              >
                + Add series
              </Button>
            </Group>
            {value.series.map((s, i) => (
              <SeriesEditor
                key={i}
                index={i}
                series={s}
                disabled={disabled}
                valueOptions={valueOptions}
                onChange={(patch) => updateSeries(i, patch)}
                onRemove={value.series.length > 1 ? () => removeSeries(i) : undefined}
              />
            ))}
          </Stack>

          {isAqlSource && probeQuery.isError && (
            <Text c="red" size="xs">Could not load query columns — the query failed to execute.</Text>
          )}
        </>
      )}

      <TextInput
        label="X axis label"
        value={value.xAxisLabel}
        onChange={(e) => onChange({ ...value, xAxisLabel: e.currentTarget.value })}
      />
      <TextInput
        label="Y axis label"
        value={value.yAxisLabel}
        onChange={(e) => onChange({ ...value, yAxisLabel: e.currentTarget.value })}
      />
    </Stack>
  );
}

function SeriesEditor({
  index,
  series,
  disabled,
  valueOptions,
  onChange,
  onRemove
}: {
  index: number;
  series: CompositeSeries;
  disabled: boolean;
  valueOptions: unknown;
  onChange: (patch: Partial<CompositeSeries>) => void;
  onRemove?: () => void;
}) {
  const valueDisabled = series.aggregation === "count";
  return (
    <Box
      style={{
        border: "1px solid var(--mantine-color-default-border)",
        borderRadius: "var(--mantine-radius-sm)",
        padding: 10
      }}
    >
      <Group justify="space-between" align="center" mb={6}>
        <Text size="xs" c="dimmed">Series {index + 1}</Text>
        {onRemove ? (
          <Tooltip label="Remove series" position="left" withArrow>
            <ActionIcon
              variant="subtle"
              color="red"
              size="sm"
              onClick={onRemove}
              aria-label={`Remove series ${index + 1}`}
            >
              <span style={{ fontSize: 14, lineHeight: 1 }}>×</span>
            </ActionIcon>
          </Tooltip>
        ) : null}
      </Group>
      <Stack gap="xs">
        <Group grow gap="xs" align="end" wrap="nowrap">
          <Select
            label="Type"
            data={SERIES_TYPE_OPTIONS}
            value={series.type}
            onChange={(v) => v && onChange({ type: v as CompositeSeriesType })}
            allowDeselect={false}
            comboboxProps={{ zIndex: 1080 }}
          />
          <Select
            label="Aggregation"
            data={AGGREGATION_OPTIONS}
            value={series.aggregation}
            onChange={(v) => v && onChange({ aggregation: v as CompositeAggregation })}
            allowDeselect={false}
            comboboxProps={{ zIndex: 1080 }}
          />
        </Group>
        <Select
          label="Value column"
          description={valueDisabled ? "Not needed for Count — bucket rows are counted directly." : undefined}
          data={
            [
              { value: NO_SELECTION_SENTINEL, label: "None" },
              ...((valueOptions as unknown) as Array<
                { value: string; label: string } | { group: string; items: { value: string; label: string }[] }
              >)
            ] as never
          }
          value={series.valueColumn || NO_SELECTION_SENTINEL}
          onChange={(v) => onChange({ valueColumn: v === NO_SELECTION_SENTINEL ? "" : (v ?? "") })}
          allowDeselect={false}
          searchable
          disabled={disabled || valueDisabled}
          comboboxProps={{ zIndex: 1080 }}
        />
        <TextInput
          label="Display name"
          value={series.name}
          onChange={(e) => onChange({ name: e.currentTarget.value })}
          placeholder={series.valueColumn || `Series ${index + 1}`}
        />
        <TextInput
          label="Color"
          description="Mantine color token (e.g. 'teal.6', 'blue.5')."
          value={series.color}
          onChange={(e) => onChange({ color: e.currentTarget.value })}
        />
      </Stack>
    </Box>
  );
}

