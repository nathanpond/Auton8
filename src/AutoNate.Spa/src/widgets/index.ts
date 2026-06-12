// Imported for side effects only — each widget's config file calls
// registerWidget() at module load. Adding a new widget = create folder +
// add one import line here.
import "./data-table/DataTableWidget.config";
import "./mantine-chart/MantineChartWidget.config";
import "./quadrant-chart/QuadrantChartWidget.config";
import "./composite-chart/CompositeChartWidget.config";

export { registerWidget, getWidget, listWidgets, mergeWidgetConfig } from "./registry";
export type { WidgetDefinition, WidgetRuntimeProps, WidgetConfigFormProps } from "./registry";
