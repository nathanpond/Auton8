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

### Project invariants

Load-bearing constraints. No story may breach one without an explicit
conversation; `/n8-exec` treats an apparent breach as a blocker, and
`/n8-audit` checks the honor-system ones and hunts weakened guards. Amending an
invariant is a user decision — log it as an `## Ad-hoc` entry in
`.n8/decisions.md`.

1. **No credential ever ships in the repository.** Configuring nothing creates
   nothing — no default password, no seeded user. *(test-enforced:
   `BootstrapAdminTests`)*
2. **The plugin ABI's assembly identity is pinned.**
   `AutoNate.Plugin.Abstractions` stays `AssemblyVersion 1.0.0.0`; it must not
   follow the product version. Moving it breaks every already-built
   third-party plugin, and the symptom is a misleading "type not found".
   *(test-enforced: `PluginAbiVersionTests`)*
3. **Every endpoint carries an explicit authorization decision.** A route with
   no gate fails the suite, and so does a gate wired to the wrong
   `(kind, action)`. *(test-enforced: `AuthorizationGatePresenceTests`,
   `KindGateEnforcementTests`)*
4. **The do-not-rename identifiers stay put** — the DataProtection purposes,
   the `.docx` binding markers, the BPMN namespace and delegate expression, the
   schema and database names, and the plugin ABI type names. Renaming any of
   them destroys existing data. See the Naming section above for the list.
   *(honor-system — a guard test is planned in the CI milestone)*
5. **Every published port in a shipped compose file binds to loopback.** The
   stack ships known credentials and an unauthenticated NATS, so a `0.0.0.0`
   bind puts a writable database on whatever network the machine is attached
   to. A service that deliberately mimics an out-of-network dependency — a
   Keycloak instance standing in for a real IdP, say — may be excepted, but the
   exception carries a written reason next to the port it applies to, so it is
   impossible to make silently. *(guard: #50 — test-enforced once that lands)*

Two more guards exist and should not be weakened, though they are not on the
list above: the jsx-a11y error gate in `npm run lint`, and
`RoleCreationRaceTests`, which pins that cluster-wide object creation never
check-then-acts.

Whole-codebase audits run via `/n8-audit`; the AutoNate-specific checklists (security, authorization, performance + hot-path inventory, stability, cleanup, 508) live in `.n8/memory/audit-*.md` and replace the former `/audit` project skills.
