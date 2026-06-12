import { z } from "zod";
import { dataSourceSchema, DEFAULT_DATA_SOURCE } from "@/widgets/dataSource";
import { registerWidget, type WidgetDefinition } from "@/widgets/registry";
import { QuadrantChartWidget } from "./QuadrantChartWidget";
import { QuadrantChartConfigForm } from "./QuadrantChartConfigForm";

// Free-form strings (not enums) because the underlying columns/fields
// are admin-configurable and not enumerable at schema-definition time.
// Same convention used by `recordGroupBy` / `savedQueryLabelColumn` in
// MantineChartWidget.config.ts.
export const quadrantChartWidgetSchema = z.object({
  dataSource: dataSourceSchema,
  // Required: numeric column / field key for the X axis.
  xAxisColumn: z.string().default(""),
  // Required: numeric column / field key for the Y axis.
  yAxisColumn: z.string().default(""),
  // Optional 3rd numeric dimension for bubble size.
  sizeColumn: z.string().default(""),
  // Optional categorical column for per-point coloring (drives the legend).
  categoryColumn: z.string().default(""),
  // null = auto (data midpoint). Lets users pin a strategic threshold.
  xMidpoint: z.number().nullable().default(null),
  yMidpoint: z.number().nullable().default(null),
  // Quadrant corner labels, in screen order: NE, NW, SW, SE.
  quadrantLabelTopRight: z.string().default("High X / High Y"),
  quadrantLabelTopLeft: z.string().default("Low X / High Y"),
  quadrantLabelBottomLeft: z.string().default("Low X / Low Y"),
  quadrantLabelBottomRight: z.string().default("High X / Low Y"),
  xAxisLabel: z.string().default(""),
  yAxisLabel: z.string().default(""),
  // Mantine color token used when categoryColumn is empty.
  seriesColor: z.string().default("teal.6")
});

export type QuadrantChartWidgetConfig = z.infer<typeof quadrantChartWidgetSchema>;

// SVG thumbnail mirrors the visual style of the other chart picker entries
// (200x150, white background, dee2e6 border, blue title bar). Renders a
// stylised 4-quadrant scatter with crossing axes and scattered points.
const QUADRANT_THUMBNAIL =
  "data:image/svg+xml;base64," +
  btoa(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 150">
<rect width="200" height="150" fill="#ffffff" stroke="#dee2e6"/>
<rect x="10" y="14" width="60" height="6" rx="1" fill="#727cb6"/>
<line x1="14" y1="128" x2="190" y2="128" stroke="#dee2e6"/>
<line x1="20" y1="34" x2="20" y2="128" stroke="#dee2e6"/>
<line x1="20" y1="80" x2="190" y2="80" stroke="#727cb6" stroke-dasharray="3 3"/>
<line x1="105" y1="34" x2="105" y2="128" stroke="#727cb6" stroke-dasharray="3 3"/>
<circle cx="48" cy="58" r="4" fill="#00acac"/>
<circle cx="62" cy="48" r="3" fill="#00acac"/>
<circle cx="78" cy="62" r="5" fill="#00acac"/>
<circle cx="138" cy="50" r="5" fill="#348fe2"/>
<circle cx="158" cy="44" r="4" fill="#348fe2"/>
<circle cx="170" cy="62" r="6" fill="#348fe2"/>
<circle cx="48" cy="104" r="3" fill="#f59c1a"/>
<circle cx="70" cy="112" r="4" fill="#f59c1a"/>
<circle cx="86" cy="98" r="3" fill="#f59c1a"/>
<circle cx="132" cy="98" r="4" fill="#32a932"/>
<circle cx="156" cy="110" r="5" fill="#32a932"/>
<circle cx="176" cy="100" r="3" fill="#32a932"/>
</svg>`);

registerWidget<QuadrantChartWidgetConfig>({
  type: "chart-quadrant",
  category: "Charts",
  title: "Quadrant chart",
  description:
    "Plot points on X/Y axes split into four labeled quadrants. Useful for priority/effort, value/risk, and BCG-style matrices.",
  thumbnail: QUADRANT_THUMBNAIL,
  defaultSize: { w: 6, h: 5, minW: 4, minH: 4 },
  defaultConfig: {
    dataSource: DEFAULT_DATA_SOURCE,
    xAxisColumn: "",
    yAxisColumn: "",
    sizeColumn: "",
    categoryColumn: "",
    xMidpoint: null,
    yMidpoint: null,
    quadrantLabelTopRight: "High X / High Y",
    quadrantLabelTopLeft: "Low X / High Y",
    quadrantLabelBottomLeft: "Low X / Low Y",
    quadrantLabelBottomRight: "High X / Low Y",
    xAxisLabel: "",
    yAxisLabel: "",
    seriesColor: "teal.6"
  },
  schema: quadrantChartWidgetSchema,
  Component: QuadrantChartWidget,
  ConfigForm: QuadrantChartConfigForm
} satisfies WidgetDefinition<QuadrantChartWidgetConfig>);
