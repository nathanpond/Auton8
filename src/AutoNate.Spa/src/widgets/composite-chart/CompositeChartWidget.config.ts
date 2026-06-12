import { z } from "zod";
import { dataSourceSchema, DEFAULT_DATA_SOURCE } from "@/widgets/dataSource";
import { registerWidget, type WidgetDefinition } from "@/widgets/registry";
import { CompositeChartWidget } from "./CompositeChartWidget";
import { CompositeChartConfigForm } from "./CompositeChartConfigForm";

export const CompositeSeriesTypeEnum = z.enum(["line", "area", "bar"]);
export type CompositeSeriesType = z.infer<typeof CompositeSeriesTypeEnum>;

export const CompositeAggregationEnum = z.enum(["sum", "avg", "count"]);
export type CompositeAggregation = z.infer<typeof CompositeAggregationEnum>;

// One configured series. Stored as a free-form `valueColumn` string for
// the same reason the scatter widget's columns are — the underlying
// fields/AQL columns aren't enumerable at schema-definition time.
export const compositeSeriesSchema = z.object({
  name: z.string().default(""),
  type: CompositeSeriesTypeEnum.default("bar"),
  valueColumn: z.string().default(""),
  aggregation: CompositeAggregationEnum.default("sum"),
  color: z.string().default("teal.6")
});

export type CompositeSeries = z.infer<typeof compositeSeriesSchema>;

export const compositeChartWidgetSchema = z.object({
  dataSource: dataSourceSchema,
  // Categorical column / field that drives the X axis. For records this
  // is a built-in property ("status") or `field:<fieldKey>`; for AQL
  // it's the result column name.
  bucketColumn: z.string().default(""),
  // 1–4 series. Each renders as line, area, or bar inside the same
  // recharts ComposedChart so the chart can show "bar of X + line of Y"
  // on the same axes. Cap is hard at 4 — beyond that the legend + axis
  // crowding stops being useful.
  series: z.array(compositeSeriesSchema).min(1).max(4).default([
    { name: "Count", type: "bar", valueColumn: "", aggregation: "count", color: "teal.6" }
  ]),
  xAxisLabel: z.string().default(""),
  yAxisLabel: z.string().default("")
});

export type CompositeChartWidgetConfig = z.infer<typeof compositeChartWidgetSchema>;

const COMPOSITE_THUMBNAIL =
  "data:image/svg+xml;base64," +
  btoa(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 150">
<rect width="200" height="150" fill="#ffffff" stroke="#dee2e6"/>
<rect x="10" y="14" width="60" height="6" rx="1" fill="#727cb6"/>
<line x1="14" y1="128" x2="190" y2="128" stroke="#dee2e6"/>
<line x1="20" y1="34" x2="20" y2="128" stroke="#dee2e6"/>
<rect x="30" y="74" width="18" height="54" fill="#00acac"/>
<rect x="62" y="60" width="18" height="68" fill="#00acac"/>
<rect x="94" y="46" width="18" height="82" fill="#00acac"/>
<rect x="126" y="64" width="18" height="64" fill="#00acac"/>
<rect x="158" y="80" width="18" height="48" fill="#00acac"/>
<polyline points="39,52 71,38 103,58 135,46 167,70" fill="none" stroke="#348fe2" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="39" cy="52" r="3" fill="#348fe2"/>
<circle cx="71" cy="38" r="3" fill="#348fe2"/>
<circle cx="103" cy="58" r="3" fill="#348fe2"/>
<circle cx="135" cy="46" r="3" fill="#348fe2"/>
<circle cx="167" cy="70" r="3" fill="#348fe2"/>
</svg>`);

registerWidget<CompositeChartWidgetConfig>({
  type: "chart-composite",
  category: "Charts",
  title: "Composite chart",
  description:
    "Combine bar, line, and area series on the same axes. Useful for showing one metric next to another over the same buckets (e.g. count of records as bars + average score as a line).",
  thumbnail: COMPOSITE_THUMBNAIL,
  defaultSize: { w: 6, h: 5, minW: 4, minH: 4 },
  defaultConfig: {
    dataSource: DEFAULT_DATA_SOURCE,
    bucketColumn: "",
    series: [
      { name: "Count", type: "bar", valueColumn: "", aggregation: "count", color: "teal.6" }
    ],
    xAxisLabel: "",
    yAxisLabel: ""
  },
  schema: compositeChartWidgetSchema,
  Component: CompositeChartWidget,
  ConfigForm: CompositeChartConfigForm
} satisfies WidgetDefinition<CompositeChartWidgetConfig>);
