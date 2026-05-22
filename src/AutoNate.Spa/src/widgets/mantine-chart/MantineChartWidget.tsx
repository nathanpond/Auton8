import { useMemo, type ReactNode } from "react";
import { Alert, Box, Loader, Stack, Text } from "@mantine/core";
import { AreaChart, BarChart, DonutChart, LineChart } from "@mantine/charts";
import { useRecords } from "@/hooks/useRecords";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useExecutionsPage } from "@/hooks/useExecutions";
import type { RecordModel, RecordTypeField } from "@/types/records";
import type { WorkflowExecutionSummary } from "@/types/flowable";
import type { WidgetRuntimeProps } from "@/widgets/registry";
import { ASSIGNEE_COUNT_GROUP_BY, CUSTOM_FIELD_GROUP_BY_PREFIX } from "./groupBy";
import type { MantineChartWidgetConfig } from "./MantineChartWidget.config";

type ChartPoint = { label: string; value: number };

const DONUT_COLORS = ["teal.6", "blue.5", "orange.5", "green.6", "pink.5", "violet.5", "cyan.6", "yellow.6"];

export function MantineChartWidget({ config }: WidgetRuntimeProps<MantineChartWidgetConfig>) {
  if (config.dataSource.type === "records") {
    return <RecordsChart config={config} />;
  }
  return <WorkflowsChart config={config} />;
}

function RecordsChart({ config }: { config: MantineChartWidgetConfig }) {
  const recordTypeId = config.dataSource.recordTypeId?.trim() ?? "";
  const recordsQuery = useRecords(
    { recordTypeId, pageSize: 200, includeArchived: false },
    Boolean(recordTypeId)
  );
  // Only need the fields when the user picked a custom-field group-by;
  // otherwise built-in resolution is enough. Query is enabled regardless
  // because the cost of an extra list-fields call is trivial and it
  // keeps memoization stable across group-by changes.
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);

  const data = useMemo<ChartPoint[]>(() => {
    const items = recordsQuery.data?.items ?? [];
    const fieldByKey = new Map<string, RecordTypeField>(
      (fieldsQuery.data ?? []).map((f) => [f.fieldKey, f])
    );
    return bucketize(items, (r) => labelForRecord(r, config.recordGroupBy, fieldByKey));
  }, [recordsQuery.data, fieldsQuery.data, config.recordGroupBy]);

  if (!recordTypeId) {
    return (
      <Box p="sm">
        <Alert color="blue" variant="light">
          "All records" isn't supported yet — pick a record type in widget settings.
        </Alert>
      </Box>
    );
  }
  if (recordsQuery.isLoading) return <LoadingState />;
  if (recordsQuery.isError) {
    return <ErrorState message="Failed to load records." />;
  }
  if (data.length === 0) return <EmptyState />;
  const groupByLabel = labelForRecordGroupBy(
    config.recordGroupBy,
    fieldsQuery.data ?? []
  );
  return renderChart(data, config, groupByLabel);
}

function WorkflowsChart({ config }: { config: MantineChartWidgetConfig }) {
  const modelId = config.dataSource.workflowModelId?.trim() ?? "";
  const executionsQuery = useExecutionsPage({
    page: 0,
    pageSize: 500,
    workflowModelId: modelId || undefined
  });

  const data = useMemo<ChartPoint[]>(() => {
    const items = executionsQuery.data?.items ?? [];
    return bucketize(items, (e) => labelForExecution(e, config.workflowGroupBy));
  }, [executionsQuery.data, config.workflowGroupBy]);

  if (executionsQuery.isLoading) return <LoadingState />;
  if (executionsQuery.isError) {
    return <ErrorState message="Failed to load workflow executions." />;
  }
  if (data.length === 0) return <EmptyState />;
  const groupByLabel = labelForWorkflowGroupBy(config.workflowGroupBy);
  return renderChart(data, config, groupByLabel);
}

// ---- Shared rendering helpers ----

function renderChart(data: ChartPoint[], config: MantineChartWidgetConfig, groupByLabel: string) {
  const series = [{ name: "value", label: config.seriesLabel, color: config.seriesColor }];
  // Recharts' Tooltip accepts labelFormatter(label, payload) which sets the
  // tooltip header. Mantine charts spread `tooltipProps` onto the inner
  // Tooltip, so prefixing here gives us "Status: Active" without needing a
  // fully custom tooltip component.
  const tooltipProps = {
    labelFormatter: (label: ReactNode) => `${groupByLabel}: ${String(label ?? "")}`
  };
  switch (config.chartType) {
    case "line":
      return (
        <LineChart
          h="100%"
          data={data}
          dataKey="label"
          series={series}
          curveType="monotone"
          xAxisLabel={groupByLabel}
          tooltipProps={tooltipProps}
        />
      );
    case "area":
      return (
        <AreaChart
          h="100%"
          data={data}
          dataKey="label"
          series={series}
          curveType="monotone"
          xAxisLabel={groupByLabel}
          tooltipProps={tooltipProps}
        />
      );
    case "donut":
      // DonutChart renders one tooltip per segment with the segment's name +
      // value. The group-by context goes in the chart label slot instead.
      return (
        <DonutChart
          h="100%"
          chartLabel={groupByLabel}
          data={data.map((d, i) => ({
            name: d.label,
            value: d.value,
            color: DONUT_COLORS[i % DONUT_COLORS.length]
          }))}
        />
      );
    case "bar":
    default:
      return (
        <BarChart
          h="100%"
          data={data}
          dataKey="label"
          series={series}
          xAxisLabel={groupByLabel}
          tooltipProps={tooltipProps}
        />
      );
  }
}

function LoadingState() {
  return (
    <Stack align="center" justify="center" h="100%">
      <Loader size="sm" />
      <Text c="dimmed" size="sm">Loading…</Text>
    </Stack>
  );
}

function ErrorState({ message }: { message: string }) {
  return (
    <Box p="sm">
      <Alert color="red" variant="light">{message}</Alert>
    </Box>
  );
}

function EmptyState() {
  return (
    <Stack align="center" justify="center" h="100%">
      <Text c="dimmed" size="sm">No data to chart.</Text>
    </Stack>
  );
}

function bucketize<T>(items: T[], labelFn: (item: T) => string): ChartPoint[] {
  const counts = new Map<string, number>();
  for (const item of items) {
    const key = labelFn(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return Array.from(counts.entries())
    .map(([label, value]) => ({ label, value }))
    .sort((a, b) => b.value - a.value);
}

function labelForRecord(
  r: RecordModel,
  groupBy: string,
  fieldByKey: Map<string, RecordTypeField>
): string {
  // Derived bucket: count of assignees → "0 assignees" / "1 assignee" / etc.
  if (groupBy === ASSIGNEE_COUNT_GROUP_BY) {
    const n = r.assigneeIds.length;
    if (n === 0) return "0 assignees";
    if (n === 1) return "1 assignee";
    return `${n} assignees`;
  }
  // Custom field on the record type. `record.values` holds the raw value;
  // some types (option, date) round-trip as primitives so a simple
  // `String()` is enough for v1 bucketing.
  if (groupBy.startsWith(CUSTOM_FIELD_GROUP_BY_PREFIX)) {
    const fieldKey = groupBy.slice(CUSTOM_FIELD_GROUP_BY_PREFIX.length);
    const raw = r.values?.[fieldKey];
    return formatFieldValue(raw, fieldByKey.get(fieldKey));
  }
  // Built-in RecordModel properties.
  switch (groupBy) {
    case "name":
      return r.name || "—";
    case "key":
      return r.key || "—";
    case "dueDate":
      return r.dueDate ?? "No due date";
    case "status":
    default:
      return r.status ?? "—";
  }
}

function formatFieldValue(raw: unknown, field: RecordTypeField | undefined): string {
  if (raw === null || raw === undefined || raw === "") return "—";
  if (Array.isArray(raw)) {
    // Multi-select / array-shaped fields: bucket per unique-value-set
    // string so two records with the same selections fall in the same
    // bucket. Ordering is normalized so [a,b] and [b,a] don't split.
    if (raw.length === 0) return "—";
    return [...raw].map((v) => String(v)).sort().join(", ");
  }
  if (typeof raw === "boolean") return raw ? "Yes" : "No";
  if (field?.dataType === "date" && typeof raw === "string") return raw;
  return String(raw);
}

function labelForExecution(
  e: WorkflowExecutionSummary,
  groupBy: MantineChartWidgetConfig["workflowGroupBy"]
): string {
  switch (groupBy) {
    case "model":
      return e.workflowModelName ?? "—";
    case "status":
    default:
      return e.status ?? "—";
  }
}

// Human-readable axis/tooltip name for the chosen group-by. Mirrors the
// options surfaced in the ConfigForm so the chart self-describes what's
// being bucketed.
const RECORD_GROUP_BY_LABELS: Record<string, string> = {
  status: "Status",
  name: "Name",
  key: "Key",
  dueDate: "Due date",
  [ASSIGNEE_COUNT_GROUP_BY]: "Assignee count"
};

function labelForRecordGroupBy(groupBy: string, fields: RecordTypeField[]): string {
  if (groupBy.startsWith(CUSTOM_FIELD_GROUP_BY_PREFIX)) {
    const fieldKey = groupBy.slice(CUSTOM_FIELD_GROUP_BY_PREFIX.length);
    const field = fields.find((f) => f.fieldKey === fieldKey);
    return field?.displayName ?? fieldKey;
  }
  return RECORD_GROUP_BY_LABELS[groupBy] ?? groupBy;
}

function labelForWorkflowGroupBy(groupBy: MantineChartWidgetConfig["workflowGroupBy"]): string {
  switch (groupBy) {
    case "model":
      return "Model";
    case "status":
    default:
      return "Status";
  }
}
