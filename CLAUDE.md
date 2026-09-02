# Auton8

ASP.NET Core backend with a React 19 + Vite + TypeScript SPA at `src/AutoNate.Spa/`.

## Naming

The product is **Auton8**; the code is **AutoNate**. User-facing strings say
Auton8 — site name, page copy, the assistant's own identity, document titles.
Internal identifiers stay AutoNate: namespaces, assembly and project names,
`AutoNateDbContext`, `AUTONATE_*` environment variables, `X-AutoNate-*`
headers, localStorage keys, the `autonate_datastores` schema and `AutoNate`
database, the `autonate.web` event `sourceAppId`, the plugin ABI
(`IAutoNatePlugin`, `AutoNate.Plugin.Abstractions`), the `.docx` markers
`AUTONATE_BINDING` / `AUTONATE_TABLE_BINDING`, the BPMN namespace
`http://autonate.dev/workflows` and `${autonateBehaviorDelegate}`, and the
DataProtection purposes `AutoNate.ExternalConnections.v1` /
`AutoNate.Plugins.RolePassword.v1`.

That split is deliberate. Renaming the DataProtection purposes makes every
stored provider secret undecryptable; renaming the `.docx` markers orphans
every bound document; renaming the plugin ABI breaks third-party plugins. Do
not "tidy" them. When adding a user-facing string, write Auton8.

## Mantine v9

The SPA was migrated from ColorAdmin v5 (Bootstrap 5 admin theme, paid license) to **Mantine v9**. Mantine is the sole UI framework — there is no Bootstrap or ColorAdmin SCSS left in the bundle. A small `src/AutoNate.Spa/src/widgets.css` carries the few custom widget styles that survived (ManageUsers avatars / status pills, `.row-archived`, `.notification-unread`).

### Mantine references for AI agents

- **Authoritative API + components**: https://mantine.dev/llms.txt
- **Full reference (large)**: https://mantine.dev/llms-full.txt
- Pull `llms.txt` first; fall back to `llms-full.txt` only for components missing from the short list.
- The `mantine` MCP server (configured in `.mcp.json` at repo root) exposes `list_items`, `get_item_doc`, `get_item_props`, `search_docs`. Prefer it over guessing component props.
- Forms use `@mantine/form` with `mantine-form-zod-resolver` for Zod-backed validation. Tables go through `src/components/data-table/DataTable.tsx`, a thin wrapper around `mantine-datatable` that re-exports a `DataTableColumn<T>` type modeled on the subset of `ColumnDef` that consumers used.

### What is still in the dep graph (and why)

- `@fortawesome/fontawesome-free` — every interactive `<i className="fa fa-*">` glyph the SPA renders. There is no `bootstrap-icons` — any `bi-*` class is dead, replace with the equivalent FA name (e.g. `bi-play-fill` → `fa-play`, `bi-info-circle` → `fa-circle-info`).

### Theming bridge

`SiteAppearance` (admin-configured site theme) is the single source of truth. `applySiteAppearanceToDocument` in `src/AutoNate.Spa/src/lib/siteAppearance.ts` writes `--mantine-*` vars (for Mantine widgets) plus `--app-*` vars (for header / top-menu / sidebar chrome used by `shell/headerStyles.ts` and the `SiteAppearance` admin preview). It also infers `data-mantine-color-scheme` from `surfaceBg` luminance. `MantineRoot` in `src/AutoNate.Spa/src/providers/MantineRoot.tsx` builds a static module-level Mantine theme; the live brand color flows through CSS vars to avoid re-render loops.

### Shell

The app shell at `src/AutoNate.Spa/src/shell/AppShell.tsx` uses Mantine `<AppShell>` with `<AppShell.Header>` wrapping `NavMenu` and `<AppShell.Main>` wrapping page content. The right-side AI chatbot (`AgentSidebar`) stays `position: fixed` because its overlay/fill × over-header/under-header modes don't map cleanly onto `<AppShell.Aside>`.

## n8SDLC project

This project is managed by the n8SDLC workflow (GitHub Issues = the plan; `/n8-stat` shows where things stand). If a change made in this session deviates from what planned issues assume — different library, provider, architecture, dropped/added scope, or amending a declared invariant below — do two things before finishing:
1. Append an `## Ad-hoc` entry to `.n8/decisions.md` (format documented in that file's header) naming the change, the why, and the milestones/issues likely affected.
2. Tell the user which future milestones may now have stale plans and suggest running `/n8-replan`.

Whole-codebase audits run via `/n8-audit`; the AutoNate-specific checklists (security, authorization, performance + hot-path inventory, stability, cleanup, 508) live in `.n8/memory/audit-*.md` and replace the former `/audit` project skills.
