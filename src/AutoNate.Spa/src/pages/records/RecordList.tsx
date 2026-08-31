import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import type { DataTableColumn } from "@/components/data-table/DataTable";
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Checkbox,
  Group,
  Pill,
  Popover,
  Stack,
  Tooltip,
  UnstyledButton
} from "@mantine/core";
import {
  DndContext,
  DragEndEvent,
  PointerSensor,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  useSortable,
  verticalListSortingStrategy
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import PageHeader from "@/components/PageHeader";
import { useRecordTypeFields, useRecordTypes } from "@/hooks/useRecordTypes";
import { searchRecords } from "@/api/records";
import {
  FilterOperatorWire,
  RecordModel,
  RecordTypeField,
  SearchFilterClause,
  SearchRecordsRequest
} from "@/types/records";
import "./fields/renderers"; // ensure renderers register on first import
import { getRenderer, getOptionChoices } from "./fields/registry";
import RecordFilterBuilder from "./RecordFilterBuilder";
import {
  DataTable,
  DataTablePageRequest
} from "@/components/data-table/DataTable";
import { ArchivedBadge } from "@/components/ArchivedBadge";

// Cap on the client-mode preload — must match the auto-mode threshold below
// so that totalCount ≤ CLIENT_PRELOAD means "we have the entire result set
// in-memory". Backed by the server's pageSize clamp (also 1000).
const CLIENT_PRELOAD = 1000;

const BUILTIN_COLUMN_IDS = ["key", "name", "status", "dueDate", "updatedAtUtc"] as const;
const BUILTIN_COLUMN_LABELS: Record<string, string> = {
  key: "Key",
  name: "Name",
  status: "Status",
  dueDate: "Due Date",
  updatedAtUtc: "Updated"
};

// Fixed proportional widths for built-in columns. Field columns split the
// remainder evenly (with an 8% floor each). See computeWidths below.
const FIXED_BUILTIN_WIDTHS: Record<string, number> = {
  key: 8,
  name: 18,
  status: 12,
  dueDate: 12,
  updatedAtUtc: 14
};

// Built-in column → server-side sort token. Dynamic field columns use the
// `field:<fieldKey>:asc|desc` form, which the backend resolves against the
// record type's field list and casts numeric/boolean fields appropriately.
const BUILTIN_SORT_TOKENS: Record<string, { asc: string; desc: string }> = {
  key: { asc: "key_asc", desc: "key_desc" },
  name: { asc: "name_asc", desc: "name_desc" },
  status: { asc: "status_asc", desc: "status_desc" },
  dueDate: { asc: "due_date_asc", desc: "due_date_desc" },
  updatedAtUtc: { asc: "updated_asc", desc: "updated_desc" }
};

function buildSortToken(columnId: string, desc: boolean): string | undefined {
  const dir = desc ? "desc" : "asc";
  const builtin = BUILTIN_SORT_TOKENS[columnId];
  if (builtin) return builtin[dir];
  if (columnId.startsWith("field-")) {
    return `field:${columnId.slice("field-".length)}:${dir}`;
  }
  return undefined;
}

const columnsStorageKey = (recordTypeId: string) => `recordList.columns.${recordTypeId}`;
const columnOrderStorageKey = (recordTypeId: string) =>
  `recordList.columnOrder.${recordTypeId}`;

// Default-visible set when the user has no saved selection: every built-in
// column + the first four (non-archived) fields, matching what the table
// looked like before the picker existed.
function defaultVisibleIds(fields: RecordTypeField[]): Set<string> {
  const ids = new Set<string>(BUILTIN_COLUMN_IDS);
  for (const f of fields.filter((f) => !f.isArchived).slice(0, 4)) {
    ids.add(`field-${f.fieldKey}`);
  }
  return ids;
}

// Default column order matching the original layout: built-ins, then fields
// in their sortOrder, then Updated last.
function defaultColumnOrder(fields: RecordTypeField[]): string[] {
  return [
    "key",
    "name",
    "status",
    "dueDate",
    ...fields.filter((f) => !f.isArchived).map((f) => `field-${f.fieldKey}`),
    "updatedAtUtc"
  ];
}

export default function RecordList() {
  const { typeShortCode = "" } = useParams<{ typeShortCode: string }>();
  const navigate = useNavigate();
  const code = typeShortCode.toUpperCase();

  const { data: types = [], isLoading: loadingTypes } = useRecordTypes(true);
  const type = types.find((t) => t.shortCode === code) ?? null;

  const [filters, setFilters] = useState<SearchFilterClause[]>([]);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [columnsOpen, setColumnsOpen] = useState(false);

  const { data: fields = [] } = useRecordTypeFields(type?.id ?? null, false);
  const allFields = useMemo(() => fields.filter((f) => !f.isArchived), [fields]);
  const fieldByKey = useMemo(() => {
    const m = new Map<string, RecordTypeField>();
    for (const f of fields) m.set(f.fieldKey, f);
    return m;
  }, [fields]);

  // Lazy-init from localStorage on first render where (type, fields) are both
  // available. Subsequent edits are saved by the matching persist effect.
  const [visibleColumnIds, setVisibleColumnIds] = useState<Set<string> | null>(null);
  useEffect(() => {
    if (!type?.id || visibleColumnIds !== null) return;
    const saved = localStorage.getItem(columnsStorageKey(type.id));
    if (saved) {
      try {
        const ids = JSON.parse(saved);
        if (Array.isArray(ids)) {
          setVisibleColumnIds(new Set(ids));
          return;
        }
      } catch {
        // fall through to defaults
      }
    }
    setVisibleColumnIds(defaultVisibleIds(allFields));
  }, [type?.id, allFields, visibleColumnIds]);
  useEffect(() => {
    if (!type?.id || !visibleColumnIds) return;
    localStorage.setItem(
      columnsStorageKey(type.id),
      JSON.stringify([...visibleColumnIds])
    );
  }, [type?.id, visibleColumnIds]);

  const [columnOrder, setColumnOrder] = useState<string[] | null>(null);
  useEffect(() => {
    if (!type?.id || columnOrder !== null) return;
    const saved = localStorage.getItem(columnOrderStorageKey(type.id));
    if (saved) {
      try {
        const ids = JSON.parse(saved);
        if (Array.isArray(ids) && ids.every((x) => typeof x === "string")) {
          setColumnOrder(ids);
          return;
        }
      } catch {
        // fall through to defaults
      }
    }
    setColumnOrder(defaultColumnOrder(allFields));
  }, [type?.id, allFields, columnOrder]);
  useEffect(() => {
    if (!type?.id || !columnOrder) return;
    localStorage.setItem(columnOrderStorageKey(type.id), JSON.stringify(columnOrder));
  }, [type?.id, columnOrder]);

  const filtersActive = filters.length > 0;

  const loadPage = useMemo(() => {
    return async (req: DataTablePageRequest) => {
      if (!type?.id) return { items: [], totalCount: 0 };
      const sortToken = req.sort ? buildSortToken(req.sort.id, req.sort.desc) : undefined;
      const request: SearchRecordsRequest = {
        recordTypeId: type.id,
        filters,
        // Always include archived; the row-archived class makes them visually
        // distinct, and explicit filters can still exclude them.
        includeArchived: true,
        page: req.page,
        pageSize: req.pageSize,
        sort: sortToken,
        search: req.search || undefined
      };
      const r = await searchRecords(request);
      return { items: r.items, totalCount: r.totalCount };
    };
  }, [type?.id, filters]);

  // Used in client mode (auto switches in when totalCount ≤ CLIENT_PRELOAD).
  // Server-side filters still apply; sort + free-text search + pagination then
  // run in-memory against the preloaded set, AND column toggles render new
  // cells from the same in-memory data — no extra server trips.
  const loadAll = useMemo(() => {
    return async (): Promise<RecordModel[]> => {
      if (!type?.id) return [];
      const r = await searchRecords({
        recordTypeId: type.id,
        filters,
        includeArchived: true,
        page: 0,
        pageSize: CLIENT_PRELOAD
      });
      return r.items;
    };
  }, [type?.id, filters]);

  // Build the full set of available columns (built-ins + every non-archived
  // field). The visible subset is filtered downstream from visibleColumnIds.
  const allColumns = useMemo<DataTableColumn<RecordModel>[]>(() => {
    return [
      {
        id: "key",
        // accessorFn returns keyNumber so client-side sort matches the server
        // (which sorts by key_number, not the formatted string).
        accessorFn: (r) => r.keyNumber,
        header: "Key",
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
        cell: ({ row }) => (
          <>
            {row.original.name}
            {row.original.isArchived && <ArchivedBadge />}
          </>
        )
      },
      {
        id: "status",
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) =>
          row.original.status ?? <span className="text-body text-opacity-50">—</span>
      },
      {
        id: "dueDate",
        accessorKey: "dueDate",
        header: "Due Date",
        cell: ({ row }) =>
          row.original.dueDate ? (
            formatDate(row.original.dueDate)
          ) : (
            <span className="text-body text-opacity-50">—</span>
          )
      },
      ...allFields.map((f): DataTableColumn<RecordModel> => ({
        id: `field-${f.fieldKey}`,
        accessorFn: (r) => r.values[f.fieldKey],
        header: f.displayName,
        cell: ({ row }) => {
          const renderer = getRenderer(f.dataType);
          return renderer
            ? renderer.formatValue(f, row.original.values[f.fieldKey])
            : String(row.original.values[f.fieldKey] ?? "");
        }
      })),
      {
        id: "updatedAtUtc",
        accessorKey: "updatedAtUtc",
        header: "Updated",
        cell: ({ row }) => {
          const iso = row.original.updatedAtUtc;
          const d = new Date(iso);
          if (Number.isNaN(d.getTime())) return iso;
          return (
            <Tooltip label={d.toLocaleString()} withArrow>
              <span>{d.toLocaleDateString()}</span>
            </Tooltip>
          );
        }
      }
    ];
  }, [allFields]);

  // Reconcile saved order with current column set: keep the saved order for
  // known ids, then append any columns that exist but aren't in the saved
  // order yet (e.g. fields added since the user last reordered).
  const resolvedOrder = useMemo(() => {
    const baseOrder = columnOrder ?? defaultColumnOrder(allFields);
    const known = new Set(allColumns.map((c) => c.id ?? ""));
    const ordered = baseOrder.filter((id) => known.has(id));
    const seen = new Set(ordered);
    for (const c of allColumns) {
      if (c.id && !seen.has(c.id)) ordered.push(c.id);
    }
    return ordered;
  }, [columnOrder, allColumns, allFields]);

  // Build the visible-and-ordered columns array (and matching widths) by
  // walking resolvedOrder, skipping hidden ids, and looking up each column
  // by id in allColumns. Index alignment with widths holds by construction.
  const { columns, columnWidths } = useMemo(() => {
    const visible = visibleColumnIds ?? defaultVisibleIds(allFields);
    const byId = new Map(allColumns.map((c) => [c.id ?? "", c] as const));
    const cols = resolvedOrder
      .filter((id) => visible.has(id))
      .map((id) => byId.get(id))
      .filter((c): c is DataTableColumn<RecordModel> => Boolean(c));
    const widths = computeWidths(cols);
    return { columns: cols, columnWidths: widths };
  }, [allColumns, visibleColumnIds, resolvedOrder, allFields]);

  const removeFilter = (index: number) => {
    setFilters((prev) => prev.filter((_, i) => i !== index));
  };

  const toggleColumn = (id: string, on: boolean) => {
    setVisibleColumnIds((prev) => {
      const next = new Set(prev ?? defaultVisibleIds(allFields));
      if (on) next.add(id);
      else next.delete(id);
      // Keep at least one visible column so the table doesn't render empty.
      if (next.size === 0) next.add("key");
      return next;
    });
  };

  // 4px activation distance lets a click on the row reach the Checkbox
  // without immediately starting a drag.
  const dragSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  );

  const handleColumnDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    const oldIdx = resolvedOrder.indexOf(String(active.id));
    const newIdx = resolvedOrder.indexOf(String(over.id));
    if (oldIdx === -1 || newIdx === -1) return;
    setColumnOrder(arrayMove(resolvedOrder, oldIdx, newIdx));
  };

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

  // Options listed in the Columns popover, walking the user's saved order
  // (with new fields appended) so the popover ordering matches the table.
  const columnOptions: { id: string; label: string }[] = resolvedOrder.map((id) => {
    if (id.startsWith("field-")) {
      const key = id.slice("field-".length);
      return { id, label: fieldByKey.get(key)?.displayName ?? key };
    }
    return { id, label: BUILTIN_COLUMN_LABELS[id] ?? id };
  });
  const visibleSet = visibleColumnIds ?? defaultVisibleIds(allFields);

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
      />

      {filtersActive && (
        <Group gap="xs" mb="sm" wrap="wrap">
          {filters.map((clause, i) => (
            <Pill
              key={`${clause.fieldKey}-${i}`}
              withRemoveButton
              onRemove={() => removeFilter(i)}
              size="md"
            >
              {formatClauseLabel(clause, fieldByKey)}
            </Pill>
          ))}
          <Button
            size="compact-xs"
            variant="subtle"
            color="gray"
            onClick={() => setFilters([])}
          >
            Clear all
          </Button>
        </Group>
      )}

      <DataTable<RecordModel>
        // Auto mode probes via loadPage(pageSize=0) to learn totalCount, then
        // switches to client (one big loadAll) below the threshold or stays in
        // server (per-page loadPage) at or above it.
        autoThreshold={CLIENT_PRELOAD}
        loadAll={loadAll}
        loadPage={loadPage}
        // The full key is what react-query caches by. Including filters means
        // tweaking them invalidates and refetches without us touching DataTable
        // internals. (DataTable already adds page/size/sort/search.) Column
        // visibility is pure UI — not in the key.
        queryKey={["records", type?.id ?? "", { filters }]}
        // ...and resetPaginationKey jumps the user back to page 0 when the
        // scope changes (so they don't sit on empty page 5 of a new filter).
        resetPaginationKey={`${filters.length}`}
        columns={columns}
        rowKey={(r) => r.id}
        columnWidths={columnWidths}
        searchPlaceholder="Search records…"
        // Mirrors the server's ILIKE on key/name/status so client and server
        // modes match. (Without this the default scan would only see the
        // built-in column accessor values, missing key.)
        globalFilterFn={(r, search) => {
          const needle = search.toLowerCase();
          return (
            r.key.toLowerCase().includes(needle) ||
            r.name.toLowerCase().includes(needle) ||
            (r.status ?? "").toLowerCase().includes(needle)
          );
        }}
        emptyMessage={
          filtersActive
            ? "No records match your filters."
            : `No records yet. Create the first ${type?.name ?? "record"}.`
        }
        loadingMessage="Loading records…"
        initialSort={[{ id: "updatedAtUtc", desc: true }]}
        getRowClassName={(r) => (r.isArchived ? "row-archived" : undefined)}
        onRowClick={(r) => navigate(`/record/${r.key}`)}
        getRowAriaLabel={(r) => `Open ${r.key}`}
        toolbarLeft={
          <Group gap="sm" wrap="wrap" align="center">
            <Popover
              opened={columnsOpen}
              onChange={setColumnsOpen}
              position="bottom-start"
              shadow="md"
              withArrow
            >
              <Popover.Target>
                <Button
                  size="xs"
                  variant="default"
                  onClick={() => setColumnsOpen((o) => !o)}
                  leftSection={<i className="fa fa-table-columns" />}
                >
                  Columns
                </Button>
              </Popover.Target>
              <Popover.Dropdown>
                <Stack gap={6} style={{ minWidth: 240, maxHeight: 360, overflowY: "auto" }}>
                  <DndContext sensors={dragSensors} onDragEnd={handleColumnDragEnd}>
                    <SortableContext
                      items={columnOptions.map((o) => o.id)}
                      strategy={verticalListSortingStrategy}
                    >
                      {columnOptions.map((opt) => (
                        <SortableColumnRow
                          key={opt.id}
                          id={opt.id}
                          label={opt.label}
                          checked={visibleSet.has(opt.id)}
                          onToggle={(on) => toggleColumn(opt.id, on)}
                        />
                      ))}
                    </SortableContext>
                  </DndContext>
                  <Button
                    size="compact-xs"
                    variant="subtle"
                    mt={4}
                    onClick={() => {
                      setVisibleColumnIds(defaultVisibleIds(allFields));
                      setColumnOrder(defaultColumnOrder(allFields));
                    }}
                  >
                    Reset to defaults
                  </Button>
                </Stack>
              </Popover.Dropdown>
            </Popover>
            <Popover
              opened={filtersOpen}
              onChange={setFiltersOpen}
              position="bottom-start"
              shadow="md"
              withArrow
              trapFocus
            >
              <Popover.Target>
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
              </Popover.Target>
              <Popover.Dropdown>
                <Box style={{ minWidth: 520 }}>
                  <RecordFilterBuilder
                    fields={fields}
                    initialFilters={filters}
                    onApply={(applied) => {
                      setFilters(applied);
                      setFiltersOpen(false);
                    }}
                    onClear={() => {
                      setFilters([]);
                      setFiltersOpen(false);
                    }}
                  />
                </Box>
              </Popover.Dropdown>
            </Popover>
          </Group>
        }
        toolbarBeforeSearch={
          <Tooltip label={`New ${type?.name ?? "record"}`} withArrow>
            <ActionIcon
              size="lg"
              variant="filled"
              aria-label={`New ${type?.name ?? "record"}`}
              disabled={!type}
              onClick={() => navigate(`/records/${code}/new`)}
            >
              <i className="fa fa-plus" />
            </ActionIcon>
          </Tooltip>
        }
      />
    </>
  );
}

// One row in the Columns popover: a drag handle (the only drag activator) +
// a checkbox. Wired through useSortable so reordering happens via @dnd-kit.
function SortableColumnRow({
  id,
  label,
  checked,
  onToggle
}: {
  id: string;
  label: string;
  checked: boolean;
  onToggle: (on: boolean) => void;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging
  } = useSortable({ id });
  return (
    <div
      ref={setNodeRef}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        display: "flex",
        alignItems: "center",
        gap: 8
      }}
    >
      <UnstyledButton
        ref={setActivatorNodeRef}
        {...attributes}
        {...listeners}
        title="Drag to reorder"
        aria-label="Drag to reorder"
        style={{
          cursor: "grab",
          color: "var(--mantine-color-gray-6)",
          padding: "0 4px",
          touchAction: "none"
        }}
      >
        <i className="fa fa-grip-vertical" />
      </UnstyledButton>
      <Checkbox
        label={label}
        checked={checked}
        onChange={(e) => onToggle(e.currentTarget.checked)}
        style={{ flex: 1 }}
      />
    </div>
  );
}

// Sum the fixed widths for visible built-in columns, then split whatever's
// left of 100% evenly across the visible field columns (8% floor per field).
function computeWidths(visibleColumns: DataTableColumn<RecordModel>[]): string[] {
  let fixedTotal = 0;
  let dynamicCount = 0;
  for (const c of visibleColumns) {
    const fixed = c.id ? FIXED_BUILTIN_WIDTHS[c.id] : undefined;
    if (fixed !== undefined) fixedTotal += fixed;
    else dynamicCount += 1;
  }
  const dynamicShare = dynamicCount > 0
    ? Math.max(8, Math.floor((100 - fixedTotal) / dynamicCount))
    : 0;
  return visibleColumns.map((c) => {
    const fixed = c.id ? FIXED_BUILTIN_WIDTHS[c.id] : undefined;
    if (fixed !== undefined) return `${fixed}%`;
    return `${dynamicShare}%`;
  });
}

const OPERATOR_SYMBOLS: Record<FilterOperatorWire, string> = {
  eq: "=",
  neq: "≠",
  gt: ">",
  gte: "≥",
  lt: "<",
  lte: "≤",
  contains: "contains",
  in: "in"
};

function formatClauseLabel(
  clause: SearchFilterClause,
  fieldByKey: Map<string, RecordTypeField>
): string {
  const field = fieldByKey.get(clause.fieldKey);
  const fieldLabel = field?.displayName ?? clause.fieldKey;
  const opLabel = OPERATOR_SYMBOLS[clause.op] ?? clause.op;
  return `${fieldLabel} ${opLabel} ${formatClauseValue(clause.value, field)}`;
}

function formatClauseValue(value: unknown, field: RecordTypeField | undefined): string {
  if (value === null || value === undefined || value === "") return "—";
  if (Array.isArray(value)) return value.map((v) => formatClauseValue(v, field)).join(", ");
  if (typeof value === "boolean") return value ? "true" : "false";
  if (field?.dataType === "option") {
    const choices = getOptionChoices(field);
    const match = choices.find((c) => c.value === value);
    if (match) return match.label;
  }
  if (field?.dataType === "date" && typeof value === "string") {
    return formatDate(value);
  }
  return String(value);
}

// `YYYY-MM-DD` is parsed as UTC by `new Date()`, which would shift the rendered
// day in negative-offset timezones. Build the date locally instead.
function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map((s) => Number(s));
  if (!y || !m || !d) return yyyyMmDd;
  const date = new Date(y, m - 1, d);
  return Number.isNaN(date.getTime()) ? yyyyMmDd : date.toLocaleDateString();
}
