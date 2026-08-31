import { Alert, Badge, Box, Text } from "@mantine/core";
import { DataTable, DataTableColumn } from "@/components/data-table/DataTable";
import { listRecords } from "@/api/records";
import { listExecutionsPage } from "@/api/executions";
import type { RecordModel } from "@/types/records";
import type { WorkflowExecutionSummary } from "@/types/flowable";
import type { WidgetRuntimeProps } from "@/widgets/registry";
import { RECORD_COLUMNS, WORKFLOW_COLUMNS, type DataTableWidgetConfig } from "./DataTableWidget.config";

export function DataTableWidget({
  config,
  widgetId
}: WidgetRuntimeProps<DataTableWidgetConfig>) {
  if (config.dataSource.type === "records") {
    return <RecordsTable config={config} widgetId={widgetId} />;
  }
  return <WorkflowsTable config={config} widgetId={widgetId} />;
}

// ---- Records source ----

const RECORD_COLUMN_LABELS: Record<(typeof RECORD_COLUMNS)[number], string> = {
  key: "Key",
  name: "Name",
  status: "Status",
  dueDate: "Due",
  assignees: "Assignees",
  updatedAtUtc: "Updated"
};

const RECORD_COLUMN_WIDTHS: Record<(typeof RECORD_COLUMNS)[number], string> = {
  key: "100px",
  name: "auto",
  status: "120px",
  dueDate: "120px",
  assignees: "160px",
  updatedAtUtc: "160px"
};

function RecordsTable({ config, widgetId }: { config: DataTableWidgetConfig; widgetId: string }) {
  const recordTypeId = config.dataSource.recordTypeId?.trim() ?? "";

  const columns: DataTableColumn<RecordModel>[] = config.recordColumns.map((c) => {
    switch (c) {
      case "status":
        return {
          id: "status",
          header: RECORD_COLUMN_LABELS.status,
          cell: ({ row }) =>
            row.original.status ? (
              <Badge variant="light" radius="sm">{row.original.status}</Badge>
            ) : (
              <Text c="dimmed">—</Text>
            ),
          enableSorting: false
        };
      case "assignees":
        return {
          id: "assignees",
          header: RECORD_COLUMN_LABELS.assignees,
          cell: ({ row }) => {
            const n = row.original.assigneeIds.length;
            return n > 0 ? `${n} assignee${n === 1 ? "" : "s"}` : "—";
          },
          enableSorting: false
        };
      case "dueDate":
        return {
          id: "dueDate",
          header: RECORD_COLUMN_LABELS.dueDate,
          accessorKey: "dueDate" as const,
          cell: ({ row }) => row.original.dueDate ?? "—"
        };
      case "updatedAtUtc":
        return {
          id: "updatedAtUtc",
          header: RECORD_COLUMN_LABELS.updatedAtUtc,
          accessorKey: "updatedAtUtc" as const,
          cell: ({ row }) => new Date(row.original.updatedAtUtc).toLocaleString()
        };
      case "key":
        return { id: "key", header: RECORD_COLUMN_LABELS.key, accessorKey: "key" as const };
      case "name":
      default:
        return { id: "name", header: RECORD_COLUMN_LABELS.name, accessorKey: "name" as const };
    }
  });

  const widths = config.recordColumns.map((c) => RECORD_COLUMN_WIDTHS[c]);

  if (!recordTypeId) {
    return (
      <Box p="sm">
        <Alert color="blue" variant="light">
          &quot;All records&quot; isn&apos;t supported yet — pick a record type in widget settings.
        </Alert>
      </Box>
    );
  }

  // DataTable owns the fetch via loadAll inside its own react-query, keyed
  // by the args below. Don't wrap with an outer useRecords — DataTable
  // caches the loadAll result under [...queryKey, "all"] and won't refetch
  // when the outer hook's data later resolves, so the table would stay
  // empty on first render. Fetching directly here keeps the lifecycles in
  // sync.
  return (
    <DataTable<RecordModel>
      mode="client"
      queryKey={["widget", widgetId, "records", recordTypeId, config.pageSize, config.includeArchived]}
      loadAll={async () => {
        const page = await listRecords({
          recordTypeId,
          pageSize: config.pageSize,
          includeArchived: config.includeArchived
        });
        return page.items;
      }}
      columns={columns}
      columnWidths={widths}
      rowKey={(row) => row.id}
      pageSize={config.pageSize}
      searchEnabled
      emptyMessage="No records yet."
      loadingMessage="Loading records…"
    />
  );
}

// ---- Workflows source ----

const WORKFLOW_COLUMN_LABELS: Record<(typeof WORKFLOW_COLUMNS)[number], string> = {
  name: "Run",
  model: "Model",
  status: "Status",
  currentStep: "Current step",
  startedAtUtc: "Started",
  lastActivityAtUtc: "Last activity"
};

const WORKFLOW_COLUMN_WIDTHS: Record<(typeof WORKFLOW_COLUMNS)[number], string> = {
  name: "auto",
  model: "160px",
  status: "120px",
  currentStep: "auto",
  startedAtUtc: "160px",
  lastActivityAtUtc: "160px"
};

function WorkflowsTable({ config, widgetId }: { config: DataTableWidgetConfig; widgetId: string }) {
  const modelId = config.dataSource.workflowModelId?.trim() ?? "";

  const columns: DataTableColumn<WorkflowExecutionSummary>[] = config.workflowColumns.map((c) => {
    switch (c) {
      case "status":
        return {
          id: "status",
          header: WORKFLOW_COLUMN_LABELS.status,
          cell: ({ row }) => (
            <Badge variant="light" radius="sm">{row.original.status}</Badge>
          ),
          enableSorting: false
        };
      case "model":
        return {
          id: "model",
          header: WORKFLOW_COLUMN_LABELS.model,
          accessorKey: "workflowModelName" as const,
          cell: ({ row }) => row.original.workflowModelName ?? "—"
        };
      case "currentStep":
        return {
          id: "currentStep",
          header: WORKFLOW_COLUMN_LABELS.currentStep,
          accessorKey: "currentStep" as const,
          cell: ({ row }) => row.original.currentStep ?? "—"
        };
      case "startedAtUtc":
        return {
          id: "startedAtUtc",
          header: WORKFLOW_COLUMN_LABELS.startedAtUtc,
          accessorKey: "startedAtUtc" as const,
          cell: ({ row }) =>
            row.original.startedAtUtc ? new Date(row.original.startedAtUtc).toLocaleString() : "—"
        };
      case "lastActivityAtUtc":
        return {
          id: "lastActivityAtUtc",
          header: WORKFLOW_COLUMN_LABELS.lastActivityAtUtc,
          accessorKey: "lastActivityAtUtc" as const,
          cell: ({ row }) =>
            row.original.lastActivityAtUtc
              ? new Date(row.original.lastActivityAtUtc).toLocaleString()
              : "—"
        };
      case "name":
      default:
        return {
          id: "name",
          header: WORKFLOW_COLUMN_LABELS.name,
          accessorKey: "name" as const,
          cell: ({ row }) => row.original.name ?? row.original.id
        };
    }
  });

  const widths = config.workflowColumns.map((c) => WORKFLOW_COLUMN_WIDTHS[c]);

  // Same lifecycle note as RecordsTable: let DataTable's internal query
  // own the fetch via loadAll, otherwise we'd cache an empty array on
  // mount and never re-resolve when the outer hook's data lands.
  return (
    <DataTable<WorkflowExecutionSummary>
      mode="client"
      queryKey={["widget", widgetId, "executions", modelId, config.pageSize]}
      loadAll={async () => {
        const page = await listExecutionsPage({
          page: 0,
          pageSize: Math.max(config.pageSize * 4, 100),
          workflowModelId: modelId || undefined
        });
        return page.items;
      }}
      columns={columns}
      columnWidths={widths}
      rowKey={(row) => row.id}
      pageSize={config.pageSize}
      searchEnabled
      emptyMessage="No executions yet."
      loadingMessage="Loading executions…"
    />
  );
}
