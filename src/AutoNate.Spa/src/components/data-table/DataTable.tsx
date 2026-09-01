import { KeyboardEvent as ReactKeyboardEvent, ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Group,
  Stack,
  TextInput,
  UnstyledButton
} from "@mantine/core";
import { DataTable as MantineDataTable, type DataTableSortStatus } from "mantine-datatable";

// Column shape consumed by the wrapper. Modeled on the subset of
// @tanstack/react-table's ColumnDef that this codebase actually used, so
// existing column definitions keep working with only an import change.
export type DataTableCellContext<T> = {
  row: { original: T; getValue: (k: string) => unknown };
  getValue: () => unknown;
  column: { id: string; columnDef: DataTableColumn<T> };
  table: Record<string, never>;
};

export type DataTableHeaderContext<T> = {
  column: { id: string; columnDef: DataTableColumn<T> };
};

export type DataTableColumn<T> = {
  id?: string;
  accessorKey?: keyof T & string;
  accessorFn?: (row: T) => unknown;
  header?: ReactNode | ((ctx: DataTableHeaderContext<T>) => ReactNode);
  cell?: (ctx: DataTableCellContext<T>) => ReactNode;
  enableSorting?: boolean;
  meta?: { wrap?: boolean };
};

// Module-level defaults so destructuring `pageSizeOptions = [...]` doesn't
// allocate a fresh array per render — mantine-datatable feeds the prop into
// internal effects, and an unstable reference there triggers an infinite
// re-render loop.
const DEFAULT_PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;
const DEFAULT_INITIAL_SORT: { id: string; desc: boolean }[] = [];

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

  columns: DataTableColumn<T>[];
  rowKey: (row: T) => string;
  columnWidths: string[];

  pageSize?: number;
  pageSizeOptions?: number[];
  initialSort?: { id: string; desc: boolean }[];

  searchEnabled?: boolean;
  searchPlaceholder?: string;

  filters?: DataTableFilterOption<T>[];
  allFilterLabel?: string;

  toolbarLeft?: ReactNode;
  toolbarBeforeSearch?: ReactNode;
  toolbarRight?: ReactNode;

  resetPaginationKey?: string | number;

  onRowClick?: (row: T) => void;
  getRowAriaLabel?: (row: T) => string;
  getRowClassName?: (row: T) => string | undefined;

  emptyMessage?: string;
  loadingMessage?: string;
  globalFilterFn?: (row: T, search: string) => boolean;

  refetchInterval?: number;
};

// A column resolved into the bits this wrapper needs: a stable id, a title,
// whether it sorts, a width, an optional cell renderer, and a value resolver
// used for client-side sort + default search. The resolver hides the
// difference between accessorKey, accessorFn, and id-only columns from the
// rest of the wrapper.
type ResolvedColumn<T> = {
  id: string;
  title: ReactNode;
  sortable: boolean;
  width: string | undefined;
  textAlign?: "left" | "center" | "right";
  noWrap: boolean;
  isActions: boolean;
  resolve: (row: T) => unknown;
  render: (row: T) => ReactNode;
};

function resolveColumn<T>(col: DataTableColumn<T>, width: string | undefined): ResolvedColumn<T> {
  const { accessorKey, accessorFn } = col;
  const id = col.id ?? accessorKey ?? "";
  const isActions = id === "actions";

  const resolve = (row: T): unknown => {
    if (accessorFn) return accessorFn(row);
    if (accessorKey) return (row as Record<string, unknown>)[accessorKey];
    return undefined;
  };

  const headerNode: ReactNode =
    typeof col.header === "function"
      ? col.header({ column: { id, columnDef: col } })
      : (col.header ?? id);

  const customRender = col.cell;
  const render = (row: T): ReactNode => {
    if (customRender) {
      return customRender({
        row: {
          original: row,
          getValue: (k: string) => (row as Record<string, unknown>)[k]
        },
        getValue: () => resolve(row),
        column: { id, columnDef: col },
        table: {}
      });
    }
    const v = resolve(row);
    return v == null ? "" : String(v);
  };

  // Columns can opt out of the ellipsis-truncate by setting
  // `meta: { wrap: true }` on the column def.
  const noWrap = (col.meta as { wrap?: boolean } | undefined)?.wrap !== true && !isActions;

  return {
    id,
    title: headerNode,
    sortable: col.enableSorting !== false && !isActions && id !== "",
    width,
    noWrap,
    isActions,
    resolve,
    render
  };
}

function compareValues(a: unknown, b: unknown): number {
  if (a == null && b == null) return 0;
  if (a == null) return -1;
  if (b == null) return 1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  if (a instanceof Date && b instanceof Date) return a.getTime() - b.getTime();
  return String(a).localeCompare(String(b));
}

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
    pageSizeOptions = DEFAULT_PAGE_SIZE_OPTIONS as unknown as number[],
    initialSort = DEFAULT_INITIAL_SORT,
    searchEnabled = true,
    searchPlaceholder = "Search…",
    filters,
    allFilterLabel = "All",
    toolbarLeft,
    toolbarBeforeSearch,
    toolbarRight,
    resetPaginationKey,
    onRowClick,
    getRowAriaLabel,
    getRowClassName,
    emptyMessage = "No records found.",
    loadingMessage = "Loading…",
    globalFilterFn,
    refetchInterval
  } = props;

  const resolvedColumns = useMemo(
    () => columns.map((c, i) => resolveColumn<T>(c, columnWidths[i])),
    [columns, columnWidths]
  );
  const resolversById = useMemo(() => {
    const m = new Map<string, (row: T) => unknown>();
    for (const c of resolvedColumns) m.set(c.id, c.resolve);
    return m;
  }, [resolvedColumns]);

  const [sortStatus, setSortStatus] = useState<DataTableSortStatus<T>>(() => {
    const first = initialSort[0];
    return {
      columnAccessor: (first?.id ?? "") as keyof T & string,
      direction: first?.desc ? "desc" : "asc"
    };
  });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<string | null>(null);

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
    if (probe.isError && loadAll) return "client";
    return "loading";
  }, [mode, autoThreshold, probe.data, probe.isError, loadAll]);

  // Client mode: apply search instantly since filtering is in-memory and free.
  // Server mode: debounce 400ms so typing doesn't refetch on every keystroke.
  // While auto-probe is still resolving ("loading") we debounce too — once it
  // resolves the effect re-runs and either flushes immediately (client) or
  // keeps the timer (server).
  useEffect(() => {
    if (searchInput === search) return;
    if (effectiveMode === "client") {
      setSearch(searchInput);
      setPage(1);
      return;
    }
    const handle = window.setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, 400);
    return () => window.clearTimeout(handle);
  }, [searchInput, search, effectiveMode]);

  // Client mode: one big fetch.
  const clientQuery = useQuery({
    queryKey: [...queryKey, "all"],
    queryFn: () => loadAll!(),
    enabled: effectiveMode === "client" && !!loadAll,
    refetchInterval
  });

  const sortIdForServer = sortStatus.columnAccessor as string;
  // Server mode: refetch per (page, size, sort, search, filter).
  const serverQuery = useQuery({
    queryKey: [
      ...queryKey,
      "page",
      page - 1,
      pageSize,
      search,
      sortIdForServer ? `${sortIdForServer}:${sortStatus.direction}` : null,
      filter
    ],
    queryFn: () =>
      loadPage!({
        page: page - 1,
        pageSize,
        search,
        sort: sortIdForServer
          ? { id: sortIdForServer, desc: sortStatus.direction === "desc" }
          : null,
        filter
      }),
    enabled: effectiveMode === "server" && !!loadPage,
    placeholderData: (prev) => prev,
    refetchInterval
  });

  const allRows: T[] = clientQuery.data ?? [];

  // Client-side filter (filter tabs).
  const filteredAllRows = useMemo(() => {
    if (effectiveMode !== "client" || !filter) return allRows;
    const opt = filters?.find((f) => f.id === filter);
    return opt?.predicate ? allRows.filter(opt.predicate) : allRows;
  }, [effectiveMode, filter, filters, allRows]);

  // Client-side search.
  const searchedRows = useMemo(() => {
    if (effectiveMode !== "client") return filteredAllRows;
    const q = search.trim().toLowerCase();
    if (!q) return filteredAllRows;
    if (globalFilterFn) return filteredAllRows.filter((row) => globalFilterFn(row, search));
    return filteredAllRows.filter((row) => {
      for (const col of resolvedColumns) {
        const v = col.resolve(row);
        if (v == null) continue;
        if (String(v).toLowerCase().includes(q)) return true;
      }
      return false;
    });
  }, [effectiveMode, filteredAllRows, search, globalFilterFn, resolvedColumns]);

  // Client-side sort.
  const sortedRows = useMemo(() => {
    if (effectiveMode !== "client") return searchedRows;
    const accessor = sortStatus.columnAccessor as string;
    if (!accessor) return searchedRows;
    const resolve = resolversById.get(accessor);
    if (!resolve) return searchedRows;
    const dir = sortStatus.direction === "asc" ? 1 : -1;
    return [...searchedRows].sort((a, b) => compareValues(resolve(a), resolve(b)) * dir);
  }, [effectiveMode, searchedRows, sortStatus, resolversById]);

  // Client-side paging slice — mantine-datatable shows whatever we hand it.
  const clientPaged = useMemo(() => {
    if (effectiveMode !== "client") return sortedRows;
    const start = (page - 1) * pageSize;
    return sortedRows.slice(start, start + pageSize);
  }, [effectiveMode, sortedRows, page, pageSize]);

  const records: T[] =
    effectiveMode === "client"
      ? clientPaged
      : (serverQuery.data?.items ?? []);
  const totalRecords =
    effectiveMode === "client"
      ? sortedRows.length
      : (serverQuery.data?.totalCount ?? 0);

  const isLoading =
    effectiveMode === "loading" ||
    (effectiveMode === "client" && clientQuery.isPending) ||
    (effectiveMode === "server" && serverQuery.isPending);

  // Keep page index in range when totals shrink (filters, deletes).
  useEffect(() => {
    const maxPage = Math.max(1, Math.ceil(totalRecords / pageSize));
    if (page > maxPage) setPage(maxPage);
  }, [totalRecords, pageSize, page]);

  useEffect(() => {
    if (resetPaginationKey === undefined) return;
    setPage(1);
  }, [resetPaginationKey]);

  // Map resolved columns into mantine-datatable's column shape. `resizable`
  // is on by default for all columns except action columns (where the
  // narrow icon-button cell would just look weird with a drag handle).
  const mantineColumns = useMemo(
    () =>
      resolvedColumns.map((c) => ({
        accessor: c.id,
        title: c.title,
        sortable: c.sortable,
        width: c.width,
        textAlign: c.textAlign,
        noWrap: c.noWrap,
        resizable: !c.isActions,
        render: c.render
      })),
    [resolvedColumns]
  );

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
      : (probe.data?.totalCount ?? totalRecords);

  // Stabilize callbacks passed to mantine-datatable so its internal effects
  // (some of which iterate ref arrays) don't refire on every parent render
  // and trip the "Maximum update depth exceeded" guard.
  const idAccessor = useCallback((record: T) => rowKey(record), [rowKey]);
  const handleSortChange = useCallback(
    (next: DataTableSortStatus<T>) => {
      setSortStatus(next);
      setPage(1);
    },
    []
  );
  const handleRecordsPerPageChange = useCallback((next: number) => {
    setPageSize(next);
    setPage(1);
  }, []);
  const onRowClickAdapter = useMemo(
    () => (onRowClick ? ({ record }: { record: T }) => onRowClick(record) : undefined),
    [onRowClick]
  );
  const rowClassNameAdapter = useMemo(
    () => (getRowClassName ? (record: T) => getRowClassName(record) ?? "" : undefined),
    [getRowClassName]
  );

  // mantine-datatable renders onRowClick as a bare <tr onClick>: no tabIndex,
  // no role, no key handler. Any table whose only way into a row is the row
  // itself was therefore mouse-only — Notifications could not be opened at all
  // without a pointer (WCAG 2.1.1 / 4.1.2, #12). customRowAttributes is the
  // supported hook for putting real attributes on the <tr>, so rows become
  // focusable buttons that answer Enter and Space, and getRowAriaLabel — which
  // this wrapper used to accept and throw away — finally names them.
  const rowAttributesAdapter = useMemo(
    () =>
      onRowClick
        ? (record: T) => ({
            tabIndex: 0,
            // Deliberately NOT role="button": a <tr> must keep its row role or
            // the table's structure stops being exposed at all, and browsers
            // will not surface a row as a button anyway. Focusable + a name +
            // Enter/Space is what makes it operable while staying a row.
            "aria-label": getRowAriaLabel?.(record),
            onKeyDown: (event: ReactKeyboardEvent<HTMLTableRowElement>) => {
              if (event.key !== "Enter" && event.key !== " ") return;
              // Space scrolls the page by default, and both would otherwise
              // also reach a focused control inside the row.
              if (event.target !== event.currentTarget) return;
              event.preventDefault();
              onRowClick(record);
            }
          })
        : undefined,
    [onRowClick, getRowAriaLabel]
  );

  return (
    <Stack gap="sm">
      <Group justify="space-between" gap="sm" wrap="wrap">
        <Group gap="sm" wrap="wrap">
          {filters && (
            <Group gap={4} role="group" aria-label="Filter">
              <FilterTab
                active={filter === null}
                onClick={() => {
                  setFilter(null);
                  setPage(1);
                }}
                label={allFilterLabel}
                count={allCount}
              />
              {filters.map((f) => (
                <FilterTab
                  key={f.id}
                  active={filter === f.id}
                  onClick={() => {
                    setFilter(f.id);
                    setPage(1);
                  }}
                  label={f.label}
                  count={filterCounts?.get(f.id)}
                />
              ))}
            </Group>
          )}
          {toolbarLeft}
        </Group>
        <Group gap="xs" wrap="nowrap">
          {toolbarBeforeSearch}
          {searchEnabled && (
            <TextInput
              placeholder={searchPlaceholder}
              aria-label={searchPlaceholder}
              value={searchInput}
              onChange={(e) => setSearchInput(e.currentTarget.value)}
              leftSection={<i className="fa fa-magnifying-glass" />}
              rightSection={
                searchInput ? (
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    size="sm"
                    aria-label="Clear search"
                    onClick={() => setSearchInput("")}
                  >
                    <i className="fa fa-xmark" />
                  </ActionIcon>
                ) : null
              }
              w={240}
            />
          )}
          {toolbarRight}
        </Group>
      </Group>

      <MantineDataTable<T>
        withTableBorder
        borderRadius="sm"
        striped
        highlightOnHover
        minHeight={120}
        fetching={isLoading}
        records={records}
        columns={mantineColumns}
        idAccessor={idAccessor}
        sortStatus={sortStatus}
        onSortStatusChange={handleSortChange}
        totalRecords={totalRecords}
        recordsPerPage={pageSize}
        recordsPerPageOptions={pageSizeOptions}
        onRecordsPerPageChange={handleRecordsPerPageChange}
        recordsPerPageLabel="Per page"
        page={page}
        onPageChange={setPage}
        noRecordsText={emptyMessage}
        loadingText={loadingMessage}
        onRowClick={onRowClickAdapter}
        rowClassName={rowClassNameAdapter}
        customRowAttributes={rowAttributesAdapter}
      />
    </Stack>
  );
}

function FilterTab({
  active,
  onClick,
  label,
  count
}: {
  active: boolean;
  onClick: () => void;
  label: string;
  count?: number;
}) {
  return (
    <UnstyledButton
      onClick={onClick}
      aria-pressed={active}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        height: 32,
        padding: "0 12px",
        borderRadius: 4,
        border: "1px solid var(--mantine-color-default-border)",
        background: active ? "var(--mantine-primary-color-filled)" : "transparent",
        color: active ? "white" : "inherit",
        fontSize: 13,
        cursor: "pointer",
        transition: "background 120ms ease, color 120ms ease"
      }}
    >
      <span>{label}</span>
      {count !== undefined && (
        <Badge
          size="xs"
          variant={active ? "white" : "default"}
          color={active ? "gray" : "gray"}
        >
          {count}
        </Badge>
      )}
    </UnstyledButton>
  );
}

