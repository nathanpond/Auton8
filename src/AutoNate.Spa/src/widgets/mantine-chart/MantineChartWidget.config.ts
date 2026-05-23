import { z } from "zod";
import { attachMeta } from "@/widgets/AutoConfigForm";
import { dataSourceSchema, DEFAULT_DATA_SOURCE } from "@/widgets/dataSource";
import { registerWidget, type WidgetDefinition } from "@/widgets/registry";
import { MantineChartWidget } from "./MantineChartWidget";
import { MantineChartConfigForm } from "./MantineChartConfigForm";

export const MantineChartTypeEnum = z.enum([
  "line",
  "area",
  "bar",
  "donut",
  "pie",
  "radial-bar",
  "funnel",
  "bars-list",
  "treemap"
]);
export type MantineChartType = z.infer<typeof MantineChartTypeEnum>;

// Records source group-by uses a free-form string keyed against either a
// built-in record field (literal: "status", "name", "dueDate", "key",
// "assigneeCount") or a custom record-type field with the prefix
// `field:<fieldKey>` (see ./groupBy.ts for the sentinels). Kept as
// z.string() (not z.enum) so widgets continue to validate when admins add
// or remove fields on the underlying record type.
export const workflowGroupByEnum = z.enum(["status", "model"]);

export const mantineChartWidgetSchema = z.object({
  chartType: attachMeta(MantineChartTypeEnum.default("bar"), { label: "Chart type" }),
  dataSource: dataSourceSchema,
  recordGroupBy: z.string().default("status"),
  workflowGroupBy: workflowGroupByEnum.default("status"),
  seriesLabel: attachMeta(z.string().default("Count"), { label: "Series label" }),
  seriesColor: attachMeta(z.string().default("teal.6"), {
    label: "Series color",
    description: "Mantine color token (e.g. 'teal.6', 'blue.5')."
  })
});

export type MantineChartWidgetConfig = z.infer<typeof mantineChartWidgetSchema>;

// Per-chart picker entries. Each entry pre-bakes `chartType` into the
// default config so the user picks the chart they want from the picker
// (and the ConfigForm doesn't need a redundant chart-type dropdown). All
// entries share the same widget_type-stored config + runtime component —
// the registry key + thumbnail + description are the only differences.
//
// TODO — chart types still to implement. None of these fit the existing
// "bucket count → { name, value }[]" pipeline; each needs its own
// data-shape design + ConfigForm work before it can be registered:
//   * Sparkline       — `number[]` over time; needs a "bucket by date" group-by
//   * CompositeChart  — multi-series with mixed types (bar + line + area)
//   * RadarChart      — N numeric record fields per row, not 1 group-by
//   * ScatterChart    — { x, y } pairs from two numeric fields
//   * BubbleChart     — { x, y, z } triples from three numeric fields
//   * Heatmap         — date-indexed counts; calendar grid
//   * SankeyChart     — `nodes[] + links[{ source, target, value }]`;
//                       needs a "from field → to field" pair picker, e.g.
//                       to show volume flow between record statuses or
//                       pipeline stages. Not in @mantine/charts itself —
//                       would wrap Recharts' <Sankey> directly.
type ChartPickerEntry = {
  type: string;
  title: string;
  description: string;
  thumbnail: string;
  chartType: MantineChartType;
  defaultColor: string;
};

const CHART_ENTRIES: ChartPickerEntry[] = [
  {
    type: "chart-bar",
    title: "Bar chart",
    description: "Compare counts across categories with vertical bars. Good for buckets like status or owner.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48bGluZSB4MT0iMTQiIHkxPSIxMjgiIHgyPSIxOTAiIHkyPSIxMjgiIHN0cm9rZT0iI2RlZTJlNiIvPjxsaW5lIHgxPSIxNCIgeTE9Ijk4IiB4Mj0iMTkwIiB5Mj0iOTgiIHN0cm9rZT0iI2VlZWVlZSIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PGxpbmUgeDE9IjE0IiB5MT0iNjgiIHgyPSIxOTAiIHkyPSI2OCIgc3Ryb2tlPSIjZWVlZWVlIiBzdHJva2UtZGFzaGFycmF5PSIyIDIiLz48bGluZSB4MT0iMTQiIHkxPSIzOCIgeDI9IjE5MCIgeTI9IjM4IiBzdHJva2U9IiNlZWVlZWUiIHN0cm9rZS1kYXNoYXJyYXk9IjIgMiIvPjxyZWN0IHg9IjIyIiB5PSI3OCIgd2lkdGg9IjE2IiBoZWlnaHQ9IjUwIiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iNDYiIHk9IjU4IiB3aWR0aD0iMTYiIGhlaWdodD0iNzAiIGZpbGw9IiMwMGFjYWMiLz48cmVjdCB4PSI3MCIgeT0iNDQiIHdpZHRoPSIxNiIgaGVpZ2h0PSI4NCIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9Ijk0IiB5PSI4NiIgd2lkdGg9IjE2IiBoZWlnaHQ9IjQyIiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMTE4IiB5PSI2MiIgd2lkdGg9IjE2IiBoZWlnaHQ9IjY2IiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMTQyIiB5PSI3MCIgd2lkdGg9IjE2IiBoZWlnaHQ9IjU4IiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMTY2IiB5PSI1MCIgd2lkdGg9IjE2IiBoZWlnaHQ9Ijc4IiBmaWxsPSIjMDBhY2FjIi8+PC9zdmc+",
    chartType: "bar",
    defaultColor: "teal.6"
  },
  {
    type: "chart-line",
    title: "Line chart",
    description: "Plot a single series as a continuous line. Best when categories have natural ordering.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48bGluZSB4MT0iMTQiIHkxPSIxMjgiIHgyPSIxOTAiIHkyPSIxMjgiIHN0cm9rZT0iI2RlZTJlNiIvPjxsaW5lIHgxPSIxNCIgeTE9Ijk4IiB4Mj0iMTkwIiB5Mj0iOTgiIHN0cm9rZT0iI2VlZWVlZSIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PGxpbmUgeDE9IjE0IiB5MT0iNjgiIHgyPSIxOTAiIHkyPSI2OCIgc3Ryb2tlPSIjZWVlZWVlIiBzdHJva2UtZGFzaGFycmF5PSIyIDIiLz48bGluZSB4MT0iMTQiIHkxPSIzOCIgeDI9IjE5MCIgeTI9IjM4IiBzdHJva2U9IiNlZWVlZWUiIHN0cm9rZS1kYXNoYXJyYXk9IjIgMiIvPjxwb2x5bGluZSBwb2ludHM9IjIwLDk4IDUwLDcyIDgwLDg0IDExMCw0NiAxNDAsNjIgMTcwLDM4IDE4OCw1MiIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjMzQ4ZmUyIiBzdHJva2Utd2lkdGg9IjIuNSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PGNpcmNsZSBjeD0iMjAiIGN5PSI5OCIgcj0iMyIgZmlsbD0iIzM0OGZlMiIvPjxjaXJjbGUgY3g9IjUwIiBjeT0iNzIiIHI9IjMiIGZpbGw9IiMzNDhmZTIiLz48Y2lyY2xlIGN4PSI4MCIgY3k9Ijg0IiByPSIzIiBmaWxsPSIjMzQ4ZmUyIi8+PGNpcmNsZSBjeD0iMTEwIiBjeT0iNDYiIHI9IjMiIGZpbGw9IiMzNDhmZTIiLz48Y2lyY2xlIGN4PSIxNDAiIGN5PSI2MiIgcj0iMyIgZmlsbD0iIzM0OGZlMiIvPjxjaXJjbGUgY3g9IjE3MCIgY3k9IjM4IiByPSIzIiBmaWxsPSIjMzQ4ZmUyIi8+PGNpcmNsZSBjeD0iMTg4IiBjeT0iNTIiIHI9IjMiIGZpbGw9IiMzNDhmZTIiLz48L3N2Zz4=",
    chartType: "line",
    defaultColor: "blue.5"
  },
  {
    type: "chart-area",
    title: "Area chart",
    description: "Same shape as a line chart with the region under the line filled. Emphasizes magnitude.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48bGluZSB4MT0iMTQiIHkxPSIxMjgiIHgyPSIxOTAiIHkyPSIxMjgiIHN0cm9rZT0iI2RlZTJlNiIvPjxsaW5lIHgxPSIxNCIgeTE9Ijk4IiB4Mj0iMTkwIiB5Mj0iOTgiIHN0cm9rZT0iI2VlZWVlZSIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PGxpbmUgeDE9IjE0IiB5MT0iNjgiIHgyPSIxOTAiIHkyPSI2OCIgc3Ryb2tlPSIjZWVlZWVlIiBzdHJva2UtZGFzaGFycmF5PSIyIDIiLz48bGluZSB4MT0iMTQiIHkxPSIzOCIgeDI9IjE5MCIgeTI9IjM4IiBzdHJva2U9IiNlZWVlZWUiIHN0cm9rZS1kYXNoYXJyYXk9IjIgMiIvPjxwYXRoIGQ9Ik0gMjAgOTggTCA1MCA3MiBMIDgwIDg0IEwgMTEwIDQ2IEwgMTQwIDYyIEwgMTcwIDM4IEwgMTg4IDUyIEwgMTg4IDEyOCBMIDIwIDEyOCBaIiBmaWxsPSIjZjU5YzFhIiBmaWxsLW9wYWNpdHk9IjAuMzUiIHN0cm9rZT0iI2Y1OWMxYSIgc3Ryb2tlLXdpZHRoPSIyIi8+PC9zdmc+",
    chartType: "area",
    defaultColor: "orange.5"
  },
  {
    type: "chart-donut",
    title: "Donut chart",
    description: "Show each category's share of the whole as a ring slice. Best with a small number of buckets.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48Y2lyY2xlIGN4PSIxMDAiIGN5PSI4MCIgcj0iNDAiIGZpbGw9IiMwMGFjYWMiLz48cGF0aCBkPSJNIDEwMCA4MCBMIDEwMCA0MCBBIDQwIDQwIDAgMCAxIDEzOCA5NiBaIiBmaWxsPSIjMzQ4ZmUyIi8+PHBhdGggZD0iTSAxMDAgODAgTCAxMzggOTYgQSA0MCA0MCAwIDAgMSA4MiAxMTcgWiIgZmlsbD0iI2Y1OWMxYSIvPjxwYXRoIGQ9Ik0gMTAwIDgwIEwgODIgMTE3IEEgNDAgNDAgMCAwIDEgNjQgNjQgWiIgZmlsbD0iIzMyYTkzMiIvPjxjaXJjbGUgY3g9IjEwMCIgY3k9IjgwIiByPSIxOCIgZmlsbD0iI2ZmZmZmZiIvPjwvc3ZnPg==",
    chartType: "donut",
    defaultColor: "teal.6"
  },
  {
    type: "chart-pie",
    title: "Pie chart",
    description: "Solid pie slices showing each category's share of the whole. Like donut but without the ring hole.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48Y2lyY2xlIGN4PSIxMDAiIGN5PSI4MCIgcj0iNDQiIGZpbGw9IiMwMGFjYWMiLz48cGF0aCBkPSJNIDEwMCA4MCBMIDEwMCAzNiBBIDQ0IDQ0IDAgMCAxIDE0MiA5NiBaIiBmaWxsPSIjMzQ4ZmUyIi8+PHBhdGggZD0iTSAxMDAgODAgTCAxNDIgOTYgQSA0NCA0NCAwIDAgMSA3OCAxMTkgWiIgZmlsbD0iI2Y1OWMxYSIvPjxwYXRoIGQ9Ik0gMTAwIDgwIEwgNzggMTE5IEEgNDQgNDQgMCAwIDEgNTggNjAgWiIgZmlsbD0iIzMyYTkzMiIvPjwvc3ZnPg==",
    chartType: "pie",
    defaultColor: "teal.6"
  },
  {
    type: "chart-radial-bar",
    title: "Radial bar chart",
    description: "Concentric arcs, one per bucket. Eye-catching for a handful of categories.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48Y2lyY2xlIGN4PSIxMDAiIGN5PSI4MCIgcj0iNDgiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2U5ZWNlZiIgc3Ryb2tlLXdpZHRoPSI4Ii8+PGNpcmNsZSBjeD0iMTAwIiBjeT0iODAiIHI9IjM4IiBmaWxsPSJub25lIiBzdHJva2U9IiNlOWVjZWYiIHN0cm9rZS13aWR0aD0iOCIvPjxjaXJjbGUgY3g9IjEwMCIgY3k9IjgwIiByPSIyOCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZTllY2VmIiBzdHJva2Utd2lkdGg9IjgiLz48cGF0aCBkPSJNIDEwMCAzMiBBIDQ4IDQ4IDAgMCAxIDE0OCA4MCIgc3Ryb2tlPSIjMDBhY2FjIiBzdHJva2Utd2lkdGg9IjgiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPjxwYXRoIGQ9Ik0gMTAwIDQyIEEgMzggMzggMCAwIDEgMTIyIDExMyIgc3Ryb2tlPSIjMzQ4ZmUyIiBzdHJva2Utd2lkdGg9IjgiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPjxwYXRoIGQ9Ik0gMTAwIDUyIEEgMjggMjggMCAwIDEgMTAwIDEwOCIgc3Ryb2tlPSIjZjU5YzFhIiBzdHJva2Utd2lkdGg9IjgiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPjwvc3ZnPg==",
    chartType: "radial-bar",
    defaultColor: "teal.6"
  },
  {
    type: "chart-funnel",
    title: "Funnel chart",
    description: "Ordered stages narrowing top to bottom. Common for sales pipelines or conversion stages.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48cG9seWdvbiBwb2ludHM9IjIwLDM0IDE4MCwzNCAxNjAsNTggNDAsNTgiIGZpbGw9IiMwMGFjYWMiLz48cG9seWdvbiBwb2ludHM9IjQwLDYyIDE2MCw2MiAxNDQsODYgNTYsODYiIGZpbGw9IiMzNDhmZTIiLz48cG9seWdvbiBwb2ludHM9IjU2LDkwIDE0NCw5MCAxMzAsMTE0IDcwLDExNCIgZmlsbD0iI2Y1OWMxYSIvPjxwb2x5Z29uIHBvaW50cz0iNzAsMTE4IDEzMCwxMTggMTE2LDEzOCA4NCwxMzgiIGZpbGw9IiMzMmE5MzIiLz48L3N2Zz4=",
    chartType: "funnel",
    defaultColor: "teal.6"
  },
  {
    type: "chart-bars-list",
    title: "Bars list",
    description: "Horizontal bars sized by value relative to the biggest entry. Reads like a top-N leaderboard.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxNCIgeT0iMzQiIHdpZHRoPSIxNzIiIGhlaWdodD0iMTQiIHJ4PSIzIiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMTQiIHk9IjU0IiB3aWR0aD0iMTM4IiBoZWlnaHQ9IjE0IiByeD0iMyIgZmlsbD0iIzM0OGZlMiIvPjxyZWN0IHg9IjE0IiB5PSI3NCIgd2lkdGg9IjEwNCIgaGVpZ2h0PSIxNCIgcng9IjMiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIxNCIgeT0iOTQiIHdpZHRoPSI3NCIgaGVpZ2h0PSIxNCIgcng9IjMiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNCIgeT0iMTE0IiB3aWR0aD0iNDYiIGhlaWdodD0iMTQiIHJ4PSIzIiBmaWxsPSIjZmI1NTk3Ii8+PC9zdmc+",
    chartType: "bars-list",
    defaultColor: "teal.6"
  },
  {
    type: "chart-treemap",
    title: "Treemap",
    description: "Nested rectangles sized by value. Good when you have many categories and want everything visible at once.",
    thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjE0IiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxNCIgeT0iMzQiIHdpZHRoPSI5OCIgaGVpZ2h0PSI2MiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjExNCIgeT0iMzQiIHdpZHRoPSI3MiIgaGVpZ2h0PSIzNiIgZmlsbD0iIzM0OGZlMiIvPjxyZWN0IHg9IjExNCIgeT0iNzIiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyNCIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjE1NiIgeT0iNzIiIHdpZHRoPSIzMCIgaGVpZ2h0PSIyNCIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjE0IiB5PSI5OCIgd2lkdGg9IjYyIiBoZWlnaHQ9IjQwIiBmaWxsPSIjZmI1NTk3Ii8+PHJlY3QgeD0iNzgiIHk9Ijk4IiB3aWR0aD0iNDgiIGhlaWdodD0iNDAiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxMjgiIHk9Ijk4IiB3aWR0aD0iNTgiIGhlaWdodD0iNDAiIGZpbGw9IiM0OWI2ZDYiLz48L3N2Zz4=",
    chartType: "treemap",
    defaultColor: "teal.6"
  }
];

function makeChartDefinition(entry: ChartPickerEntry): WidgetDefinition<MantineChartWidgetConfig> {
  return {
    type: entry.type,
    category: "Charts",
    title: entry.title,
    description: entry.description,
    thumbnail: entry.thumbnail,
    defaultSize: { w: 6, h: 4, minW: 3, minH: 3 },
    defaultConfig: {
      chartType: entry.chartType,
      dataSource: DEFAULT_DATA_SOURCE,
      recordGroupBy: "status",
      workflowGroupBy: "status",
      seriesLabel: "Count",
      seriesColor: entry.defaultColor
    },
    schema: mantineChartWidgetSchema,
    Component: MantineChartWidget,
    ConfigForm: MantineChartConfigForm
  };
}

for (const entry of CHART_ENTRIES) {
  registerWidget<MantineChartWidgetConfig>(makeChartDefinition(entry));
}

// Back-compat: the picker used to ship a single combined "Mantine chart"
// entry with an in-form chart-type select. Existing dashboards may still
// have widgets persisted under widget_type='mantine-chart' — keep the
// definition registered so they render, but hide it from the picker so
// new widgets go through the per-type entries above.
registerWidget<MantineChartWidgetConfig>({
  type: "mantine-chart",
  category: "Charts",
  title: "Chart (legacy)",
  description: "Legacy combined chart entry. Use Bar / Line / Area / Donut instead.",
  thumbnail: "",
  defaultSize: { w: 6, h: 4, minW: 3, minH: 3 },
  defaultConfig: {
    chartType: "bar",
    dataSource: DEFAULT_DATA_SOURCE,
    recordGroupBy: "status",
    workflowGroupBy: "status",
    seriesLabel: "Count",
    seriesColor: "teal.6"
  },
  schema: mantineChartWidgetSchema,
  Component: MantineChartWidget,
  ConfigForm: MantineChartConfigForm,
  hiddenFromPicker: true
});
