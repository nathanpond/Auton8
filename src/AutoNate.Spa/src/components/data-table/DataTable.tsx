import { ReactNode, useEffect, useMemo, useState } from "react";
import {
  ColumnDef,
  PaginationState,
  SortingState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable
} from "@tanstack/react-table";
import { useQuery } from "@tanstack/react-query";

export type DataTableMode = "client" | "server" | "auto";

export type DataTablePageRequest = {
  page: number;
  pageSize: number;
  search: string;
  sort: { id: string; desc: boolean } | null;
  filter: string | null;
};

export type DataTablePageResult<T> = {
  items: T[];
  totalCount: number;
};

export type DataTableFilterOption<T> = {
  id: string;
  label: string;
  predicate?: (row: T) => boolean;
};

type DataTableProps<T> = {
  mode?: DataTableMode;
  autoThreshold?: number;
  loadAll?: () => Promise<T[]>;
  loadPage?: (req: DataTablePageRequest) => Promise<DataTablePageResult<T>>;
  queryKey: ReadonlyArray<unknown>;

  columns: ColumnDef<T>[];
  rowKey: (row: T) => string;
  columnWidths: string[];

  pageSize?: number;
  pageSizeOptions?: number[];
  initialSort?: SortingState;

  searchEnabled?: boolean;
  searchPlaceholder?: string;

  filters?: DataTableFilterOption<T>[];
  allFilterLabel?: string;

  // Custom left-toolbar slot for callers that need richer filter chrome than
  // the single-pick `filters` tabs (e.g., RecordList's filter builder, the
  // SystemIssues triple-faceted filter). Renders left of the search box.
  toolbarLeft?: ReactNode;
  toolbarRight?: ReactNode;

  // When this value changes (e.g. caller-driven filter/sort state changes),
  // the table resets to page 0. Use this when the data scope changes — not
  // for cache-invalidation refreshes after mutations, which should keep the
  // user on the current page.
  resetPaginationKey?: string | number;

  onRowClick?: (row: T) => void;
  getRowAriaLabel?: (row: T) => string;
  getRowClassName?: (row: T) => string | undefined;

  emptyMessage?: string;
  loadingMessage?: string;
  globalFilterFn?: (row: T, search: string) => boolean;
};

// Auto mode probes /loadPage with pageSize=0 to learn the total without
// downloading rows. Below `autoThreshold` (default 1000) it switches to
// client mode (single loadAll call, sort/filter/page in-memory). At or above,
// it stays in server mode (loadPage per state change).
export function DataTable<T>(props: DataTableProps<T>) {
  const {
    mode = "auto",
    autoThreshold = 1000,
    loadAll,
    loadPage,
    queryKey,
    columns,
    rowKey,
    columnWidths,
    pageSize: initialPageSize = 25,
    pageSizeOptions = [10, 25, 50, 100],
    initialSort = [],
    searchEnabled = true,
    searchPlaceholder = "Search…",
    filters,
    allFilterLabel = "All",
    toolbarLeft,
    toolbarRight,
    resetPaginationKey,
    onRowClick,
    getRowAriaLabel,
    getRowClassName,
    emptyMessage = "No records found.",
    loadingMessage = "Loading…",
    globalFilterFn
  } = props;

  const [sorting, setSorting] = useState<SortingState>(initialSort);
  const [pagination, setPagination] = useState<PaginationState>({ pageIndex: 0, pageSize: initialPageSize });
  // The input is controlled by `searchInput` for immediate visual feedback;
  // `search` (debounced 400ms below) is what actually drives filtering and
  // server queries. This keeps each keystroke from refetching.
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<string | null>(null);

  useEffect(() => {
    if (searchInput === search) return;
    const handle = window.setTimeout(() => {
      setSearch(searchInput);
      setPagination((p) => (p.pageIndex === 0 ? p : { ...p, pageIndex: 0 }));
    }, 400);
    return () => window.clearTimeout(handle);
  }, [searchInput, search]);

  // Count probe — only fires in auto mode, decides client vs. server based on
  // total. Stale time keeps it from re-running on every interaction.
  const probe = useQuery({
    queryKey: [...queryKey, "count-probe"],
    queryFn: () => loadPage!({ page: 0, pageSize: 0, search: "", sort: null, filter: null }),
    enabled: mode === "auto" && !!loadPage,
    staleTime: 30_000
  });

  const effectiveMode: "client" | "server" | "loading" = useMemo(() => {
    if (mode === "client") return "client";
    if (mode === "server") return "server";
    if (probe.data) return probe.data.totalCount <= autoThreshold ? "client" : "server";
    // Probe failure (e.g. backend missing the paged endpoint): fall back to
    // client mode if loadAll is available — better to load everything than
    // to hang the page.
    if (probe.isError && loadAll) return "client";
    return "loading";
  }, [mode, autoThreshold, probe.data, probe.isError, loadAll]);

  // Client mode: one big fetch.
  const clientQuery = useQuery({
    queryKey: [...queryKey, "all"],
    queryFn: () => loadAll!(),
    enabled: effectiveMode === "client" && !!loadAll
  });

  const sortKey = sorting[0] ? `${sorting[0].id}:${sorting[0].desc ? "desc" : "asc"}` : null;
  // Server mode: refetch per (page, size, sort, search, filter).
  const serverQuery = useQuery({
    queryKey: [
      ...queryKey,
      "page",
      pagination.pageIndex,
      pagination.pageSize,
      search,
      sortKey,
      filter
    ],
    queryFn: () =>
      loadPage!({
        page: pagination.pageIndex,
        pageSize: pagination.pageSize,
        search,
        sort: sorting[0] ? { id: sorting[0].id, desc: sorting[0].desc } : null,
        filter
      }),
    enabled: effectiveMode === "server" && !!loadPage,
    placeholderData: (prev) => prev
  });

  const allRows: T[] = clientQuery.data ?? [];
  const filteredAllRows = useMemo(() => {
    if (effectiveMode !== "client" || !filter) return allRows;
    const opt = filters?.find((f) => f.id === filter);
    return opt?.predicate ? allRows.filter(opt.predicate) : allRows;
  }, [effectiveMode, filter, filters, allRows]);

  const tableData =
    effectiveMode === "client" ? filteredAllRows : (serverQuery.data?.items ?? []);
  const serverRowCount = serverQuery.data?.totalCount ?? 0;

  const isLoading =
    effectiveMode === "loading" ||
    (effectiveMode === "client" && clientQuery.isPending) ||
    (effectiveMode === "server" && serverQuery.isPending);

  const table = useReactTable<T>({
    data: tableData,
    columns,
    state: { sorting, globalFilter: search, pagination },
    onSortingChange: setSorting,
    onGlobalFilterChange: setSearch,
    onPaginationChange: setPagination,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: effectiveMode === "client" ? getSortedRowModel() : undefined,
    getFilteredRowModel: effectiveMode === "client" ? getFilteredRowModel() : undefined,
    getPaginationRowModel: effectiveMode === "client" ? getPaginationRowModel() : undefined,
    manualSorting: effectiveMode === "server",
    manualFiltering: effectiveMode === "server",
    manualPagination: effectiveMode === "server",
    rowCount: effectiveMode === "server" ? serverRowCount : undefined,
    globalFilterFn:
      effectiveMode === "client" && globalFilterFn
        ? (row, _id, value) => globalFilterFn(row.original, String(value))
        : undefined
  });

  const { pageIndex, pageSize } = table.getState().pagination;
  const totalPages = table.getPageCount();
  const filteredCount =
    effectiveMode === "client" ? table.getFilteredRowModel().rows.length : serverRowCount;
  const pageButtons = useMemo(() => buildPageWindow(pageIndex, totalPages, 7), [pageIndex, totalPages]);

  // Keep the page index in range when totals shrink (e.g. filters that drop
  // hits, or rows get deleted). Without this, the table can sit on an empty
  // page after a delete.
  useEffect(() => {
    if (totalPages > 0 && pageIndex >= totalPages) {
      table.setPageIndex(Math.max(0, totalPages - 1));
    }
  }, [totalPages, pageIndex, table]);

  useEffect(() => {
    if (resetPaginationKey === undefined) return;
    setPagination((p) => (p.pageIndex === 0 ? p : { ...p, pageIndex: 0 }));
  }, [resetPaginationKey]);

  const filterCounts = useMemo(() => {
    if (effectiveMode !== "client" || !filters) return null;
    const m = new Map<string, number>();
    for (const f of filters) {
      m.set(f.id, f.predicate ? allRows.filter(f.predicate).length : 0);
    }
    return m;
  }, [effectiveMode, filters, allRows]);

  const allCount =
    effectiveMode === "client"
      ? allRows.length
      : (probe.data?.totalCount ?? serverRowCount);

  const onSearch = (v: string) => {
    setSearchInput(v);
  };

  const onPickFilter = (id: string | null) => {
    setFilter(id);
    table.setPageIndex(0);
  };

  return (
    <div className="data-table">
      <div className="data-table-toolbar d-flex flex-column flex-lg-row justify-content-between align-items-lg-center gap-3 mb-3">
        <div className="data-table-toolbar-start d-flex align-items-center gap-3 flex-wrap">
          {filters && (
            <div className="data-table-tabs" role="group" aria-label="Filter">
              <button
                type="button"
                className={`data-table-tab${filter === null ? " active" : ""}`}
                onClick={() => onPickFilter(null)}
                aria-pressed={filter === null}
              >
                {allFilterLabel} <span className="data-table-tab-count">{allCount}</span>
              </button>
              {filters.map((f) => {
                const active = filter === f.id;
                const count = filterCounts?.get(f.id);
                return (
                  <button
                    key={f.id}
                    type="button"
                    className={`data-table-tab${active ? " active" : ""}`}
                    onClick={() => onPickFilter(f.id)}
                    aria-pressed={active}
                  >
                    {f.label}
                    {count !== undefined && <span className="data-table-tab-count">{count}</span>}
                  </button>
                );
              })}
            </div>
          )}
          {toolbarLeft}
        </div>
        <div className="d-flex align-items-center gap-2 ms-lg-auto">
          {searchEnabled && (
            <div className="data-table-search">
              <i className="fa fa-magnifying-glass data-table-search-icon" aria-hidden="true"></i>
              <input
                type="search"
                className="form-control"
                placeholder={searchPlaceholder}
                aria-label={searchPlaceholder}
                value={searchInput}
                onChange={(e) => onSearch(e.target.value)}
              />
            </div>
          )}
          {toolbarRight}
        </div>
      </div>

      <div className="data-table-table-wrap">
        <table className="data-table-table">
          <colgroup>
            {columnWidths.map((w, i) => (
              <col key={i} style={{ width: w }} />
            ))}
          </colgroup>
          <thead>
            <tr>
              {table.getHeaderGroups()[0].headers.map((header) => {
                const canSort = header.column.getCanSort();
                const sortDir = header.column.getIsSorted();
                const isActions = header.id === "actions";
                return (
                  <th
                    key={header.id}
                    scope="col"
                    className={`data-table-th${isActions ? " data-table-th-actions" : ""}${
                      canSort ? " data-table-th-sortable" : ""
                    }`}
                    onClick={canSort ? header.column.getToggleSortingHandler() : undefined}
                    aria-sort={
                      sortDir === "asc"
                        ? "ascending"
                        : sortDir === "desc"
                          ? "descending"
                          : canSort
                            ? "none"
                            : undefined
                    }
                  >
                    {header.isPlaceholder
                      ? null
                      : flexRender(header.column.columnDef.header, header.getContext())}
                    {canSort && <SortIndicator dir={(sortDir || null) as "asc" | "desc" | null} />}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr className="data-table-empty-row">
                <td colSpan={columns.length}>{loadingMessage}</td>
              </tr>
            )}
            {!isLoading && table.getRowModel().rows.length === 0 && (
              <tr className="data-table-empty-row">
                <td colSpan={columns.length}>{emptyMessage}</td>
              </tr>
            )}
            {!isLoading &&
              table.getRowModel().rows.map((row) => {
                const extraClass = getRowClassName?.(row.original);
                const ariaLabel = getRowAriaLabel?.(row.original);
                return (
                  <tr
                    key={rowKey(row.original)}
                    className={`data-table-row${extraClass ? ` ${extraClass}` : ""}${
                      onRowClick ? " data-table-row-clickable" : ""
                    }`}
                    tabIndex={onRowClick ? 0 : undefined}
                    aria-label={ariaLabel}
                    onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                    onKeyDown={
                      onRowClick
                        ? (e) => {
                            if (e.key === "Enter" || e.key === " ") {
                              e.preventDefault();
                              onRowClick(row.original);
                            }
                          }
                        : undefined
                    }
                  >
                    {row.getVisibleCells().map((cell) => {
                      const isActions = cell.column.id === "actions";
                      return (
                        <td
                          key={cell.id}
                          className={`data-table-td${isActions ? " data-table-td-actions" : ""}`}
                        >
                          {flexRender(cell.column.columnDef.cell, cell.getContext())}
                        </td>
                      );
                    })}
                  </tr>
                );
              })}
          </tbody>
        </table>
      </div>

      <div className="data-table-footer d-flex flex-column flex-lg-row justify-content-between align-items-lg-center gap-3 mt-3">
        <div className="data-table-footer-start d-flex flex-column flex-sm-row align-items-sm-center gap-3">
          <label className="data-table-length d-flex align-items-center gap-2 mb-0">
            <select
              className="form-select form-select-sm"
              value={pageSize}
              onChange={(e) => table.setPageSize(Number(e.target.value))}
              style={{ width: "auto" }}
            >
              {pageSizeOptions.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
            <span>per page</span>
          </label>
          <div className="data-table-info">
            {effectiveMode === "loading"
              ? ""
              : filteredCount === 0
                ? "Showing 0"
                : `Showing ${pageIndex * pageSize + 1}–${Math.min(
                    (pageIndex + 1) * pageSize,
                    filteredCount
                  )} of ${filteredCount}`}
          </div>
          <span className="data-table-mode-badge" title="Data loading mode">
            {effectiveMode === "client" ? "client" : effectiveMode === "server" ? "server" : "…"}
          </span>
        </div>
        <nav aria-label="Table pagination" className="data-table-paging">
          <ul className="pagination pagination-sm mb-0">
            <li className={`page-item ${!table.getCanPreviousPage() ? "disabled" : ""}`}>
              <button
                type="button"
                className="page-link"
                onClick={() => table.previousPage()}
                disabled={!table.getCanPreviousPage()}
                aria-label="Previous page"
              >
                <i className="fa fa-chevron-left"></i>
              </button>
            </li>
            {pageButtons.map((p) => (
              <li key={p} className={`page-item ${p === pageIndex ? "active" : ""}`}>
                <button
                  type="button"
                  className="page-link"
                  onClick={() => table.setPageIndex(p)}
                >
                  {p + 1}
                </button>
              </li>
            ))}
            <li className={`page-item ${!table.getCanNextPage() ? "disabled" : ""}`}>
              <button
                type="button"
                className="page-link"
                onClick={() => table.nextPage()}
                disabled={!table.getCanNextPage()}
                aria-label="Next page"
              >
                <i className="fa fa-chevron-right"></i>
              </button>
            </li>
          </ul>
        </nav>
      </div>
    </div>
  );
}

function SortIndicator({ dir }: { dir: "asc" | "desc" | null }) {
  if (dir === "asc") return <i className="fa fa-caret-up data-table-sort-active ms-1"></i>;
  if (dir === "desc") return <i className="fa fa-caret-down data-table-sort-active ms-1"></i>;
  return <i className="fa fa-sort data-table-sort-idle ms-1"></i>;
}

function buildPageWindow(pageIndex: number, totalPages: number, max: number): number[] {
  if (totalPages <= 0) return [0];
  const half = Math.floor(max / 2);
  let start = Math.max(0, pageIndex - half);
  const end = Math.min(totalPages, start + max);
  start = Math.max(0, end - max);
  const out: number[] = [];
  for (let i = start; i < end; i++) out.push(i);
  return out;
}
