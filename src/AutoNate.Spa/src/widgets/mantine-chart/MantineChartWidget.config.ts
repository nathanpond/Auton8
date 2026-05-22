import { z } from "zod";
import { attachMeta } from "@/widgets/AutoConfigForm";
import { dataSourceSchema, DEFAULT_DATA_SOURCE } from "@/widgets/dataSource";
import { registerWidget } from "@/widgets/registry";
import { MantineChartWidget } from "./MantineChartWidget";
import { MantineChartConfigForm } from "./MantineChartConfigForm";

export const MantineChartTypeEnum = z.enum(["line", "area", "bar", "donut"]);

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

registerWidget<MantineChartWidgetConfig>({
  type: "mantine-chart",
  category: "Charts",
  title: "Mantine chart",
  description: "Bar, line, area, or donut chart of grouped records or workflow executions.",
  thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjEwIiB3aWR0aD0iNjAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48bGluZSB4MT0iMTAiIHkxPSIxMjAiIHgyPSIxOTAiIHkyPSIxMjAiIHN0cm9rZT0iI2RlZTJlNiIvPjxsaW5lIHgxPSIxMCIgeTE9IjkwIiB4Mj0iMTkwIiB5Mj0iOTAiIHN0cm9rZT0iI2VlZWVlZSIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PGxpbmUgeDE9IjEwIiB5MT0iNjAiIHgyPSIxOTAiIHkyPSI2MCIgc3Ryb2tlPSIjZWVlZWVlIiBzdHJva2UtZGFzaGFycmF5PSIyIDIiLz48cmVjdCB4PSIyMCIgeT0iNzgiIHdpZHRoPSIyMCIgaGVpZ2h0PSI0MiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjUwIiB5PSI2NSIgd2lkdGg9IjIwIiBoZWlnaHQ9IjU1IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iODAiIHk9IjUwIiB3aWR0aD0iMjAiIGhlaWdodD0iNzAiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIxMTAiIHk9Ijg1IiB3aWR0aD0iMjAiIGhlaWdodD0iMzUiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNDAiIHk9IjcwIiB3aWR0aD0iMjAiIGhlaWdodD0iNTAiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIxNzAiIHk9IjkwIiB3aWR0aD0iMjAiIGhlaWdodD0iMzAiIGZpbGw9IiM3MjdjYjYiLz48L3N2Zz4=",
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
  ConfigForm: MantineChartConfigForm
});
