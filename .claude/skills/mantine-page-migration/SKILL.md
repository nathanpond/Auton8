---
name: mantine-page-migration
description: Use when migrating an AutoNate SPA page from ColorAdmin/Bootstrap + react-hook-form + @tanstack/react-table to Mantine v9. Covers the per-page recipe — forms, tables, modals, notifications, layout primitives — and the verification steps that have to run before the page is merged.
---

# Migrating an AutoNate page to Mantine v9

The SPA at `src/AutoNate.Spa/` is mid-migration from ColorAdmin v5 (Bootstrap 5) to Mantine v9. This skill is the per-page recipe used in **Phase II** (page-by-page). One page = one PR.

The framework (MantineProvider, ModalsProvider, Notifications, theme bridge) is already wired in Phase I. **Do not** re-do framework setup. **Do not** remove any shared dependency (`bootstrap`, `react-hook-form`, `@tanstack/react-table`, `react-notifications-component`) — those are dropped in Phase III after every page is migrated.

If you are setting up the framework for the first time, this is **not** the right skill — see the plan file or CLAUDE.md.

## Mantine references

Always pull current docs before guessing component props:
1. **MCP server** (project-scoped, configured in `.mcp.json`): `mantine` — exposes `list_items`, `get_item_doc`, `get_item_props`, `search_docs`. Use first.
2. **Compact index**: https://mantine.dev/llms.txt
3. **Full reference**: https://mantine.dev/llms-full.txt (only if the compact index is missing the component)

## Decision tree

Before editing, identify what kind of page this is. Pages cluster into:
- **Forms-heavy** (login, user-profile, manage-users, record/type editors, edge-type editor, form editor): biggest churn is `useForm` migration.
- **List/table** (records, workflow-executions, workflow-tasks, notifications, bus-watcher): biggest churn is `<DataTable>` migration.
- **Detail / mixed** (record-detail, execution-detail, dynamic-page): both forms and tables, plus modals.
- **Dashboard** (home/MyTasksPanel, WatchedRecordsPanel, TeamTasksPanel): cards + small tables.
- **Shell** (NavMenu, AgentSidebar, PreferencesModal): defer to Phase III.

## Steps

### 1. Inventory the page

Open the page's `.tsx` file and grep for everything that touches the legacy stack:

- `bootstrap` import, `Modal` from bootstrap, `data-bs-*` attributes
- `react-hook-form`: `useForm`, `register`, `Controller`, `formState`
- `@hookform/resolvers/zod`: `zodResolver`
- `@tanstack/react-table`: `useReactTable`, `flexRender`, `ColumnDef`
- `react-notifications-component`: `Store.addNotification`
- Bootstrap classes: `btn`, `btn-*`, `form-control`, `form-select`, `row`, `col-*`, `card`, `panel`, `page-header`, `dropdown`, `nav-tabs`
- ColorAdmin shell classes: `app-content`, `app-without-*`

Make a punch list. Estimate scope before starting — if a page has 3+ tables AND 3+ forms it may be worth splitting into multiple PRs.

### 2. Forms — RHF + Zod → @mantine/form

Replace each form's runtime. The Zod schema **stays** — only the form library changes.

**Before:**
```tsx
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

const { register, handleSubmit, formState: { errors } } = useForm({
  resolver: zodResolver(schema)
});

<form onSubmit={handleSubmit(onSubmit)}>
  <input className="form-control" {...register("email")} />
  {errors.email && <div className="invalid-feedback">{errors.email.message}</div>}
</form>
```

**After:**
```tsx
import { useForm } from "@mantine/form";
import { TextInput, Button } from "@mantine/core";

const form = useForm({
  initialValues: { email: "" },
  validate: zodResolver(schema)  // @mantine/form supports zod resolver via @mantine/form's zodResolver
});

<form onSubmit={form.onSubmit(onSubmit)}>
  <TextInput label="Email" {...form.getInputProps("email")} />
  <Button type="submit">Submit</Button>
</form>
```

Notes:
- `@mantine/form` exports its own `zodResolver` — import it from `mantine-form-zod-resolver` (a separate package). Install only when needed.
- For nested fields, use dot-notation: `form.getInputProps("user.email")`.
- For arrays, use `form.insertListItem`, `form.removeListItem`, `form.reorderListItem`.
- Drop the `@hookform/resolvers` import on this page (but leave it in package.json — other pages still use it).

### 3. Tables — @tanstack/react-table → mantine-datatable

Replace `useReactTable` + manual `<table>` markup with `<DataTable>`. Map columns 1:1. Move sort/filter/pagination state into DataTable props.

**Before:**
```tsx
import { useReactTable, getCoreRowModel, flexRender, ColumnDef } from "@tanstack/react-table";

const columns: ColumnDef<Row>[] = [
  { accessorKey: "name", header: "Name" },
  { accessorKey: "status", header: "Status" }
];
const table = useReactTable({ data, columns, getCoreRowModel: getCoreRowModel() });
// ... render <table className="table table-bordered">
```

**After:**
```tsx
import { DataTable, type DataTableSortStatus } from "mantine-datatable";

<DataTable
  withTableBorder
  borderRadius="sm"
  striped
  highlightOnHover
  records={data}
  columns={[
    { accessor: "name", sortable: true },
    { accessor: "status", sortable: true }
  ]}
  sortStatus={sortStatus}
  onSortStatusChange={setSortStatus}
/>
```

For pagination, use the `totalRecords`, `recordsPerPage`, `page`, `onPageChange` props. For server-side data, set `fetching` to a loading boolean.

### 4. Modals — Bootstrap → @mantine/core or @mantine/modals

- **Static modal** (open/close via local state): use `<Modal>` from `@mantine/core`.
- **Confirm dialog**: use `modals.openConfirmModal({ title, children, labels, onConfirm })` from `@mantine/modals`.
- **Programmatic modal**: use `modals.open({ title, children })`.

Drop all `data-bs-toggle="modal"`, `data-bs-target="#x"`, and `bootstrap.Modal` instances. Remove the `import "bootstrap"` line at the top of the file (the global side-effect import in `main.tsx` keeps it loaded for unmigrated pages).

### 5. Notifications — react-notifications-component → @mantine/notifications

**Before:** `Store.addNotification({ title, message, type, container, ... })`

**After:** `notifications.show({ title, message, color: "green" })`

Color mapping: `success` → `green`, `info` → `blue`, `warning` → `yellow`, `danger` → `red`.

### 6. Icons — keep FontAwesome unless adding new icons

Don't sweep `fa fa-*` to Mantine icons. Only when the page reaches for a NEW icon, install `@tabler/icons-react` (Mantine's recommended icon set) and use it for the new icon. Existing FA icons stay until Phase III.

### 7. Layout primitives — `row`/`col-*` → `<Grid>`/`<SimpleGrid>`/`<Group>`/`<Stack>`

Rewrite layout-critical sections only. Quick map:
- `<div className="row">` + `col-*` → `<Grid>` + `<Grid.Col span={6}>`
- Equal columns → `<SimpleGrid cols={3}>`
- Horizontal stack → `<Group>`
- Vertical stack → `<Stack>`
- Spacer → `<Space h="md">` or `<Divider />`
- Centered container → `<Container size="md">`

For **inline** Bootstrap utility classes that aren't structural (e.g. `mb-3`, `text-center` on a label), don't migrate — leave the classes alone. The unlayered ColorAdmin/Bootstrap CSS still applies.

### 8. Buttons, inputs, badges — replace as you encounter them

- `<button className="btn btn-primary">` → `<Button>`
- `<button className="btn btn-secondary">` → `<Button variant="default">`
- `<button className="btn btn-danger">` → `<Button color="red">`
- `<input className="form-control">` → `<TextInput>`
- `<select className="form-select">` → `<Select data={...}>`
- `<span className="badge bg-success">` → `<Badge color="green">`

### 9. Verify the migrated page

Mandatory before merge:

1. **Type-check**: `cd src/AutoNate.Spa && npm run type-check`. Must pass.
2. **Dev server**: `npm run dev`. Watch console — must be clean.
3. **Playwright smoke** (use the Playwright MCP):
   - `mcp__playwright__browser_navigate` to the page URL
   - `mcp__playwright__browser_snapshot` to confirm the structure renders
   - `mcp__playwright__browser_click` every interactive element on the page (buttons, modal triggers, sort headers, form submits)
   - `mcp__playwright__browser_console_messages` — must show zero errors
4. **Theme parity**:
   - Toggle dark mode via `/__mantine_test`'s color-scheme button → confirm the migrated page renders correctly in both light + dark.
   - Change `primaryAccentColor` in admin SiteAppearance → confirm migrated buttons reflect the new accent.
5. **Visual diff (optional but recommended)**: `mcp__playwright__browser_take_screenshot` before and after the migration PR. Diff manually.
6. **Verify unmigrated pages still render** — open at least one untouched page after the migration to confirm no global regression.

### 10. PR checklist

- [ ] Page's bootstrap/RHF/tanstack-table/RNC imports removed.
- [ ] No `bootstrap.Modal`, `data-bs-*`, or `useReactTable` calls remain in the page.
- [ ] `npm run type-check` passes.
- [ ] Playwright smoke recorded in PR description (or screenshots attached).
- [ ] Light + dark mode both render.
- [ ] One unmigrated reference page still works.

### Pages NOT to touch via this skill

- `src/shell/AppShell.tsx`, `src/shell/AuthShell.tsx`, `src/shell/NavMenu.tsx` — the shell itself migrates in Phase III.
- `src/agent/AgentSidebar.tsx` — Phase III.
- `src/preferences/PreferencesModal.tsx` — Phase III, after the shell.
- The BPMN modeler page (`src/pages/workflow/WorkflowStudio.tsx`) — late Phase II, only after the rest is done, because its CSS is sensitive.
