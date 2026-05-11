import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
import { Badge, Box, Button, Group, NativeSelect, Switch } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import { searchRecords } from "@/api/records";
import {
  RecordModel,
  SearchFilterClause,
  SearchRecordsRequest
} from "@/types/records";
import "./fields/renderers"; // ensure renderers register on first import
import { getRenderer } from "./fields/registry";
import RecordFilterBuilder from "./RecordFilterBuilder";
import {
  DataTable,
  DataTablePageRequest
} from "@/components/data-table/DataTable";

export default function RecordList() {
  const { typeShortCode = "" } = useParams<{ typeShortCode: string }>();
  const navigate = useNavigate();
  const code = typeShortCode.toUpperCase();

  const { data: types = [], isLoading: loadingTypes } = useRecordTypes(true);
  const type = types.find((t) => t.shortCode === code) ?? null;

  const [includeArchived, setIncludeArchived] = useState(false);
  const [sort, setSort] = useState<string>("updated_desc");
  const [filters, setFilters] = useState<SearchFilterClause[]>([]);
  const [filtersOpen, setFiltersOpen] = useState(false);

  const { data: fields = [] } = useRecordTypeFields(type?.id ?? null, false);
  const visibleFields = useMemo(
    () => fields.filter((f) => !f.isArchived).slice(0, 4),
    [fields]
  );

  const filtersActive = filters.length > 0;

  const loadPage = useMemo(() => {
    return async (req: DataTablePageRequest) => {
      if (!type?.id) return { items: [], totalCount: 0 };
      const request: SearchRecordsRequest = {
        recordTypeId: type.id,
        filters,
        includeArchived,
        page: req.page,
        pageSize: req.pageSize,
        sort
      };
      const r = await searchRecords(request);
      return { items: r.items, totalCount: r.totalCount };
    };
  }, [type?.id, filters, includeArchived, sort]);

  // Column definitions are dynamic — depend on the record type's first 4
  // configured fields. TanStack handles dynamic column lists fine; the only
  // caveat is sort state may reference a stale column id when types switch.
  // We avoid that by surfacing sort through the page-level dropdown rather
  // than DataTable's clickable-header sort.
  const columns = useMemo<ColumnDef<RecordModel>[]>(() => {
    const base: ColumnDef<RecordModel>[] = [
      {
        id: "key",
        header: "Key",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Link to={`/record/${row.original.key}`} onClick={(e) => e.stopPropagation()}>
            <code>{row.original.key}</code>
          </Link>
        )
      },
      {
        id: "name",
        accessorKey: "name",
        header: "Name",
        enableSorting: false
      },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        enableSorting: false,
        cell: ({ row }) =>
          row.original.status ?? <span className="text-body text-opacity-50">—</span>
      },
      {
        id: "dueDate",
        header: "Due Date",
        enableSorting: false,
        cell: ({ row }) =>
          row.original.dueDate ? (
            formatDate(row.original.dueDate)
          ) : (
            <span className="text-body text-opacity-50">—</span>
          )
      },
      ...visibleFields.map((f): ColumnDef<RecordModel> => ({
        id: `field-${f.fieldKey}`,
        header: f.displayName,
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => {
          const renderer = getRenderer(f.dataType);
          return renderer
            ? renderer.formatValue(f, row.original.values[f.fieldKey])
            : String(row.original.values[f.fieldKey] ?? "");
        }
      })),
      {
        id: "updatedAtUtc",
        header: "Updated",
        enableSorting: false,
        cell: ({ row }) => formatWhen(row.original.updatedAtUtc)
      },
      {
        id: "archive",
        header: "Archive",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) =>
          row.original.isArchived ? (
            <Badge color="gray" variant="filled">
              Archived
            </Badge>
          ) : (
            <Badge color="green" variant="filled">
              Active
            </Badge>
          )
      }
    ];
    return base;
  }, [visibleFields]);

  // 4 fixed columns (Key, Name, Status, Due Date) + dynamic field columns +
  // 2 trailing (Updated, Archive). Distribute width evenly across the
  // dynamic fields with fixed widths on the bookend columns.
  const columnWidths = useMemo(() => {
    const dynamicCount = visibleFields.length;
    const remaining = Math.max(8, Math.floor(48 / Math.max(1, dynamicCount)));
    return [
      "10%",
      "16%",
      "10%",
      "10%",
      ...Array(dynamicCount).fill(`${remaining}%`),
      "16%",
      "10%"
    ];
  }, [visibleFields.length]);

  if (!loadingTypes && !type) {
    return (
      <PageHeader
        title="Records"
        description={
          <>
            Unknown record type code <code>{code}</code>.{" "}
            <Link to="/record-types">Browse record types</Link>.
          </>
        }
      />
    );
  }

  return (
    <>
      <PageHeader
        title={
          <>
            <code style={{ marginRight: 8 }}>{code}</code>
            {type?.name ?? "Records"}
          </>
        }
        description={<Link to={`/record-types/${type?.id ?? ""}`}>Edit type definition</Link>}
        actions={
          <Button
            onClick={() => navigate(`/records/${code}/new`)}
            disabled={!type}
            leftSection={<i className="fa fa-plus" />}
          >
            New {type?.name ?? "Record"}
          </Button>
        }
      />

      {filtersOpen && (
        <Box
          p="md"
          mb="md"
          style={{
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-default)"
          }}
        >
          <RecordFilterBuilder
            fields={fields}
            initialFilters={filters}
            onApply={(applied) => setFilters(applied)}
            onClear={() => setFilters([])}
          />
        </Box>
      )}

      <DataTable<RecordModel>
        mode="server"
        loadPage={loadPage}
        // The full key is what react-query caches by. Including filter/sort/
        // archived in the key means tweaking any of them invalidates and
        // refetches without us touching DataTable internals.
        queryKey={["records", type?.id ?? "", { filters, sort, includeArchived }]}
        // ...and resetPaginationKey jumps the user back to page 0 when the
        // scope changes (so they don't sit on empty page 5 of a new filter).
        resetPaginationKey={`${sort}|${includeArchived}|${filters.length}`}
        columns={columns}
        rowKey={(r) => r.id}
        columnWidths={columnWidths}
        searchEnabled={false}
        emptyMessage={
          filtersActive
            ? "No records match your filters."
            : `No records yet. Create the first ${type?.name ?? "record"}.`
        }
        loadingMessage="Loading records…"
        getRowClassName={(r) => (r.isArchived ? "row-archived" : undefined)}
        onRowClick={(r) => navigate(`/record/${r.key}`)}
        getRowAriaLabel={(r) => `Open ${r.key}`}
        toolbarLeft={
          <Group gap="md" wrap="wrap" align="center">
            <Button
              size="xs"
              variant={filtersActive ? "filled" : "default"}
              onClick={() => setFiltersOpen((o) => !o)}
              leftSection={<i className="fa fa-filter" />}
              rightSection={
                filtersActive ? (
                  <Badge size="sm" color="gray" variant="light">
                    {filters.length}
                  </Badge>
                ) : undefined
              }
            >
              Filters
            </Button>
            <NativeSelect
              size="xs"
              label="Sort"
              value={sort}
              onChange={(e) => setSort(e.currentTarget.value)}
              data={[
                { value: "updated_desc", label: "Updated (newest)" },
                { value: "created_desc", label: "Created (newest)" },
                { value: "key_asc", label: "Key ascending" },
                { value: "key_desc", label: "Key descending" },
                { value: "name_asc", label: "Name A-Z" },
                { value: "name_desc", label: "Name Z-A" },
                { value: "status_asc", label: "Status A-Z" },
                { value: "status_desc", label: "Status Z-A" },
                { value: "due_date_asc", label: "Due date (earliest)" },
                { value: "due_date_desc", label: "Due date (latest)" }
              ]}
              styles={{ root: { display: "inline-flex", flexDirection: "row", gap: 8, alignItems: "center" } }}
            />
            <Switch
              id="include-archived-records"
              size="sm"
              label="Show archived"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.currentTarget.checked)}
            />
          </Group>
        }
      />
    </>
  );
}

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}
