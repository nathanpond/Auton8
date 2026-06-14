import { useCallback, useMemo, type ReactNode } from "react";
import { Alert, Anchor, Box, Breadcrumbs, ColorSwatch, Group, Loader, ScrollArea, Stack, Text } from "@mantine/core";
import { useElementSize } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import {
  AreaChart,
  BarChart,
  BarsList,
  DonutChart,
  FunnelChart,
  LineChart,
  PieChart,
  RadialBarChart,
  Treemap
} from "@mantine/charts";
import { useRecordSearch } from "@/hooks/useRecords";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useExecutionsPage } from "@/hooks/useExecutions";
import { useWorkflows } from "@/hooks/useWorkflows";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import type { RecordModel, RecordTypeField, SearchFilterClause } from "@/types/records";
import type { WorkflowExecutionSummary } from "@/types/flowable";
import type { WidgetRuntimeProps } from "@/widgets/registry";
import { ASSIGNEE_COUNT_GROUP_BY, CUSTOM_FIELD_GROUP_BY_PREFIX, groupByToFilterClause } from "./groupBy";
import { useDrillState, type DrillStep } from "./useDrillState";
import type { MantineChartWidgetConfig } from "./MantineChartWidget.config";

// Chart types whose Mantine wrappers + Recharts forward a per-segment
// onClick. Anything outside this set renders without drill UI even when
// `drillBy` is set on the config (the config form also hides the field
// for these types, so this is mostly defence-in-depth).
const DRILL_CAPABLE_CHARTS = new Set<MantineChartWidgetConfig["chartType"]>([
  "bar",
  "pie",
  "donut",
  "treemap",
  "bars-list"
]);

type ChartPoint = { label: string; value: number };

const DONUT_COLORS = ["teal.6", "blue.5", "orange.5", "green.6", "pink.5", "violet.5", "cyan.6", "yellow.6"];

export function MantineChartWidget({ config }: WidgetRuntimeProps<MantineChartWidgetConfig>) {
  if (config.dataSource.type === "records") {
    return <RecordsChart config={config} />;
  }
  if (config.dataSource.type === "savedQuery") {
    return <SavedQueryChart config={config} />;
  }
  if (config.dataSource.type === "adHocAql") {
    return <AdHocAqlChart config={config} />;
  }
  return <WorkflowsChart config={config} />;
}

function RecordsChart({ config }: { config: MantineChartWidgetConfig }) {
  const recordTypeId = config.dataSource.recordTypeId?.trim() ?? "";
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);

  // Reset drill whenever the widget's hierarchy is meaningfully edited.
  // A change to chart type / data source / initial group-by / drill chain
  // invalidates the existing stack because the levels no longer line up.
  const resetKey = useMemo(
    () =>
      JSON.stringify([
        "records",
        recordTypeId,
        config.recordGroupBy,
        config.recordDrillBy
      ]),
    [recordTypeId, config.recordGroupBy, config.recordDrillBy]
  );
  const drill = useDrillState(config.recordGroupBy, config.recordDrillBy, resetKey);
  const currentGroupBy = drill.currentGroupBy ?? config.recordGroupBy;

  // Search filters built from the drill stack. Each step contributes one
  // eq-clause; assigneeCount drills are blocked upstream by the config
  // form so we don't need to handle them here.
  const filters = useMemo<SearchFilterClause[]>(() => {
    const out: SearchFilterClause[] = [];
    for (const step of drill.path) {
      const c = groupByToFilterClause(step.groupBy, step.label);
      if (c) out.push(c);
    }
    return out;
  }, [drill.path]);

  const searchRequest = useMemo(
    () => ({
      recordTypeId,
      filters,
      includeArchived: false,
      page: 0,
      pageSize: 200
    }),
    [recordTypeId, filters]
  );

  const recordsQuery = useRecordSearch(searchRequest, Boolean(recordTypeId));

  const data = useMemo<ChartPoint[]>(() => {
    const items = recordsQuery.data?.items ?? [];
    const fieldByKey = new Map<string, RecordTypeField>(
      (fieldsQuery.data ?? []).map((f) => [f.fieldKey, f])
    );
    return bucketize(items, (r) => labelForRecord(r, currentGroupBy, fieldByKey));
  }, [recordsQuery.data, fieldsQuery.data, currentGroupBy]);

  // Wire onClick only when the current chart can dispatch them AND the
  // drill chain has another level the click could land on. The label
  // → filter mapper rejects any axis that can't be filtered (e.g.
  // assigneeCount), which we surface by ignoring the click.
  const drillEnabled = DRILL_CAPABLE_CHARTS.has(config.chartType) && drill.canDrill;
  const onSegmentClick = useCallback(
    (label: string) => {
      const clause = groupByToFilterClause(currentGroupBy, label);
      if (!clause) return;
      const next: DrillStep = {
        groupBy: currentGroupBy,
        fieldKey: clause.fieldKey,
        value: clause.value,
        label
      };
      drill.push(next);
    },
    [currentGroupBy, drill]
  );

  if (!recordTypeId) {
    return (
      <Box p="sm">
        <Alert color="blue" variant="light">
          "All records" isn't supported yet — pick a record type in widget settings.
        </Alert>
      </Box>
    );
  }
  const groupByLabel = labelForRecordGroupBy(currentGroupBy, fieldsQuery.data ?? []);
  const breadcrumb =
    drill.path.length > 0 ? (
      <DrillBreadcrumb
        path={drill.path}
        onPop={drill.popTo}
        rootLabel={labelForRecordGroupBy(config.recordGroupBy, fieldsQuery.data ?? [])}
      />
    ) : null;
  const body = recordsQuery.isLoading
    ? <LoadingState />
    : recordsQuery.isError
      ? <ErrorState message="Failed to load records." />
      : data.length === 0
        ? <EmptyState />
        : renderChart(data, config, groupByLabel, drillEnabled ? onSegmentClick : undefined);
  return <ChartShell breadcrumb={breadcrumb}>{body}</ChartShell>;
}

function WorkflowsChart({ config }: { config: MantineChartWidgetConfig }) {
  const modelId = config.dataSource.workflowModelId?.trim() ?? "";
  const workflowsQuery = useWorkflows();
  // The model axis renders model names, but `listExecutionsPage` filters
  // by `workflowModelId`. Maintain a name → id index so a click on the
  // model axis can drill into the right id.
  const nameToModelId = useMemo(() => {
    const m = new Map<string, string>();
    (workflowsQuery.data ?? []).forEach((w) => m.set(w.name, w.id));
    return m;
  }, [workflowsQuery.data]);

  const resetKey = useMemo(
    () =>
      JSON.stringify([
        "workflows",
        modelId,
        config.workflowGroupBy,
        config.workflowDrillBy
      ]),
    [modelId, config.workflowGroupBy, config.workflowDrillBy]
  );
  const drill = useDrillState(config.workflowGroupBy, config.workflowDrillBy, resetKey);
  const currentGroupBy = (drill.currentGroupBy ?? config.workflowGroupBy) as MantineChartWidgetConfig["workflowGroupBy"];

  // Translate the drill stack into the two query params the executions
  // page already supports. Either may be set by either the static widget
  // config (modelId) or a drill step.
  const drillStatus = drill.path.find((s) => s.groupBy === "status")?.value as string | undefined;
  const drillModelId = drill.path.find((s) => s.groupBy === "model")?.value as string | undefined;
  const executionsQuery = useExecutionsPage({
    page: 0,
    pageSize: 500,
    workflowModelId: drillModelId || modelId || undefined,
    status: drillStatus
  });

  const data = useMemo<ChartPoint[]>(() => {
    const items = executionsQuery.data?.items ?? [];
    return bucketize(items, (e) => labelForExecution(e, currentGroupBy));
  }, [executionsQuery.data, currentGroupBy]);

  const drillEnabled = DRILL_CAPABLE_CHARTS.has(config.chartType) && drill.canDrill;
  const onSegmentClick = useCallback(
    (label: string) => {
      if (currentGroupBy === "status") {
        // Status labels are already canonical values; the dash is the
        // "no status" bucket which can't be expressed as a filter.
        if (label === "—") return;
        drill.push({ groupBy: "status", fieldKey: "status", value: label, label });
        return;
      }
      // model: resolve display name back to id.
      const id = nameToModelId.get(label);
      if (!id) return;
      drill.push({ groupBy: "model", fieldKey: "workflowModelId", value: id, label });
    },
    [currentGroupBy, drill, nameToModelId]
  );

  const groupByLabel = labelForWorkflowGroupBy(currentGroupBy);
  const breadcrumb =
    drill.path.length > 0 ? (
      <DrillBreadcrumb
        path={drill.path}
        onPop={drill.popTo}
        rootLabel={labelForWorkflowGroupBy(config.workflowGroupBy)}
      />
    ) : null;
  const body = executionsQuery.isLoading
    ? <LoadingState />
    : executionsQuery.isError
      ? <ErrorState message="Failed to load workflow executions." />
      : data.length === 0
        ? <EmptyState />
        : renderChart(data, config, groupByLabel, drillEnabled ? onSegmentClick : undefined);
  return <ChartShell breadcrumb={breadcrumb}>{body}</ChartShell>;
}

function SavedQueryChart({ config }: { config: MantineChartWidgetConfig }) {
  const savedQueryId = config.dataSource.savedQueryId?.trim() ?? "";
  const savedQueriesQuery = useSavedQueries();
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );

  // Execute the saved query's text directly. /api/query already enforces
  // per-entity authorization, so a user who lost access to an underlying
  // entity after the query was saved will see the executor's error.
  const queryText = savedQuery?.queryText ?? "";
  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "saved-query-chart", savedQueryId, queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(savedQueryId) && Boolean(queryText),
    staleTime: 30_000
  });

  const data = useMemo<ChartPoint[]>(() => {
    const res = resultQuery.data;
    if (!res || res.columns.length === 0) return [];
    return bucketizeAqlRows(
      res,
      config.savedQueryLabelColumn,
      config.savedQueryValueColumn
    );
  }, [resultQuery.data, config.savedQueryLabelColumn, config.savedQueryValueColumn]);

  if (!savedQueryId) {
    return (
      <Box p="sm">
        <Alert color="blue" variant="light">
          Pick a saved query in widget settings.
        </Alert>
      </Box>
    );
  }
  if (savedQueriesQuery.isLoading) return <LoadingState />;
  if (savedQueriesQuery.isError) {
    return <ErrorState message="Failed to load saved queries." />;
  }
  if (!savedQuery) {
    return (
      <Box p="sm">
        <Alert color="yellow" variant="light">
          The selected saved query is no longer available. Pick another in widget settings.
        </Alert>
      </Box>
    );
  }
  if (resultQuery.isLoading) return <LoadingState />;
  if (resultQuery.isError) {
    return <ErrorState message="Failed to execute the saved query." />;
  }
  if (data.length === 0) return <EmptyState />;

  const groupByLabel = config.savedQueryLabelColumn || (resultQuery.data?.columns[0]?.name ?? "Label");
  return renderChart(data, config, groupByLabel);
}

// Inline AQL text owned by the widget itself. Same execution + column
// mapping pipeline as SavedQueryChart, but the query lives on
// `dataSource.adHocAqlQuery` instead of being looked up by id. The
// label/value column config keys are shared with savedQuery — both
// derive their data from /api/query rows, so the mapping has the same
// shape.
function AdHocAqlChart({ config }: { config: MantineChartWidgetConfig }) {
  const queryText = config.dataSource.adHocAqlQuery?.trim() ?? "";

  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "ad-hoc-aql-chart", queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(queryText),
    staleTime: 30_000
  });

  const data = useMemo<ChartPoint[]>(() => {
    const res = resultQuery.data;
    if (!res || res.columns.length === 0) return [];
    return bucketizeAqlRows(
      res,
      config.savedQueryLabelColumn,
      config.savedQueryValueColumn
    );
  }, [resultQuery.data, config.savedQueryLabelColumn, config.savedQueryValueColumn]);

  if (!queryText) {
    return (
      <Box p="sm">
        <Alert color="blue" variant="light">
          Write an AQL query in widget settings.
        </Alert>
      </Box>
    );
  }
  if (resultQuery.isLoading) return <LoadingState />;
  if (resultQuery.isError) {
    return <ErrorState message="Failed to execute the query." />;
  }
  if (data.length === 0) return <EmptyState />;

  const groupByLabel =
    config.savedQueryLabelColumn || (resultQuery.data?.columns[0]?.name ?? "Label");
  return renderChart(data, config, groupByLabel);
}

// ---- Shared rendering helpers ----

function renderChart(
  data: ChartPoint[],
  config: MantineChartWidgetConfig,
  groupByLabel: string,
  onSegmentClick?: (label: string) => void
) {
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
      return (
        <RadialChartCard
          kind="donut"
          data={data}
          chartLabel={groupByLabel}
          onSegmentClick={onSegmentClick}
        />
      );
    case "pie":
      return (
        <RadialChartCard
          kind="pie"
          data={data}
          chartLabel={groupByLabel}
          onSegmentClick={onSegmentClick}
        />
      );
    case "radial-bar":
      // RadialBarChart is already fully responsive and has native legend
      // + tooltip support, so we just turn those on rather than wrapping.
      return (
        <RadialBarChart
          h="100%"
          dataKey="value"
          withLegend
          withTooltip
          data={data.map((d, i) => ({
            name: d.label,
            value: d.value,
            color: DONUT_COLORS[i % DONUT_COLORS.length]
          }))}
        />
      );
    case "funnel":
      // FunnelChart treats the data as ordered top-to-bottom stages. Order
      // comes from the data source: records/workflows bucketize sorts by
      // count desc; AQL preserves the query's ORDER BY, so funnel users on
      // AQL should write `ORDER BY <value> DESC` for a largest-on-top funnel.
      return (
        <FunnelChart
          h="100%"
          data={data.map((d, i) => ({
            name: d.label,
            value: d.value,
            color: DONUT_COLORS[i % DONUT_COLORS.length]
          }))}
        />
      );
    case "bars-list":
      // BarsList is a horizontal-bar leaderboard; bars are sized relative
      // to the largest entry.
      return (
        <BarsList
          data={data.map((d, i) => ({
            name: d.label,
            value: d.value,
            color: DONUT_COLORS[i % DONUT_COLORS.length]
          }))}
          getBarProps={
            onSegmentClick
              ? (entry) => ({
                  onClick: () => onSegmentClick(entry.name),
                  style: { cursor: "pointer" }
                })
              : undefined
          }
        />
      );
    case "treemap":
      // Treemap supports nested children but our buckets are flat — one
      // root level with sized rectangles. Recharts' Treemap fires onClick
      // on the inner cells; the payload includes the cell's `name`.
      return (
        <Treemap
          h="100%"
          data={data.map((d, i) => ({
            name: d.label,
            value: d.value,
            color: DONUT_COLORS[i % DONUT_COLORS.length]
          }))}
          treemapProps={
            onSegmentClick
              ? {
                  onClick: (cell: { name?: string } | null) => {
                    if (cell?.name) onSegmentClick(cell.name);
                  },
                  style: { cursor: "pointer" }
                }
              : undefined
          }
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
          barProps={
            onSegmentClick
              ? {
                  // Recharts attaches the underlying datum to `payload`
                  // on the rectangle; `BarRectangleItem`'s typing
                  // doesn't expose it but it's there at runtime. Cast
                  // narrowly and read our bucket's label.
                  onClick: ((datum: unknown) => {
                    const payload = (datum as { payload?: { label?: string } } | undefined)?.payload;
                    if (payload?.label) onSegmentClick(payload.label);
                  }) as never,
                  style: { cursor: "pointer" }
                }
              : {}
          }
        />
      );
  }
}

// Convert a Mantine theme color token (e.g. "teal.6") into a CSS value
// the browser can paint. Tokens map 1:1 to Mantine's auto-generated CSS
// variables (`--mantine-color-<name>-<shade>`). Anything that isn't a
// shade-suffixed token is returned as-is so plain hex / named colors
// still work.
function mantineTokenToCssVar(token: string): string {
  if (/^[a-z]+\.\d$/i.test(token)) {
    return `var(--mantine-color-${token.replace(".", "-")})`;
  }
  return token;
}

// Wrapper for the pie / donut chart family. Mantine gives both a fixed
// `size` default (160px) and ships no legend, so the chart looked small +
// uninformative inside a dashboard cell. This wrapper:
//   * measures the available area with useElementSize and sizes the chart
//     to the smaller of (width - legend, height) so it actually fills the
//     widget
//   * enables withTooltip so segments are interactive on hover
//   * renders a side legend with color swatch + name + value so the chart
//     is readable without hovering each slice
function RadialChartCard({
  kind,
  data,
  chartLabel,
  onSegmentClick
}: {
  kind: "pie" | "donut";
  data: ChartPoint[];
  chartLabel: string;
  onSegmentClick?: (label: string) => void;
}) {
  // Measure the chart slot directly. Measuring the outer flex container
  // and then deriving the chart slot's dimensions led to a tiny vertical
  // overshoot that produced a phantom scrollbar on the widget body, so
  // we instead let flex own the layout and read the chart slot's actual
  // box.
  const { ref: chartSlotRef, width: chartSlotW, height: chartSlotH } = useElementSize();

  const colored = useMemo(
    () =>
      data.map((d, i) => ({
        name: d.label,
        value: d.value,
        color: DONUT_COLORS[i % DONUT_COLORS.length]
      })),
    [data]
  );

  // Floor the chart at 80px (Mantine's lower bound for readability) and
  // subtract 2px of breathing room so antialiased edges don't push the
  // surrounding flex parent past its limit.
  const size =
    chartSlotW > 0 && chartSlotH > 0
      ? Math.max(80, Math.min(chartSlotW, chartSlotH) - 2)
      : 0;

  const total = useMemo(() => data.reduce((acc, d) => acc + d.value, 0), [data]);

  return (
    <Box
      style={{
        display: "flex",
        flexDirection: "row",
        flexWrap: "wrap",
        alignItems: "stretch",
        justifyContent: "center",
        gap: 8,
        height: "100%",
        width: "100%",
        overflow: "hidden"
      }}
    >
      <Box
        ref={chartSlotRef}
        style={{
          flex: "1 1 200px",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          minWidth: 0,
          minHeight: 0
        }}
      >
        {size > 0 ? (
          kind === "donut" ? (
            <DonutChart
              data={colored}
              chartLabel={chartLabel}
              size={size}
              withTooltip
              tooltipDataSource="segment"
              cellProps={
                onSegmentClick
                  ? (cell) => ({
                      onClick: () => onSegmentClick(cell.name),
                      style: { cursor: "pointer" }
                    })
                  : undefined
              }
            />
          ) : (
            <PieChart
              data={colored}
              size={size}
              withTooltip
              tooltipDataSource="segment"
              cellProps={
                onSegmentClick
                  ? (cell) => ({
                      onClick: () => onSegmentClick(cell.name),
                      style: { cursor: "pointer" }
                    })
                  : undefined
              }
            />
          )
        ) : null}
      </Box>
      <ScrollArea
        style={{
          flex: "1 1 140px",
          maxWidth: 220,
          minWidth: 120,
          minHeight: 0
        }}
        scrollbarSize={6}
      >
        <Stack gap={4} py={4} pr="xs">
          {colored.map((d) => {
            const pct = total > 0 ? Math.round((d.value / total) * 100) : 0;
            return (
              <Group key={d.name} gap="xs" wrap="nowrap" align="center">
                <ColorSwatch color={mantineTokenToCssVar(d.color)} size={10} />
                <Text size="xs" lineClamp={1} style={{ flex: "1 1 auto", minWidth: 0 }}>
                  {d.name}
                </Text>
                <Text size="xs" c="dimmed" style={{ flex: "0 0 auto" }}>
                  {d.value}
                  {total > 0 ? ` · ${pct}%` : ""}
                </Text>
              </Group>
            );
          })}
        </Stack>
      </ScrollArea>
    </Box>
  );
}

// Common chart frame that reserves a row above the chart body for the
// drill breadcrumb (when present). The body uses flex with min-height: 0
// so the chart still fills the remaining space after the header is laid
// out.
function ChartShell({ breadcrumb, children }: { breadcrumb: ReactNode; children: ReactNode }) {
  return (
    <Box
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        minHeight: 0
      }}
    >
      {breadcrumb ? (
        <Box pb={4} style={{ flex: "0 0 auto" }}>
          {breadcrumb}
        </Box>
      ) : null}
      <Box style={{ flex: "1 1 auto", minHeight: 0, minWidth: 0 }}>{children}</Box>
    </Box>
  );
}

// Breadcrumb above the chart while a drill path is active. Each prior
// segment is clickable to pop back to that level; the last segment is
// non-interactive and shows the currently-filtered value.
function DrillBreadcrumb({
  path,
  onPop,
  rootLabel
}: {
  path: DrillStep[];
  onPop: (index: number) => void;
  rootLabel: string;
}) {
  const items: ReactNode[] = [
    <Anchor key="root" component="button" type="button" size="xs" onClick={() => onPop(-1)}>
      All {rootLabel}
    </Anchor>,
    ...path.map((step, i) =>
      i === path.length - 1 ? (
        <Text key={`${i}-${step.label}`} size="xs" fw={500}>
          {step.label}
        </Text>
      ) : (
        <Anchor
          key={`${i}-${step.label}`}
          component="button"
          type="button"
          size="xs"
          onClick={() => onPop(i + 1)}
        >
          {step.label}
        </Anchor>
      )
    )
  ];
  return (
    <Breadcrumbs separator="›" separatorMargin="xs" styles={{ separator: { fontSize: 11 } }}>
      {items}
    </Breadcrumbs>
  );
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

// Reshape an AQL result set into {label, value}[]. The label column
// defaults to the first column; the value column is optional — when
// blank we count rows per unique label (matches records / workflows
// bucketize behaviour), and when set we sum the numeric coercion of each
// row's value-column cell, which lets aggregated queries (e.g. one row
// per status with an explicit COUNT) render without re-aggregating.
// Output order = first-seen row order, which preserves the AQL ORDER BY.
// Funnel / bars-list / desc-by-value charts should write `ORDER BY <value>
// DESC` in their query; this function will no longer impose a value-desc
// sort that clobbers a date-axis ORDER BY on time-series charts.
function bucketizeAqlRows(
  res: AqlQueryResponse,
  labelColumn: string,
  valueColumn: string
): ChartPoint[] {
  const labelKey =
    labelColumn && res.columns.some((c) => c.name === labelColumn)
      ? labelColumn
      : (res.columns[0]?.name ?? "");
  if (!labelKey) return [];
  const valueKey =
    valueColumn && res.columns.some((c) => c.name === valueColumn) ? valueColumn : "";

  const buckets = new Map<string, number>();
  for (const row of res.rows) {
    const labelRaw = row[labelKey];
    const label =
      labelRaw === null || labelRaw === undefined || labelRaw === "" ? "—" : String(labelRaw);
    let delta = 1;
    if (valueKey) {
      const raw = row[valueKey];
      const n = typeof raw === "number" ? raw : Number(raw);
      delta = Number.isFinite(n) ? n : 0;
    }
    buckets.set(label, (buckets.get(label) ?? 0) + delta);
  }
  return Array.from(buckets.entries()).map(([label, value]) => ({ label, value }));
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
