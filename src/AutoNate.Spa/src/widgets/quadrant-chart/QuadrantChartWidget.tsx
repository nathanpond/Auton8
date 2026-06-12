import { useCallback, useMemo, useRef, useState, type ReactNode } from "react";
import { Alert, Box, Button, ColorSwatch, Group, Loader, Paper, Stack, Text } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { ScatterChart } from "@mantine/charts";
import { useNavigate } from "react-router-dom";
import { useRecordSearch } from "@/hooks/useRecords";
import { useRecordTypeFields } from "@/hooks/useRecordTypes";
import { useExecutionsPage } from "@/hooks/useExecutions";
import { useSavedQueries } from "@/hooks/useSavedQueries";
import { executeQuery, type AqlQueryResponse } from "@/api/aql";
import type { RecordModel, RecordTypeField } from "@/types/records";
import type { WorkflowExecutionSummary } from "@/types/flowable";
import type { WidgetRuntimeProps } from "@/widgets/registry";
import type { QuadrantChartWidgetConfig } from "./QuadrantChartWidget.config";

// Built-in numeric column key on RecordModel. Custom numeric record-type
// fields are prefixed with this sentinel so the runtime can distinguish
// `keyNumber` from a custom field named "keyNumber".
export const RECORD_BUILTIN_KEY_NUMBER = "keyNumber";
export const RECORD_CUSTOM_FIELD_PREFIX = "field:";
// Categorical (non-numeric) record properties — same vocabulary the
// existing chart's recordGroupBy uses. Reused here for the optional
// "category" coloring dim. `dueDate` is included because plotting points
// coloured by due date bucket is reasonable; `assigneeCount` is omitted
// because it isn't a column the user typically thinks of as a category.
export const RECORD_BUILTIN_CATEGORY_OPTIONS = [
  { value: "status", label: "Status" },
  { value: "name", label: "Name" },
  { value: "key", label: "Key" },
  { value: "dueDate", label: "Due date" }
] as const;

type QuadrantPoint = {
  x: number;
  y: number;
  size?: number;
  // Per-point identity shown as the tooltip header. Independent of
  // `category` (which drives colour); the user can set both, either, or
  // neither.
  label?: string;
  category?: string;
  recordKey?: string;
};

// Per-series color cycle when categoryColumn is set. The first 8 entries
// match DONUT_COLORS in MantineChartWidget.tsx so a Records-by-status
// quadrant and a Records-by-status bar chart on the same dashboard share
// the same colour for "Open", "Closed", etc. The remaining 6 are picked
// to be visually distinct from the first 8 — they only get used when a
// category column has more than 8 unique values.
const CATEGORY_COLOR_CYCLE = [
  "teal.6",
  "blue.5",
  "orange.5",
  "green.6",
  "pink.5",
  "violet.5",
  "cyan.6",
  "yellow.6",
  "red.6",
  "grape.5",
  "indigo.6",
  "lime.6",
  "orange.8",
  "gray.7"
];

// Maximum number of categorical series we'll render before collapsing to
// a single uniform series. Matches the colour cycle length above so every
// series gets a distinct colour. Without this cap a high-cardinality
// category column (e.g. one row per unique title) would spawn a
// series-per-row, blow up the legend to dozens of chips, and squash the
// plot into a tiny strip. The category value still rides on each point's
// payload so the per-point tooltip still shows it on hover.
const MAX_CATEGORY_SERIES = CATEGORY_COLOR_CYCLE.length;

export function QuadrantChartWidget({ config }: WidgetRuntimeProps<QuadrantChartWidgetConfig>) {
  if (config.dataSource.type === "records") {
    return <RecordsQuadrant config={config} />;
  }
  if (config.dataSource.type === "savedQuery") {
    return <SavedQueryQuadrant config={config} />;
  }
  if (config.dataSource.type === "adHocAql") {
    return <AdHocAqlQuadrant config={config} />;
  }
  return <WorkflowsQuadrant config={config} />;
}

function RecordsQuadrant({ config }: { config: QuadrantChartWidgetConfig }) {
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

  const points = useMemo<QuadrantPoint[]>(() => {
    if (!config.xAxisColumn || !config.yAxisColumn) return [];
    const items = recordsQuery.data?.items ?? [];
    const fieldByKey = new Map<string, RecordTypeField>(
      (fieldsQuery.data ?? []).map((f) => [f.fieldKey, f])
    );
    const out: QuadrantPoint[] = [];
    for (const r of items) {
      const x = readRecordNumber(r, config.xAxisColumn, fieldByKey);
      const y = readRecordNumber(r, config.yAxisColumn, fieldByKey);
      if (x === null || y === null) continue;
      const size = config.sizeColumn ? readRecordNumber(r, config.sizeColumn, fieldByKey) ?? undefined : undefined;
      const category = config.categoryColumn ? readRecordCategory(r, config.categoryColumn) : undefined;
      const label = config.labelColumn ? readRecordCategory(r, config.labelColumn) : undefined;
      out.push({ x, y, size, label, category, recordKey: r.key });
    }
    return out;
  }, [recordsQuery.data, fieldsQuery.data, config.xAxisColumn, config.yAxisColumn, config.sizeColumn, config.labelColumn, config.categoryColumn]);

  if (!recordTypeId) return <InfoState message='"All records" isn’t supported yet — pick a record type in widget settings.' />;
  if (!config.xAxisColumn || !config.yAxisColumn) return <InfoState message="Pick an X and Y column in widget settings." />;
  if (recordsQuery.isLoading) return <LoadingState />;
  if (recordsQuery.isError) return <ErrorState message="Failed to load records." />;
  if (points.length === 0) return <EmptyState />;
  return <QuadrantChartCanvas config={config} points={points} />;
}

// Workflows have no numeric properties on executions and only version
// numbers on the model; a quadrant on raw workflow rows would always be
// empty or degenerate. Render an inline notice pointing users at the
// AQL path, which IS expressive enough to drive a useful chart.
function WorkflowsQuadrant({ config: _config }: { config: QuadrantChartWidgetConfig }) {
  // Touch the executions query so an admin investigating "is this widget
  // loading anything" sees the same hook in the network tab they'd see
  // for other workflow-sourced widgets. Cheap and short-circuits via
  // staleTime in useExecutionsPage; result is ignored.
  useExecutionsPage({ page: 0, pageSize: 1 });
  return (
    <Box p="sm">
      <Alert color="yellow" variant="light" title="Quadrant chart needs numeric data">
        Workflow executions don’t expose numeric fields directly. Use the <strong>Saved Query</strong> or
        <strong> Ad-hoc AQL</strong> source with a query that aggregates per workflow (e.g. average duration,
        success rate) and plot those.
      </Alert>
    </Box>
  );
}

function SavedQueryQuadrant({ config }: { config: QuadrantChartWidgetConfig }) {
  const savedQueryId = config.dataSource.savedQueryId?.trim() ?? "";
  const savedQueriesQuery = useSavedQueries();
  const savedQuery = useMemo(
    () => (savedQueriesQuery.data ?? []).find((q) => q.id === savedQueryId) ?? null,
    [savedQueriesQuery.data, savedQueryId]
  );
  const queryText = savedQuery?.queryText ?? "";
  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "saved-query-quadrant", savedQueryId, queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(savedQueryId) && Boolean(queryText),
    staleTime: 30_000
  });

  const points = useMemo<QuadrantPoint[]>(() => {
    if (!config.xAxisColumn || !config.yAxisColumn) return [];
    return aqlRowsToPoints(resultQuery.data, config);
  }, [resultQuery.data, config.xAxisColumn, config.yAxisColumn, config.sizeColumn, config.labelColumn, config.categoryColumn]);

  if (!savedQueryId) return <InfoState message="Pick a saved query in widget settings." />;
  if (savedQueriesQuery.isLoading || resultQuery.isLoading) return <LoadingState />;
  if (savedQueriesQuery.isError) return <ErrorState message="Failed to load saved queries." />;
  if (!savedQuery) return <InfoState message="The selected saved query is no longer available. Pick another in widget settings." />;
  if (resultQuery.isError) return <ErrorState message="Failed to execute the saved query." />;
  if (!config.xAxisColumn || !config.yAxisColumn) return <InfoState message="Pick an X and Y column in widget settings." />;
  if (points.length === 0) return <EmptyState />;
  return <QuadrantChartCanvas config={config} points={points} />;
}

function AdHocAqlQuadrant({ config }: { config: QuadrantChartWidgetConfig }) {
  const queryText = config.dataSource.adHocAqlQuery?.trim() ?? "";
  const resultQuery = useQuery<AqlQueryResponse>({
    queryKey: ["widget", "ad-hoc-aql-quadrant", queryText],
    queryFn: ({ signal }) => executeQuery(queryText, signal),
    enabled: Boolean(queryText),
    staleTime: 30_000
  });

  const points = useMemo<QuadrantPoint[]>(() => {
    if (!config.xAxisColumn || !config.yAxisColumn) return [];
    return aqlRowsToPoints(resultQuery.data, config);
  }, [resultQuery.data, config.xAxisColumn, config.yAxisColumn, config.sizeColumn, config.labelColumn, config.categoryColumn]);

  if (!queryText) return <InfoState message="Write an AQL query in widget settings." />;
  if (resultQuery.isLoading) return <LoadingState />;
  if (resultQuery.isError) return <ErrorState message="Failed to execute the query." />;
  if (!config.xAxisColumn || !config.yAxisColumn) return <InfoState message="Pick an X and Y column in widget settings." />;
  if (points.length === 0) return <EmptyState />;
  return <QuadrantChartCanvas config={config} points={points} />;
}

// ---- Chart canvas ----

// Mantine ScatterChart expects ScatterChartSeries[] (where `data` is typed
// `Record<string, number>[]`). recharts itself preserves non-numeric extra
// keys on the payload at runtime — we use that to carry `recordKey` and
// `category` through to click handlers / legend — so we type-cast at the
// boundary rather than throwing the extras away.
type SeriesShape = { color: string; name: string; data: Record<string, number>[] };

type ZoomDomain = { xMin: number; xMax: number; yMin: number; yMax: number };
type PixelRect = { left: number; top: number; right: number; bottom: number };

function QuadrantChartCanvas({
  config,
  points
}: {
  config: QuadrantChartWidgetConfig;
  points: QuadrantPoint[];
}) {
  const navigate = useNavigate();

  // Refs + state for the shift+drag-to-zoom interaction.
  // `wrapperRef` lets us query the recharts plot rectangle from the DOM so
  // pixel→data coordinate conversion uses the chart's real bounds rather
  // than guessed margins. `zoom` is the persisted axis domain after the
  // user releases; `dragRect` is the live preview rectangle during drag.
  const wrapperRef = useRef<HTMLDivElement>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);
  const [zoom, setZoom] = useState<ZoomDomain | null>(null);
  const [dragRect, setDragRect] = useState<PixelRect | null>(null);
  // Hovering a legend chip dims every series except the matching one,
  // restoring the Mantine built-in legend's mouseover behaviour now that
  // we render the legend ourselves outside the chart container.
  const [hoveredSeries, setHoveredSeries] = useState<string | null>(null);

  const { xMid, yMid } = useMemo(() => {
    const xs = points.map((p) => p.x);
    const ys = points.map((p) => p.y);
    return {
      xMid: config.xMidpoint ?? median(xs),
      yMid: config.yMidpoint ?? median(ys)
    };
  }, [points, config.xMidpoint, config.yMidpoint]);

  // Full data extents — used as the upstream domain when not zoomed AND as
  // the conversion basis when zooming further from an already-zoomed view.
  const fullDomain = useMemo<ZoomDomain>(() => {
    if (points.length === 0) return { xMin: 0, xMax: 1, yMin: 0, yMax: 1 };
    const xs = points.map((p) => p.x);
    const ys = points.map((p) => p.y);
    return {
      xMin: Math.min(...xs),
      xMax: Math.max(...xs),
      yMin: Math.min(...ys),
      yMax: Math.max(...ys)
    };
  }, [points]);
  const activeDomain = zoom ?? fullDomain;

  const series = useMemo<SeriesShape[]>(() => {
    const fallbackName = config.xAxisLabel && config.yAxisLabel
      ? `${config.yAxisLabel} vs ${config.xAxisLabel}`
      : "Points";
    if (!config.categoryColumn) {
      return [
        {
          color: config.seriesColor,
          name: fallbackName,
          data: points as unknown as Record<string, number>[]
        }
      ];
    }
    const buckets = new Map<string, QuadrantPoint[]>();
    for (const p of points) {
      const k = p.category ?? "—";
      const arr = buckets.get(k) ?? [];
      arr.push(p);
      buckets.set(k, arr);
    }
    // High-cardinality guard: when the category column has more unique
    // values than the colour cycle can express (e.g. a per-row identifier
    // like job title), collapsing to a single series keeps the legend
    // compact and the plot area full-height. The tooltip still surfaces
    // the per-point category on hover.
    if (buckets.size > MAX_CATEGORY_SERIES) {
      return [
        {
          color: config.seriesColor,
          name: fallbackName,
          data: points as unknown as Record<string, number>[]
        }
      ];
    }
    // Stable ordering by bucket size desc — biggest category renders first
    // and gets the lead colour, matching the bucketize sort in the bar
    // chart on the same dashboard.
    return Array.from(buckets.entries())
      .sort((a, b) => b[1].length - a[1].length)
      .map(([name, data], i) => ({
        color: CATEGORY_COLOR_CYCLE[i % CATEGORY_COLOR_CYCLE.length],
        name,
        data: data as unknown as Record<string, number>[]
      }));
  }, [points, config.categoryColumn, config.seriesColor, config.xAxisLabel, config.yAxisLabel]);

  // recharts payload comes from the per-series data we passed in — extra
  // keys (recordKey, size) survive the round trip.
  type ScatterClickPayload = { payload?: QuadrantPoint };
  const handlePointClick = (datum: ScatterClickPayload) => {
    const key = datum?.payload?.recordKey;
    if (key) navigate(`/record/${key}`);
  };

  // Bubble sizing: when sizeColumn is set, scale the dot radius from 4 to
  // 14 across the data range. Mantine doesn't expose a sizing prop on
  // ScatterChart, so we render a custom shape on the Scatter underneath.
  const sizeBounds = useMemo(() => {
    if (!config.sizeColumn) return null;
    const sizes = points.map((p) => p.size).filter((s): s is number => typeof s === "number" && Number.isFinite(s));
    if (sizes.length === 0) return null;
    return { min: Math.min(...sizes), max: Math.max(...sizes) };
  }, [points, config.sizeColumn]);

  // Locate the recharts plot rectangle inside our wrapper. Used to
  // (a) anchor the live drag overlay's coordinates and (b) translate
  // pixel positions into data values on release. recharts renders the
  // grid as a `<g>` (no inner `<rect>`), so we use the group element's
  // own bounding box.
  const getPlotRect = useCallback(() => {
    const grid = wrapperRef.current?.querySelector(".recharts-cartesian-grid");
    return grid?.getBoundingClientRect() ?? null;
  }, []);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (!e.shiftKey) return;
      const plot = getPlotRect();
      if (!plot) return;
      // Ignore mousedowns that started outside the plot area (e.g. on the
      // axis labels or the quadrant-label rows).
      if (
        e.clientX < plot.left ||
        e.clientX > plot.right ||
        e.clientY < plot.top ||
        e.clientY > plot.bottom
      ) return;
      e.preventDefault();
      dragStartRef.current = { x: e.clientX, y: e.clientY };
      setDragRect({ left: e.clientX, top: e.clientY, right: e.clientX, bottom: e.clientY });
    },
    [getPlotRect]
  );

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    const start = dragStartRef.current;
    if (!start) return;
    setDragRect({
      left: Math.min(start.x, e.clientX),
      top: Math.min(start.y, e.clientY),
      right: Math.max(start.x, e.clientX),
      bottom: Math.max(start.y, e.clientY)
    });
  }, []);

  const finishDrag = useCallback((e: React.MouseEvent) => {
    const start = dragStartRef.current;
    dragStartRef.current = null;
    setDragRect(null);
    if (!start) return;
    // Compute the final rect from start (ref) + end (event) instead of
    // reading dragRect from React state — state may not have flushed yet
    // when mouseup arrives in the same React batch as a fast mousemove,
    // and a stale closure value would silently skip the zoom.
    const left = Math.min(start.x, e.clientX);
    const right = Math.max(start.x, e.clientX);
    const top = Math.min(start.y, e.clientY);
    const bottom = Math.max(start.y, e.clientY);
    // Treat sub-10px drags as accidental and ignore — preserves the
    // shift+click pass-through (no zoom, no navigation either since the
    // scatter onClick still fires its normal handler when the user just
    // clicks a point).
    if (right - left < 10 || bottom - top < 10) return;
    const plot = getPlotRect();
    if (!plot) return;
    // Clip the drag rect to the plot's visible bounds so dragging past
    // an edge still zooms to the boundary.
    const l = Math.max(left, plot.left);
    const r = Math.min(right, plot.right);
    const t = Math.max(top, plot.top);
    const b = Math.min(bottom, plot.bottom);
    const xRange = activeDomain.xMax - activeDomain.xMin;
    const yRange = activeDomain.yMax - activeDomain.yMin;
    if (xRange <= 0 || yRange <= 0) return;
    // Y is inverted in screen coordinates (top=high data Y).
    setZoom({
      xMin: activeDomain.xMin + ((l - plot.left) / plot.width) * xRange,
      xMax: activeDomain.xMin + ((r - plot.left) / plot.width) * xRange,
      yMin: activeDomain.yMax - ((b - plot.top) / plot.height) * yRange,
      yMax: activeDomain.yMax - ((t - plot.top) / plot.height) * yRange
    });
  }, [activeDomain, getPlotRect]);

  // Live preview rectangle in container-local coordinates (recomputed on
  // each render from clientX/Y stored in dragRect).
  const overlayPos = useMemo(() => {
    if (!dragRect || !wrapperRef.current) return null;
    const wrap = wrapperRef.current.getBoundingClientRect();
    return {
      left: dragRect.left - wrap.left,
      top: dragRect.top - wrap.top,
      width: dragRect.right - dragRect.left,
      height: dragRect.bottom - dragRect.top
    };
  }, [dragRect]);

  // Apply zoom by handing recharts an explicit domain. `allowDataOverflow`
  // is required so points outside the zoom window are clipped instead of
  // re-fitting the axis. `tickFormatter` rounds the otherwise-arbitrary
  // float bounds (the drag rectangle never lands on round numbers, so
  // raw ticks would read like "44.798045977") down to readable labels.
  // When not zoomed we leave xAxisProps/yAxisProps undefined so Mantine's
  // auto-fit and default formatting stay in effect.
  const xAxisProps = zoom
    ? ({
        domain: [zoom.xMin, zoom.xMax],
        allowDataOverflow: true,
        type: "number",
        tickFormatter: formatAxisTick
      } as const)
    : undefined;
  const yAxisProps = zoom
    ? ({
        domain: [zoom.yMin, zoom.yMax],
        allowDataOverflow: true,
        type: "number",
        tickFormatter: formatAxisTick
      } as const)
    : undefined;

  return (
    <Box
      style={{
        height: "100%",
        width: "100%",
        minHeight: 0,
        minWidth: 0,
        display: "flex",
        flexDirection: "column"
      }}
    >
      {config.showQuadrantOverlay ? (
        <QuadrantLabelRow
          left={config.quadrantLabelTopLeft}
          right={config.quadrantLabelTopRight}
          hasYAxisLabel={!!config.yAxisLabel}
        />
      ) : null}
      <Box
        ref={wrapperRef}
        style={{ flex: "1 1 auto", minHeight: 0, position: "relative" }}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={finishDrag}
        onMouseLeave={finishDrag}
      >
        <ScatterChart
          h="100%"
          data={series}
          dataKey={{ x: "x", y: "y" }}
          xAxisLabel={config.xAxisLabel || undefined}
          yAxisLabel={config.yAxisLabel || undefined}
          labels={{
            x: config.xAxisLabel || "X",
            y: config.yAxisLabel || "Y"
          }}
          xAxisProps={xAxisProps}
          yAxisProps={yAxisProps}
          referenceLines={
            config.showQuadrantOverlay
              ? [
                  { x: xMid, color: "gray.5", strokeDasharray: "4 4" },
                  { y: yMid, color: "gray.5", strokeDasharray: "4 4" }
                ]
              : undefined
          }
          withLegend={false}
          withTooltip={!dragRect}
          tooltipProps={{
            cursor: { strokeDasharray: "3 3" },
            content: ((props: { active?: boolean; payload?: ReadonlyArray<{ payload?: QuadrantPoint }> }) =>
              renderPointTooltip(props, config)) as never
          }}
          scatterProps={{
            onClick: handlePointClick as never,
            style: { cursor: "pointer" },
            // Always render via a custom shape so we can apply
            // (a) per-point sizing when `sizeColumn` is set and
            // (b) per-series dimming when the user is hovering a legend
            // chip. Without this we'd need to swap series fills in state
            // on every hover, which would re-key the chart and lose the
            // animation. The fallback radius (4) matches Mantine's
            // default scatter dot, so charts without sizing or hover
            // look identical to the built-in render.
            shape: ((shapeProps: { cx?: number; cy?: number; fill?: string; payload?: QuadrantPoint }) => {
              const sz = shapeProps.payload?.size;
              const r =
                sizeBounds && typeof sz === "number"
                  ? scaleRadius(sz, sizeBounds.min, sizeBounds.max)
                  : 4;
              const cat = shapeProps.payload?.category;
              const dimmed = hoveredSeries !== null && hoveredSeries !== cat;
              return (
                <circle
                  cx={shapeProps.cx ?? 0}
                  cy={shapeProps.cy ?? 0}
                  r={r}
                  fill={shapeProps.fill ?? "currentColor"}
                  fillOpacity={dimmed ? 0.15 : 0.85}
                />
              );
            }) as never
          }}
        />
        {overlayPos ? (
          <div
            style={{
              position: "absolute",
              left: overlayPos.left,
              top: overlayPos.top,
              width: overlayPos.width,
              height: overlayPos.height,
              border: "1.5px dashed var(--mantine-color-blue-6)",
              backgroundColor: "var(--mantine-color-blue-1)",
              opacity: 0.35,
              pointerEvents: "none"
            }}
          />
        ) : null}
        {zoom ? (
          <Button
            variant="default"
            size="compact-xs"
            radius="sm"
            onClick={() => setZoom(null)}
            style={{ position: "absolute", top: 6, right: 6 }}
            aria-label="Reset zoom"
          >
            Reset Zoom
          </Button>
        ) : null}
      </Box>
      {config.showQuadrantOverlay ? (
        <QuadrantLabelRow
          left={config.quadrantLabelBottomLeft}
          right={config.quadrantLabelBottomRight}
          hasYAxisLabel={!!config.yAxisLabel}
        />
      ) : null}
      {series.length > 1 ? (
        <QuadrantLegend
          series={series}
          hoveredSeries={hoveredSeries}
          onHover={setHoveredSeries}
        />
      ) : null}
    </Box>
  );
}

// One row of quadrant labels (top OR bottom). Renders OUTSIDE the chart's
// plot area so labels can't collide with data points, axis ticks, the
// legend, or the tooltip. Left padding matches the chart's Y-axis tick
// column (~60px) plus extra (~20px) when the Y axis label is shown, so
// the left label visually sits inside its quadrant horizontally.
function QuadrantLabelRow({
  left,
  right,
  hasYAxisLabel
}: {
  left: string;
  right: string;
  hasYAxisLabel: boolean;
}) {
  const leftPad = hasYAxisLabel ? 84 : 64;
  return (
    <Box
      style={{
        flex: "0 0 auto",
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        gap: 12,
        paddingLeft: leftPad,
        paddingRight: 16,
        paddingTop: 2,
        paddingBottom: 2,
        pointerEvents: "none"
      }}
    >
      <Text size="xs" c="dimmed" fw={500} style={{ maxWidth: "45%", textAlign: "left" }}>
        {left}
      </Text>
      <Text size="xs" c="dimmed" fw={500} style={{ maxWidth: "45%", textAlign: "right" }}>
        {right}
      </Text>
    </Box>
  );
}

// Categorical legend rendered as its own flex row below the bottom
// corner-label row. Mantine's built-in ScatterChart legend lives INSIDE
// the chart container — when the category cardinality is high enough to
// wrap onto multiple rows, the legend overflows the chart's allocated
// height and visually collides with the bottom corner labels we already
// render outside the plot. Rendering it here gives us a dedicated row
// that the chart can't push into.
function QuadrantLegend({
  series,
  hoveredSeries,
  onHover
}: {
  series: SeriesShape[];
  hoveredSeries: string | null;
  onHover: (name: string | null) => void;
}) {
  return (
    <Box
      style={{
        flex: "0 0 auto",
        display: "flex",
        flexWrap: "wrap",
        gap: "4px 12px",
        justifyContent: "center",
        paddingTop: 4,
        paddingBottom: 2,
        paddingLeft: 16,
        paddingRight: 16
      }}
    >
      {series.map((s) => {
        const dimmed = hoveredSeries !== null && hoveredSeries !== s.name;
        return (
          <Group
            key={s.name}
            gap={6}
            wrap="nowrap"
            align="center"
            onMouseEnter={() => onHover(s.name)}
            onMouseLeave={() => onHover(null)}
            style={{ cursor: "default", opacity: dimmed ? 0.4 : 1, transition: "opacity 80ms ease" }}
          >
            <ColorSwatch color={mantineTokenToCssVar(s.color)} size={10} withShadow={false} />
            <Text size="xs" c="dimmed">{s.name}</Text>
          </Group>
        );
      })}
    </Box>
  );
}

// Convert a Mantine theme color token (e.g. "teal.6") into a CSS value
// the browser can paint. Tokens map 1:1 to Mantine's auto-generated CSS
// variables (`--mantine-color-<name>-<shade>`).
function mantineTokenToCssVar(token: string): string {
  if (/^[a-z]+\.\d$/i.test(token)) {
    return `var(--mantine-color-${token.replace(".", "-")})`;
  }
  return token;
}

// ---- Helpers ----

// Custom tooltip body. Replaces Mantine's default ChartTooltip so the
// per-point label + category are always available on hover, even when
// the high-cardinality guard collapsed many categories into a single
// series (where the default tooltip would only show the generic series
// name). Header prefers `label` (per-point identity); category renders
// as a secondary line below.
function renderPointTooltip(
  props: { active?: boolean; payload?: ReadonlyArray<{ payload?: QuadrantPoint }> },
  config: QuadrantChartWidgetConfig
): ReactNode {
  if (!props.active || !props.payload?.length) return null;
  const p = props.payload[0]?.payload;
  if (!p) return null;
  const xLabel = config.xAxisLabel || "X";
  const yLabel = config.yAxisLabel || "Y";
  const header = p.label ?? p.category ?? null;
  // Only show the category as a sub-line when it isn't already serving
  // as the header (i.e. label was set and is distinct from category).
  const subCategory = p.label && p.category && p.category !== p.label ? p.category : null;
  return (
    <Paper p="xs" shadow="md" withBorder radius="sm" style={{ pointerEvents: "none" }}>
      {header ? (
        <Text size="sm" fw={600} mb={subCategory ? 2 : 4}>
          {header}
        </Text>
      ) : null}
      {subCategory ? (
        <Text size="xs" c="dimmed" mb={4}>
          {subCategory}
        </Text>
      ) : null}
      <Stack gap={2}>
        <Text size="xs" c="dimmed">
          {xLabel}: {formatTooltipNumber(p.x)}
        </Text>
        <Text size="xs" c="dimmed">
          {yLabel}: {formatTooltipNumber(p.y)}
        </Text>
        {config.sizeColumn && typeof p.size === "number" ? (
          <Text size="xs" c="dimmed">
            Size: {formatTooltipNumber(p.size)}
          </Text>
        ) : null}
      </Stack>
    </Paper>
  );
}

// Axis-tick formatter used while zoomed. Drag-derived domain bounds are
// never round, so raw ticks would read like "44.798045977". Round to a
// scale that matches the magnitude: thousands-separators above 1k,
// integers in the 10-1000 range, one decimal at single-digit scale,
// three decimals below 1.
function formatAxisTick(value: number): string {
  if (!Number.isFinite(value)) return String(value);
  const abs = Math.abs(value);
  if (abs >= 1000) return Math.round(value).toLocaleString();
  if (abs >= 10) return Math.round(value).toString();
  if (abs >= 1) return value.toFixed(1);
  return value.toFixed(3);
}

function formatTooltipNumber(n: number): string {
  if (!Number.isFinite(n)) return String(n);
  // Use a thousands-separator for readable salaries / counts; preserve
  // any meaningful fractional part with up to 2 digits.
  const fractional = Math.abs(n) < 1 ? 4 : 2;
  return n.toLocaleString(undefined, { maximumFractionDigits: fractional });
}

function scaleRadius(value: number, min: number, max: number): number {
  const MIN_R = 4;
  const MAX_R = 14;
  if (max <= min) return (MIN_R + MAX_R) / 2;
  const t = (value - min) / (max - min);
  return MIN_R + t * (MAX_R - MIN_R);
}

function median(nums: number[]): number {
  if (nums.length === 0) return 0;
  const sorted = [...nums].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}

// Read a numeric field off a record. Returns null when the source is
// non-numeric or empty, which the caller treats as "skip this point" so
// missing values don't drag points to (0, 0).
function readRecordNumber(
  r: RecordModel,
  columnKey: string,
  fieldByKey: Map<string, RecordTypeField>
): number | null {
  if (columnKey === RECORD_BUILTIN_KEY_NUMBER) return r.keyNumber ?? null;
  if (columnKey.startsWith(RECORD_CUSTOM_FIELD_PREFIX)) {
    const fieldKey = columnKey.slice(RECORD_CUSTOM_FIELD_PREFIX.length);
    const raw = r.values?.[fieldKey];
    const n = coerceNumber(raw);
    if (n === null) return null;
    // Guard: only allow the cell through if the field is actually typed
    // numeric. A free-text "123" string would otherwise sneak in.
    const field = fieldByKey.get(fieldKey);
    if (field && field.dataType !== "number") return null;
    return n;
  }
  return null;
}

function readRecordCategory(r: RecordModel, columnKey: string): string {
  switch (columnKey) {
    case "status":
      return r.status ?? "—";
    case "name":
      return r.name || "—";
    case "key":
      return r.key || "—";
    case "dueDate":
      return r.dueDate ?? "No due date";
    default:
      if (columnKey.startsWith(RECORD_CUSTOM_FIELD_PREFIX)) {
        const fieldKey = columnKey.slice(RECORD_CUSTOM_FIELD_PREFIX.length);
        const raw = r.values?.[fieldKey];
        if (raw === null || raw === undefined || raw === "") return "—";
        if (Array.isArray(raw)) return raw.length === 0 ? "—" : raw.map(String).sort().join(", ");
        if (typeof raw === "boolean") return raw ? "Yes" : "No";
        return String(raw);
      }
      return "—";
  }
}

function coerceNumber(raw: unknown): number | null {
  if (raw === null || raw === undefined || raw === "") return null;
  if (typeof raw === "number") return Number.isFinite(raw) ? raw : null;
  if (typeof raw === "boolean") return raw ? 1 : 0;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

// Translate /api/query rows into QuadrantPoints. Numeric columns are
// coerced via coerceNumber; the recordKey-style click navigation only
// fires when the user has explicitly mapped a column named "recordKey"
// (or one that returns a value at that key). For arbitrary AQL we don't
// guess.
function aqlRowsToPoints(
  res: AqlQueryResponse | undefined,
  config: QuadrantChartWidgetConfig
): QuadrantPoint[] {
  if (!res) return [];
  const cols = new Set(res.columns.map((c) => c.name));
  const xKey = cols.has(config.xAxisColumn) ? config.xAxisColumn : null;
  const yKey = cols.has(config.yAxisColumn) ? config.yAxisColumn : null;
  if (!xKey || !yKey) return [];
  const sizeKey = config.sizeColumn && cols.has(config.sizeColumn) ? config.sizeColumn : null;
  const catKey = config.categoryColumn && cols.has(config.categoryColumn) ? config.categoryColumn : null;
  const labelKey = config.labelColumn && cols.has(config.labelColumn) ? config.labelColumn : null;

  const stringOrDash = (raw: unknown): string =>
    raw === null || raw === undefined || raw === "" ? "—" : String(raw);

  const out: QuadrantPoint[] = [];
  for (const row of res.rows) {
    const x = coerceNumber(row[xKey]);
    const y = coerceNumber(row[yKey]);
    if (x === null || y === null) continue;
    const size = sizeKey ? coerceNumber(row[sizeKey]) ?? undefined : undefined;
    const category = catKey ? stringOrDash(row[catKey]) : undefined;
    const label = labelKey ? stringOrDash(row[labelKey]) : undefined;
    // Convention: a row column literally named "recordKey" enables click
    // navigation. Optional, opt-in.
    const recordKeyRaw = row["recordKey"];
    const recordKey =
      typeof recordKeyRaw === "string" && recordKeyRaw.length > 0 ? recordKeyRaw : undefined;
    out.push({ x, y, size, label, category, recordKey });
  }
  return out;
}

// ---- Status states (mirror MantineChartWidget) ----

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

function InfoState({ message }: { message: string }) {
  return (
    <Box p="sm">
      <Alert color="blue" variant="light">{message}</Alert>
    </Box>
  );
}
