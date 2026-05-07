import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ColumnDef } from "@tanstack/react-table";
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
            <span className="badge bg-secondary">Archived</span>
          ) : (
            <span className="badge bg-success">Active</span>
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
      <div className="page-head">
        <h1 className="page-header mb-1">Records</h1>
        <p className="page-head-copy">
          Unknown record type code <code>{code}</code>.{" "}
          <Link to="/record-types">Browse record types</Link>.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="page-head d-flex justify-content-between align-items-start">
        <div>
          <h1 className="page-header mb-1">
            <code className="me-2">{code}</code>
            {type?.name ?? "Records"}
          </h1>
          <p className="page-head-copy mb-0">
            <Link to={`/record-types/${type?.id ?? ""}`}>Edit type definition</Link>
          </p>
        </div>
        <div>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => navigate(`/records/${code}/new`)}
            disabled={!type}
          >
            <i className="fa fa-plus me-2"></i>New {type?.name ?? "Record"}
          </button>
        </div>
      </div>

      {filtersOpen && (
        <div className="border rounded p-3 mb-3">
          <RecordFilterBuilder
            fields={fields}
            initialFilters={filters}
            onApply={(applied) => setFilters(applied)}
            onClear={() => setFilters([])}
          />
        </div>
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
          <div className="d-flex align-items-center gap-3 flex-wrap">
            <button
              type="button"
              className={`btn btn-sm ${filtersActive ? "btn-primary" : "btn-outline-secondary"}`}
              onClick={() => setFiltersOpen((o) => !o)}
            >
              <i className="fa fa-filter me-2"></i>
              Filters
              {filtersActive && (
                <span className="badge bg-light text-dark ms-2">{filters.length}</span>
              )}
            </button>
            <label className="d-flex align-items-center gap-2 mb-0 small">
              Sort:
              <select
                className="form-select form-select-sm"
                value={sort}
                onChange={(e) => setSort(e.target.value)}
              >
                <option value="updated_desc">Updated (newest)</option>
                <option value="created_desc">Created (newest)</option>
                <option value="key_asc">Key ascending</option>
                <option value="key_desc">Key descending</option>
                <option value="name_asc">Name A-Z</option>
                <option value="name_desc">Name Z-A</option>
                <option value="status_asc">Status A-Z</option>
                <option value="status_desc">Status Z-A</option>
                <option value="due_date_asc">Due date (earliest)</option>
                <option value="due_date_desc">Due date (latest)</option>
              </select>
            </label>
            <div className="form-check form-switch mb-0">
              <input
                type="checkbox"
                className="form-check-input"
                id="include-archived-records"
                checked={includeArchived}
                onChange={(e) => setIncludeArchived(e.target.checked)}
              />
              <label className="form-check-label small" htmlFor="include-archived-records">
                Show archived
              </label>
            </div>
          </div>
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
