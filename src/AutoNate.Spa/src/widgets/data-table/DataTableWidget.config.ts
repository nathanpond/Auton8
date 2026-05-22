import { z } from "zod";
import { registerWidget } from "@/widgets/registry";
import { dataSourceSchema, DEFAULT_DATA_SOURCE } from "@/widgets/dataSource";
import { DataTableWidget } from "./DataTableWidget";
import { DataTableConfigForm } from "./DataTableConfigForm";

// Columns are addressed per data-source type. Each source advertises a
// fixed set of "built-in" columns; the bespoke ConfigForm shows the slice
// that matches the chosen source. Custom per-field columns (from a record
// type's fields, or workflow process variables) are a future iteration.
export const RECORD_COLUMNS = ["key", "name", "status", "dueDate", "assignees", "updatedAtUtc"] as const;
export const WORKFLOW_COLUMNS = ["name", "model", "status", "currentStep", "startedAtUtc", "lastActivityAtUtc"] as const;

export const dataTableWidgetSchema = z.object({
  dataSource: dataSourceSchema,
  recordColumns: z.array(z.enum(RECORD_COLUMNS)).default(["key", "name", "status", "updatedAtUtc"]),
  workflowColumns: z.array(z.enum(WORKFLOW_COLUMNS)).default(["name", "model", "status", "lastActivityAtUtc"]),
  pageSize: z.number().min(5).max(200).default(25),
  includeArchived: z.boolean().default(false)
});

export type DataTableWidgetConfig = z.infer<typeof dataTableWidgetSchema>;

registerWidget<DataTableWidgetConfig>({
  type: "data-table",
  category: "Tables",
  title: "Data table",
  description: "List records or workflow executions with sortable, filterable columns.",
  thumbnail: "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIj48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeT0iMCIgd2lkdGg9IjIwMCIgaGVpZ2h0PSIyNCIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjEwIiB5PSI4IiB3aWR0aD0iNDAiIGhlaWdodD0iNyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSI2MCIgeT0iOCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjciIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIuOCIvPjxyZWN0IHg9IjEyMCIgeT0iOCIgd2lkdGg9IjMwIiBoZWlnaHQ9IjciIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIuOCIvPjxyZWN0IHg9IjE2MCIgeT0iOCIgd2lkdGg9IjMwIiBoZWlnaHQ9IjciIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIuOCIvPjxyZWN0IHg9IjAiIHk9IjMyIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjI0IiBmaWxsPSIjZjhmOWZhIi8+PHJlY3QgeD0iMTAiIHk9IjQwIiB3aWR0aD0iMzAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI2MCIgeT0iNDAiIHdpZHRoPSI2MCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEzMCIgeT0iNDAiIHdpZHRoPSIyMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjE2MCIgeT0iNDAiIHdpZHRoPSIzMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjAiIHk9IjU2IiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjI0IiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeD0iMTAiIHk9IjY0IiB3aWR0aD0iMzAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI2MCIgeT0iNjQiIHdpZHRoPSI2MCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEzMCIgeT0iNjQiIHdpZHRoPSIyMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjE2MCIgeT0iNjQiIHdpZHRoPSIzMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjAiIHk9IjgwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjI0IiBmaWxsPSIjZjhmOWZhIi8+PHJlY3QgeD0iMTAiIHk9Ijg4IiB3aWR0aD0iMzAiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI2MCIgeT0iODgiIHdpZHRoPSI2MCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEzMCIgeT0iODgiIHdpZHRoPSIyMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjE2MCIgeT0iODgiIHdpZHRoPSIzMCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjAiIHk9IjEwNCIgd2lkdGg9IjIwMCIgaGVpZ2h0PSIyNCIgZmlsbD0iI2ZmZmZmZiIvPjxyZWN0IHg9IjAiIHk9IjEyOCIgd2lkdGg9IjIwMCIgaGVpZ2h0PSIyMiIgZmlsbD0iI2Y4ZjlmYSIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAiIHk9IjEzNyIgd2lkdGg9IjQwIiBoZWlnaHQ9IjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTYwIiB5PSIxMzciIHdpZHRoPSIzMCIgaGVpZ2h0PSI1IiByeD0iMSIgZmlsbD0iIzAwYWNhYyIvPjwvc3ZnPg==",
  defaultSize: { w: 8, h: 4, minW: 3, minH: 3 },
  defaultConfig: {
    dataSource: DEFAULT_DATA_SOURCE,
    recordColumns: ["key", "name", "status", "updatedAtUtc"],
    workflowColumns: ["name", "model", "status", "lastActivityAtUtc"],
    pageSize: 25,
    includeArchived: false
  },
  schema: dataTableWidgetSchema,
  Component: DataTableWidget,
  ConfigForm: DataTableConfigForm
});
