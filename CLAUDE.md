# AutoNate

ASP.NET Core backend with a React 19 + Vite + TypeScript SPA at `src/AutoNate.Spa/`.

## Mantine v9

The SPA was migrated from ColorAdmin v5 (Bootstrap 5 admin theme, paid license) to **Mantine v9**. Mantine is the sole UI framework — there is no Bootstrap or ColorAdmin SCSS left in the bundle. A small `src/AutoNate.Spa/src/widgets.css` carries the few custom widget styles that survived (ManageUsers avatars / status pills, `.row-archived`, `.notification-unread`).

### Mantine references for AI agents

- **Authoritative API + components**: https://mantine.dev/llms.txt
- **Full reference (large)**: https://mantine.dev/llms-full.txt
- Pull `llms.txt` first; fall back to `llms-full.txt` only for components missing from the short list.
- The `mantine` MCP server (configured in `.mcp.json` at repo root) exposes `list_items`, `get_item_doc`, `get_item_props`, `search_docs`. Prefer it over guessing component props.
- Migration playbook for any remaining migration work (e.g. swapping `react-hook-form` for `@mantine/form` on a per-page basis): invoke skill `mantine-page-migration`.

### What is still in the dep graph (and why)

- `@fortawesome/fontawesome-free` — every interactive `<i className="fa fa-*">` glyph the SPA renders. There is no `bootstrap-icons` — any `bi-*` class is dead, replace with the equivalent FA name (e.g. `bi-play-fill` → `fa-play`, `bi-info-circle` → `fa-circle-info`).
- `react-hook-form`, `@hookform/resolvers`, `@tanstack/react-table` — still load-bearing for ~20 forms / tables (`RecordForm`, `ManageUsers` modals, the `DataTable` wrapper's tanstack-table column shim, etc.). Removing them is a port-to-`@mantine/form` job, not a delete; deferred.

### Theming bridge

`SiteAppearance` (admin-configured site theme) is the single source of truth. `applySiteAppearanceToDocument` in `src/AutoNate.Spa/src/lib/siteAppearance.ts` writes `--mantine-*` vars (for Mantine widgets) plus `--app-*` vars (for header / top-menu / sidebar chrome used by `shell/headerStyles.ts` and the `SiteAppearance` admin preview). It also infers `data-mantine-color-scheme` from `surfaceBg` luminance. `MantineRoot` in `src/AutoNate.Spa/src/providers/MantineRoot.tsx` builds a static module-level Mantine theme; the live brand color flows through CSS vars to avoid re-render loops.

### Shell

The app shell at `src/AutoNate.Spa/src/shell/AppShell.tsx` uses Mantine `<AppShell>` with `<AppShell.Header>` wrapping `NavMenu` and `<AppShell.Main>` wrapping page content. The right-side AI chatbot (`AgentSidebar`) stays `position: fixed` because its overlay/fill × over-header/under-header modes don't map cleanly onto `<AppShell.Aside>`.
