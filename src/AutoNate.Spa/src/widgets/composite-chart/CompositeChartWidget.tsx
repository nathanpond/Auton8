import { useMemo } from "react";
import { Alert, Box, Loader, Stack, Text } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { CompositeChart } from "@mantine/charts";
import { useRecordSearch } from "@/hooks/useRecords";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import type { RecordModel, RecordTypeField } from "@/types/records";
import type { WidgetRuntimeProps } from "@/widgets/registry";
import {
  readRecordCategory,
  readRecordNumber
} from "@/widgets/quadrant-chart/QuadrantChartWidget";
import type {
  CompositeChartWidgetConfig,
  CompositeSeries
} from "./CompositeChartWidget.config";

// Bucket key used for the categorical X axis. Renamed away from any
// likely-clashing field/column name so a user-picked "label" or "value"
// column can't collide with the internal key.
const BUCKET_KEY = "__bucket__";

type CompositeRow = Record<string, string | number>;

export function CompositeChartWidget({ config }: WidgetRuntimeProps<CompositeChartWidgetConfig>) {
  if (config.dataSource.type === "records") return <RecordsComposite config={config} />;
  if (config.dataSource.type === "savedQuery") return <SavedQueryComposite config={config} />;
  if (config.dataSource.type === "adHocAql") return <AdHocAqlComposite config={config} />;
  return <WorkflowsComposite />;
}

function RecordsComposite({ config }: { config: CompositeChartWidgetConfig }) {
  const recordTypeId = config.dataSource.recordTypeId?.trim() ?? "";
  const fieldsQuery = useRecordTypeFields(recordTypeId || null);
  const recordsQuery = useRecordSearch(
    {
      recordTypeId,
      filters: [],
      includeArchived: false,
      page: 0,
      pageSize: 500
    },
    Boolean(recordTypeId)
  );

  const data = useMemo<CompositeRow[]>(() => {
    if (!config.bucketColumn || config.series.length === 0) return [];
    const items = recordsQuery.data?.items ?? [];
    const fieldByKey = new Map<string, RecordTypeField>(
      (fieldsQuery.data ?? []).map((f) => [f.fieldKey, f])
    );
    return bucketizeRecords(items, fieldByKey, config.bucketColumn, config.series);
  }, [recordsQuery.data, fieldsQuery.data, config.bucketColumn, config.series]);

  if (!recordTypeId) return <InfoState message='"All records" isn’t supported yet — pick a record type in widget settings.' />;
  if (!config.bucketColumn) return <InfoState message="Pick a bucket column in widget settings." />;
  if (recordsQuery.isLoading) return <LoadingState />;
  if (recordsQuery.isError) return <ErrorState message="Failed to load records." />;
  if (data.length === 0) return <EmptyState />;
  return <Canvas config={config} data={data} />;
}

function WorkflowsComposite() {
  return (
    <Box p="sm">
      <Alert color="yellow" variant="light" title="Composite chart needs aggregated data">
        Workflow executions don’t expose numeric fields directly. Use the <strong>Saved Query</strong> or
        <strong> Ad-hoc AQL</strong> source with a query that aggregates per bucket (e.g. avg duration by
        model) and map its columns to series here.
      </Alert>
    </Box>
  );
}

function SavedQueryComposite({ config }: { config: CompositeChartWidgetConfig }) {
  const savedQueryId = config.dataSource.savedQueryId?.trim() ?? "";
  const savedQueriesQuery = useSavedQueries();
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );
  const queryText = savedQuery?.queryText ?? "";
  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "saved-query-composite", savedQueryId, queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(savedQueryId) && Boolean(queryText),
    staleTime: 30_000
  });
  return <AqlCanvas config={config} result={resultQuery.data} isLoading={savedQueriesQuery.isLoading || resultQuery.isLoading} isError={savedQueriesQuery.isError || resultQuery.isError} missing={!savedQueryId} missingMessage="Pick a saved query in widget settings." />;
}

function AdHocAqlComposite({ config }: { config: CompositeChartWidgetConfig }) {
  const queryText = config.dataSource.adHocAqlQuery?.trim() ?? "";
  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "ad-hoc-aql-composite", queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(queryText),
    staleTime: 30_000
  });
  return <AqlCanvas config={config} result={resultQuery.data} isLoading={resultQuery.isLoading} isError={resultQuery.isError} missing={!queryText} missingMessage="Write an AQL query in widget settings." />;
}

function AqlCanvas({
  config,
  result,
  isLoading,
  isError,
  missing,
  missingMessage
}: {
  config: CompositeChartWidgetConfig;
  result: AqlQueryResponse | undefined;
  isLoading: boolean;
  isError: boolean;
  missing: boolean;
  missingMessage: string;
}) {
  const data = useMemo<CompositeRow[]>(() => {
    if (!config.bucketColumn || !result) return [];
    return aqlRowsToCompositeRows(result, config.bucketColumn, config.series);
  }, [result, config.bucketColumn, config.series]);

  if (missing) return <InfoState message={missingMessage} />;
  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message="Failed to execute the query." />;
  if (!config.bucketColumn) return <InfoState message="Pick a bucket column in widget settings." />;
  if (data.length === 0) return <EmptyState />;
  return <Canvas config={config} data={data} />;
}

// ---- Render ----

function Canvas({ config, data }: { config: CompositeChartWidgetConfig; data: CompositeRow[] }) {
  // Mantine `series` expects `{ name, color, type }`. The `name` MUST
  // match a key in each data row — that's how recharts looks up the
  // value. We use the user-typed series.name when present, falling back
  // to the value column (or an index sentinel) to keep keys unique.
  const series = useMemo(
    () =>
      config.series.map((s, i) => ({
        name: seriesKey(s, i),
        color: s.color,
        type: s.type,
        label: s.name || s.valueColumn || `Series ${i + 1}`
      })),
    [config.series]
  );
  return (
    <Box style={{ height: "100%", width: "100%", minHeight: 0, minWidth: 0 }}>
      <CompositeChart
        h="100%"
        data={data}
        dataKey={BUCKET_KEY}
        series={series}
        xAxisLabel={config.xAxisLabel || undefined}
        yAxisLabel={config.yAxisLabel || undefined}
        withLegend
        legendProps={{ verticalAlign: "bottom" }}
        withTooltip
        tooltipAnimationDuration={120}
      />
    </Box>
  );
}

// ---- Aggregation helpers ----

function seriesKey(s: CompositeSeries, index: number): string {
  // Unique key per series for `data[row][key]` lookups. Fall back to an
  // index suffix so duplicate user-typed names don't silently collapse
  // into one series.
  const base = s.name || s.valueColumn || `series_${index}`;
  return `${base}__${index}`;
}

function bucketizeRecords(
  records: RecordModel[],
  fieldByKey: Map<string, RecordTypeField>,
  bucketColumn: string,
  series: CompositeSeries[]
): CompositeRow[] {
  type Acc = { sums: number[]; counts: number[] };
  const buckets = new Map<string, Acc>();
  for (const r of records) {
    const key = readRecordCategory(r, bucketColumn);
    let acc = buckets.get(key);
    if (!acc) {
      acc = { sums: series.map(() => 0), counts: series.map(() => 0) };
      buckets.set(key, acc);
    }
    series.forEach((s, i) => {
      if (s.aggregation === "count") {
        acc!.counts[i] += 1;
        return;
      }
      const v = readRecordNumber(r, s.valueColumn, fieldByKey);
      if (v !== null) {
        acc!.sums[i] += v;
        acc!.counts[i] += 1;
      }
    });
  }
  const out: CompositeRow[] = [];
  for (const [label, acc] of buckets.entries()) {
    const row: CompositeRow = { [BUCKET_KEY]: label };
    series.forEach((s, i) => {
      const key = seriesKey(s, i);
      let v = 0;
      if (s.aggregation === "sum") v = acc.sums[i];
      else if (s.aggregation === "avg") v = acc.counts[i] > 0 ? acc.sums[i] / acc.counts[i] : 0;
      else if (s.aggregation === "count") v = acc.counts[i];
      row[key] = v;
    });
    out.push(row);
  }
  // Sort: numeric-looking keys ascending, otherwise by first-series desc
  // so the biggest bar sits on the left — matches the bar chart widget.
  const firstKey = seriesKey(series[0], 0);
  out.sort((a, b) => {
    const av = a[firstKey];
    const bv = b[firstKey];
    return typeof av === "number" && typeof bv === "number" ? bv - av : 0;
  });
  return out;
}

function aqlRowsToCompositeRows(
  res: AqlQueryResponse,
  bucketColumn: string,
  series: CompositeSeries[]
): CompositeRow[] {
  const cols = new Set(res.columns.map((c) => c.name));
  if (!cols.has(bucketColumn)) return [];
  // For AQL we trust the user's query to have pre-aggregated. Each row
  // contributes a bucket. If the user wrote a query that returns
  // multiple rows per bucket the chart will show only the last one —
  // they need to GROUP BY in their query.
  const out: CompositeRow[] = [];
  for (const row of res.rows) {
    const rawBucket = row[bucketColumn];
    const bucketLabel =
      rawBucket === null || rawBucket === undefined || rawBucket === "" ? "—" : String(rawBucket);
    const compRow: CompositeRow = { [BUCKET_KEY]: bucketLabel };
    series.forEach((s, i) => {
      const key = seriesKey(s, i);
      if (s.aggregation === "count" || !s.valueColumn) {
        // For AQL we can't infer a per-bucket count without
        // re-aggregating across rows; show 1 per row so a bar series
        // with COUNT() in SQL is reflected literally.
        compRow[key] = 1;
        return;
      }
      const raw = row[s.valueColumn];
      const n = typeof raw === "number" ? raw : Number(raw);
      compRow[key] = Number.isFinite(n) ? n : 0;
    });
    out.push(compRow);
  }
  return out;
}

// ---- Status states ----

function LoadingState() {
  return (
    <Stack align="center" justify="center" h="100%">
      <Loader size="sm" />
      <Text c="dimmed" size="sm">Loading…</Text>
    </Stack>
  );
}

function ErrorState({ message }: { message: string }) {
  return <Box p="sm"><Alert color="red" variant="light">{message}</Alert></Box>;
}

function EmptyState() {
  return (
    <Stack align="center" justify="center" h="100%">
      <Text c="dimmed" size="sm">No data to chart.</Text>
    </Stack>
  );
}

function InfoState({ message }: { message: string }) {
  return <Box p="sm"><Alert color="blue" variant="light">{message}</Alert></Box>;
}
