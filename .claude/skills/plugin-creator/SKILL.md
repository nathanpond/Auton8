---
name: plugin-creator
description: Use when creating a new AutoNate plugin or extending an existing one. Covers the project skeleton under `/plugins`, the `IAutoNatePlugin` contract (Configure + Cleanup), hook subscriptions (actions / filters, including the audit-event firehose and the per-plugin data hook), the per-plugin Postgres schema and SQL migrations, the menu-item helpers (`IPluginMenus`), the workflow-behavior helper (`IPluginBehaviors`), the `PageTemplates/` page-template manifest, packaging, and the type-identity pitfalls of the host's collectible AssemblyLoadContext. Invoke whenever the user asks to "build a plugin", "add a plugin that does X", "create an extension", or modify code under `/plugins/<Name>/`.
---

# Building an AutoNate plugin

A plugin is a .NET 10 class library under `plugins/<Name>/` that the host loads at runtime into its own collectible `AssemblyLoadContext`. The plugin gets these extension surfaces, all reached through `IPluginContext`:

- **Hooks** — register filter/action callbacks on host hook points (`context.Hooks`).
- **Per-plugin Postgres schema** — read/write its own tables, read app + other plugins' tables (`context.Data`).
- **Menu items** — append to any of the host's menus, including JSX/HTML pages (`context.Menus`).
- **Page templates** — ship reusable JSX page templates under `PageTemplates/` that admins pick from when creating menu items (`templates` map in `plugin.json`).
- **Workflow behaviors** — contribute `IWorkflowBehavior` implementations the studio surfaces in the service-task picker (`context.Behaviors`).
- **Projections** — register scheduled background tick jobs that show up on `/api/admin/projections` alongside host projections, with the same pause/resume/health surface (`context.Projections`). See the separate `add-projection` skill for the projection-side details. Caveat: registrations from `Configure()` only begin draining after the next host restart (dynamic registration is a Phase 5 enhancement).

References:
- `plugins/HelloPlugin/` — minimal worked example. Read first when in doubt.
- `plugins/Auditor/` — exercises the more advanced surfaces: `Cleanup`, `HookPoints.AuditEventPublished`, `HookPoints.PluginDataHookFor`, and a `PageTemplates/` template with manifest metadata + thumbnail.

## Critical files

- `src/AutoNate.Plugin.Abstractions/IAutoNatePlugin.cs` — the contract (`Configure` + `Cleanup`). Read-only; don't change unless you mean to change it for every plugin.
- `src/AutoNate.Plugin.Abstractions/IPluginContext.cs` — what the host hands you (`Code`, `SchemaName`, `Hooks`, `Data`, `Menus`, `Behaviors`, `Projections`, `HostServices`).
- `src/AutoNate.Plugin.Abstractions/IPluginDataAccess.cs` — Dapper-style data API.
- `src/AutoNate.Plugin.Abstractions/IPluginMenus.cs` — menu helpers (incl. `RemoveAll` / `RemoveMenuItem` for `Cleanup`).
- `src/AutoNate.Plugin.Abstractions/IPluginBehaviors.cs` — workflow-behavior registration.
- `src/AutoNate.Plugin.Abstractions/IPluginProjections.cs` — `RegisterScheduled(name, interval, tick)` for plugin-contributed projections; see also the `add-projection` skill.
- `src/AutoNate.Plugin.Abstractions/HookPoints.cs` — canonical hook constants.
- `src/AutoNate.Plugin.Abstractions/AuditEventNotification.cs` — payload received on the `HookPoints.AuditEventPublished` hook.
- `src/AutoNate.Plugin.Abstractions/PluginDataRequest.cs` — request/response shape for `HookPoints.PluginDataHookFor(code)`.
- `src/AutoNate.Web/Plugins/PluginAssemblyLoadContext.cs` — `SharedAssemblies` set; matters for type identity.
- `src/AutoNate.Web/Plugins/PluginRuntime.cs` — host-side ingestion of `PageTemplates/*.template` and the `templates` map.
- `plugins/Directory.Build.props` / `plugins/Directory.Build.targets` — shared MSBuild settings + zip target (zip includes `migrations/*.sql` and `PageTemplates/*`).
- `plugins/HelloPlugin/` — minimal worked example.
- `plugins/Auditor/` — page templates, audit-event hook, per-plugin data hook, and `Cleanup`.

## Steps

### 1. Scaffold the project

```
plugins/
  <PluginName>/
    <PluginName>.csproj          # empty; settings come from Directory.Build.props
    plugin.json                  # manifest (incl. optional `templates` map)
    <PluginName>.cs              # IAutoNatePlugin implementation (Configure + optional Cleanup)
    migrations/                  # optional, only if the plugin has its own tables
      001_init.sql
    PageTemplates/               # optional, only if the plugin ships JSX page templates
      MyTemplate.template        # JSX file; key = filename stem
      MyTemplate.png             # optional thumbnail (sibling of the .template file)
```

`<PluginName>.csproj` body:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <!-- Common settings (TargetFramework, abstractions reference, zip target)
         come from /plugins/Directory.Build.props and Directory.Build.targets. -->
</Project>
```

`plugin.json`:

```json
{
  "name": "<PluginName>",
  "version": "1.0.0",
  "entryAssembly": "<PluginName>.dll",
  "entryType": "AutoNate.Plugins.<PluginName>.<PluginName>",
  "templates": {
    "MyTemplate": {
      "name": "My Template",
      "description": "What this template renders.",
      "category": "Plugins"
    }
  }
}
```

- `entryType` is optional — the loader scans for a single `IAutoNatePlugin` implementation if omitted. Specify it when you have multiple candidate types.
- `templates` is optional. Each key must match a `PageTemplates/<key>.template` filename stem. Keys with no entry in this map still register as page templates (display name falls back to the stem); the entries here just give the SPA picker a friendlier name/description/category. See "Add page templates" below.

### 2. Implement IAutoNatePlugin

```csharp
using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoNate.Plugins.<PluginName>;

public sealed class <PluginName> : IAutoNatePlugin
{
    public string Name => "<PluginName>";
    public string Version => "1.0.0";

    public void Configure(IPluginContext context)
    {
        // Registration only. No long-running work here.
        // The host has already cleared any prior menu items, behaviors, and
        // page templates this plugin owned, so AddXxx() calls are correct on
        // every enable.
    }

    // Optional. Default is a no-op (the interface ships an empty body).
    // Runs once when the host is about to DELETE the plugin, BEFORE the per-
    // plugin schema/role, on-disk files, and `plugins` row are torn down.
    // Runs even if the plugin was disabled at the time of delete: the host
    // loads the assembly into a fresh ALC just for this call.
    public void Cleanup(IPluginContext context)
    {
        // Override only when there's state OUTSIDE the host's automatic
        // cleanup paths to remove. The host already (1) FK-CASCADE-removes
        // every menu_items / page_templates / workflow_behavior_registrations
        // row tagged with this plugin's id, (2) DROP SCHEMA CASCADE the
        // entire plg_<code> schema, and (3) deletes the plugin's bin folder.
        // So a no-op default is correct for plugins that don't reach beyond
        // those.
    }
}
```

Rules for `Configure`:

- It runs **once per enable**. Treat it like a synchronous init pass.
- Stash anything you need to keep (`context.Data`, the logger factory) in fields. Don't expose `IPluginContext` itself on a public surface.
- Don't block. If you need background work, spawn a `Task.Run` or own a long-lived service.

Rules for `Cleanup`:

- Runs **once at delete time**, never on disable. The host gives you a fresh `IPluginContext` (new schema connection, new menu helpers).
- Any exception thrown is logged and swallowed; deletion still proceeds.
- Use it to sweep things the host can't see: third-party state the plugin owns, files written outside `plg_<code>` or the plugin's bin folder, etc.
- For finer-grained menu cleanup than the FK CASCADE (e.g. removing trailing separators a sibling left behind), use `context.Menus.RemoveMenuItem(id)` (ownership-checked) and `context.Menus.RemoveAll()` (sweeps every row this plugin owned).

### 3. Add hook subscriptions

`context.Hooks` is an `IHookRegistrar`. Two kinds of subscriptions:

```csharp
// Action: fire-and-forget callback.
context.Hooks.AddActionAsync("autonate.something", priority: 100, async (args, ct) =>
{
    // args is a positional object[] — cast args[0], args[1] etc. to the
    // documented payload types for the hook point.
    await DoWorkAsync(args, ct);
});

// Filter: receive value, return modified value.
context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
    HookPoints.AuthorizeAuthorize, priority: 100, async (current, args, ct) =>
    {
        // current is the in-flight value being filtered; args carries the
        // positional context object (cast args[0] to the hook's payload type).
        if (ShouldDeny(current))
        {
            return current with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = AuthEffectDto.Deny,
                    Reason = "<plugin-name>"
                }
            };
        }
        return current;
    });
```

Always use the named constants in `HookPoints`. String typos won't error — they'll silently never fire. Canonical set today:

| Constant | Kind | Payload | Purpose |
|---|---|---|---|
| `HookPoints.AuthorizeAuthorize` | filter | `AuthorizeFilterContext` | Inject deny / override decisions into the host authorizer. |
| `HookPoints.AuditEventPublished` | action | `AuditEventNotification` | Firehose of every audit-grade event the host emits — fires after the envelope has been enqueued to the outbox. Subscribers run on the request thread, so push I/O off the hot path (`Task.Run` or queue to a per-plugin worker). |
| `HookPoints.PluginDataHookFor(context.Code)` | filter | `PluginDataRequest` → `PluginDataResponse` | Per-plugin namespaced hook backing `GET /api/admin/plugins/by-code/{code}/data/{view}`. The plugin owns the namespace (one hook per code), so a handler doesn't need to filter for its own code; just inspect `req.View` and return a `PluginDataResponse` (default-construct returns 404). Used to feed JSX page templates the plugin ships under `PageTemplates/`. |

`AuditEventPublished` example (the Auditor plugin):

```csharp
context.Hooks.AddActionAsync(
    HookPoints.AuditEventPublished, priority: 100, async (args, ct) =>
    {
        if (args.Length == 0 || args[0] is not AuditEventNotification notification) return;
        await _state.HandleAsync(notification, ct);
    });
```

`PluginDataHookFor` example (per-plugin REST endpoint without registering a route):

```csharp
context.Hooks.AddFilterAsync<PluginDataResponse>(
    HookPoints.PluginDataHookFor(context.Code), priority: 100,
    async (current, args, ct) =>
    {
        if (args.Length == 0 || args[0] is not PluginDataRequest req) return current;
        if (req.View != "audit-log") return current; // not ours; let the default 404 stand
        return await _state.QueryAuditLogAsync(req, ct);
    });
```

A SPA page template addresses this hook with `api.get("/api/admin/plugins/by-code/{{pluginCode}}/data/audit-log")` — the host substitutes `{{pluginCode}}` at template registration time (see step 6).

### 4. Add per-plugin data (optional)

If the plugin needs to persist anything, the host has already provisioned a Postgres schema and role for it. Just write SQL migrations and use `context.Data`.

**Migration files** at `plugins/<PluginName>/migrations/*.sql`:

```sql
-- 001_init.sql
CREATE TABLE IF NOT EXISTS widgets (
    id BIGSERIAL PRIMARY KEY,
    label TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

- Filenames are sorted lexically — use a zero-padded numeric prefix (`001_…`, `002_…`).
- Each file runs in its own transaction.
- **Files are immutable once shipped.** Ship a new file, never edit a tracked one.
- Use `IF NOT EXISTS` defensively even though the host tracks applied migrations.
- Don't reach into `public` from a migration — the plugin role can't write to app tables.

The shared `Directory.Build.props` auto-copies `migrations/*.sql` into the zip. No extra MSBuild needed.

**Reads/writes inside hook handlers** via `context.Data`:

```csharp
await context.Data.ExecuteAsync(
    "INSERT INTO widgets (label) VALUES (@label);",
    new { label }, ct);

var rows = await context.Data.QueryAsync<Widget>(
    "SELECT id AS Id, label AS Label FROM widgets WHERE label = @label;",
    new { label }, ct);

// App table reads — qualified or unqualified (search_path includes public):
var users = await context.Data.QuerySingleOrDefaultAsync<long>(
    "SELECT COUNT(*) FROM public.local_users;", ct: ct);
```

The plugin's `NpgsqlDataSource` connects as `plg_<code>` with `search_path = plg_<code>,public`. Any write to `public.*` will throw `PostgresException` with `SqlState = "42501"` — that's the database refusing the privilege. To mutate app data, register a hook on the relevant filter point and let the host apply your change.

**Cross-plugin reads**: use the qualified schema name. If you need another plugin's code, look it up:

```csharp
var otherCode = await context.Data.QuerySingleOrDefaultAsync<string>(
    "SELECT code FROM public.plugins WHERE name = @name AND status = 1;",
    new { name = "OtherPlugin" }, ct);
if (otherCode is null) return;

var sql = $"SELECT * FROM plg_{otherCode}.widgets;"; // safe: code is [a-z][a-z0-9]{7}
```

### 5. Add menu items (optional)

`context.Menus` exposes four helpers (all sync — match `Configure`'s signature):

| Helper | When to use |
|---|---|
| `AddPluginMenuItem(NewMenuItem)` | Single item under the "Plugins" group in Site Configuration. |
| `AddSiteConfigGroup(name, icon, children)` | A whole new top-level group in Site Configuration with children in one shot. |
| `AddMenuItem(menuKey, parentId, item)` | Anything else — any menu, any parent (`null` = top-level). |
| `ListMenus()` | Snapshot every menu + items, e.g. to find an existing parent's id. |

`NewMenuItem` shape:

```csharp
new NewMenuItem(
    DisplayName: "<text>",
    ItemType:    "<type>",       // see table below
    Icon:        "fa fa-cog",    // optional Font Awesome class
    Config:      new { /* JSON-serialized */ },
    SortOrder:   null,           // null = append after siblings
    IsVisible:   true);
```

Item types and config shapes:

| ItemType | Config | Behaviour |
|---|---|---|
| `"template"` | `{ templateKey, path? }` | Bind to a built-in React component. |
| `"page"` | `{ path, content, contentType: "html"\|"jsx" }` | Render content shipped by the plugin. |
| `"link"` | `{ href }` | External URL. |
| `"action"` | `{ action }` | Predefined client action. |
| `"separator"` | `{}` | Visual divider. |
| `"group"` | `{ startsExpanded?, dynamicChildren? }` | Container for children. |

**JSX content** (recommended for plugin pages):

The `content` string must define `function Page() { return <jsx/>; }`. The host compiles it via Sucrase at runtime. Available in scope: `React`, `useState`, `useEffect`, `useMemo`, `useCallback`, `useRef`, `navigate` (react-router), `Link`, `NavLink`, `api` (typed JSON HTTP client), `logout`. TypeScript is also accepted.

Path convention: `/admin/config/plugins/{context.Code}/<key>` so the page mounts inside the Site Configuration sidebar shell. Anything else mounts in the top-level app shell only.

```csharp
context.Menus.AddPluginMenuItem(new NewMenuItem(
    DisplayName: "<PluginName> Settings",
    ItemType:    "page",
    Icon:        "fa fa-gear",
    Config: new {
        path        = $"/admin/config/plugins/{context.Code}/settings",
        contentType = "jsx",
        content     = """
            function Page() {
              return (
                <div className="page-head">
                  <h1>Hello from the plugin</h1>
                </div>
              );
            }
        """,
    }));
```

**Lifecycle ownership**: every row added by these helpers carries `created_by_plugin_id = <plugin id>`. The host:
- wipes all such rows **before** every `Configure()` (clean slate),
- wipes them on disable (items vanish from sidebar),
- relies on FK `ON DELETE CASCADE` on delete.

Net effect: just call `AddXxx()` unconditionally inside `Configure()`. No idempotency keys, no existence checks.

### 6. Add page templates (optional)

A page template is a reusable JSX page admins can pick when they create a `template`-typed menu item. Plugins ship them by dropping JSX files into `PageTemplates/`:

```
plugins/<PluginName>/
  PageTemplates/
    AuditLog.template     # JSX; the filename stem ("AuditLog") becomes the templateKey
    AuditLog.png          # optional thumbnail; sibling of the .template file
```

The host reads the folder on every enable and UPSERTs each `*.template` file into `public.page_templates` as a row tagged `created_by_plugin_id = <plugin id>`. Templates the plugin used to ship but no longer carries are deleted on the next enable. Don't try to write `page_templates` rows yourself — the helper is implicit through the folder convention.

The file's contents are JSX (Sucrase-compiled at render time). Same scope as menu-item JSX content (`React`, `useState`, `useEffect`, `useMemo`, `useCallback`, `useRef`, `navigate`, `Link`, `NavLink`, `api`, `logout`). Two placeholders are substituted into the source at registration time so the JSX can address its host endpoints without knowing the plugin's randomly-assigned code at build time:

| Placeholder | Substituted with |
|---|---|
| `{{pluginCode}}` | The plugin's 8-char code, e.g. `a1b2c3d4`. |
| `{{pluginId}}`   | The plugin's UUID. |

A template that wants to call the plugin's `PluginDataHookFor` hook does it like this:

```jsx
function Page() {
  const [rows, setRows] = useState([]);
  useEffect(() => {
    api.get("/api/admin/plugins/by-code/{{pluginCode}}/data/audit-log",
            { params: { limit: 50 } })
       .then(res => setRows(res.data.rows));
  }, []);
  return <table>{/* ... */}</table>;
}
```

Per-template metadata (display name, description, category, thumbnail) is sourced from `plugin.json`'s `templates` map plus the optional sibling `<stem>.png`. A template with no entry in the map still registers; it just shows up in the SPA picker keyed by its stem with no description / default category.

Conflict handling: if the same `templateKey` is already owned by the host or another plugin, the host **logs a warning and skips** the conflicting file — yours doesn't trample the existing row. Pick stems that namespace your plugin (e.g. `Auditor.AuditLog` rather than just `AuditLog`) when you suspect collisions.

Lifecycle: identical to menu items. The host wipes plugin-owned `page_templates` rows on disable, the FK CASCADE handles delete. Your `Configure()` doesn't have to do anything for templates — the folder scan is implicit.

### 7. Add workflow behaviors (optional)

`context.Behaviors` registers `IWorkflowBehavior` implementations the workflow studio's service-task picker surfaces:

```csharp
context.Behaviors.Register(new MyBehavior(/* ... */));
```

Same lifecycle as menu items: registrations are tagged with the plugin id, wiped on disable, FK-cascaded on delete. In-flight `ExecuteAsync` calls finish (the ALC stays loaded), but new invocations after disable 404 from the host endpoint, which the Flowable bridge surfaces as a system failure (job retry).

### 8. Build & package

```bash
dotnet build plugins/<PluginName>/<PluginName>.csproj
# → plugins/<PluginName>/dist/<PluginName>.zip
```

The `Directory.Build.targets` post-build step zips the bin output and excludes:

- `*.pdb`
- `AutoNate.Plugin.Abstractions.dll`
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
- `Microsoft.Extensions.Logging.Abstractions.dll`
- `Npgsql.dll`
- `Dapper.dll`

Those five assemblies are loaded by the host and shared into every plugin's ALC for type identity. Shipping local copies inside the zip causes silent type-identity bugs (the cast to `IAutoNatePlugin` fails because the plugin's `IAutoNatePlugin` is a different `Type` than the host's).

If a plugin pulls in a new NuGet that brings *another* assembly the host already loads, extend `SharedAssemblies` in `src/AutoNate.Web/Plugins/PluginAssemblyLoadContext.cs` and the exclusion list in `plugins/Directory.Build.targets` together.

### 9. Upload & enable

Through the admin UI: *Site Configuration → Plugins → Manage Plugins → Upload plugin…* The host:
- generates an 8-char `code`,
- creates `plg_<code>` schema + LOGIN role,
- persists the plugins row with `status = Disabled`.

Then click **Enable**. The host runs migrations, instantiates the plugin, calls `Configure(context)`, and registers everything.

## Verification

Before reporting the plugin done:

1. **Build cleanly** — `dotnet build plugins/<Name>/<Name>.csproj` reports 0 errors and 0 warnings.
2. **Solution still builds** — `dotnet build AutoNate.sln` from the repo root.
3. **SPA still typechecks** — `cd src/AutoNate.Spa && npx tsc -b --force` exits 0.
4. **Tests still green** — `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --no-build` passes.
5. **Smoke** — upload the zip via the admin UI, enable it, exercise the hook the plugin registered, check `psql` for any rows it should have written, confirm any menu items it added show up in the sidebar, and verify any `PageTemplates/` files appear in the SPA template picker.
6. **Cleanup smoke** (only if you overrode `Cleanup`) — install the plugin, then *delete* it from the admin UI and confirm the artifacts your `Cleanup` was supposed to sweep are actually gone (host logs swallow exceptions thrown from `Cleanup`, so a silent failure won't surface here).

## Common mistakes

- **Reaching into `AutoNate.Web`** — plugins reference only `AutoNate.Plugin.Abstractions`. The shared `Directory.Build.props` enforces this with `<Private>false</Private>` on the abstractions reference. Don't fight it.
- **Mutating app data via raw SQL** — the database refuses; you need a host hook. Either find an existing hook in `HookPoints` or add a new one to the host (separate change to `AutoNate.Web`).
- **Mutating the plugin's own table from a place other than a hook** — fine in principle, but `context.Data` only exists during the lifetime of `IPluginContext`. Capture it in a field if you need it later.
- **Editing a shipped migration** — never. Always add a new file with a higher number.
- **Adding a "remove on disable" cleanup for menu items by hand** — unnecessary; the host does it.
- **Idempotency in `Configure()`** — also unnecessary; the host gives you a clean slate.
- **Catching exceptions silently in a hook** — log them. A `try { } catch { }` with no log will hide bugs forever.
- **Hardcoding another plugin's `code`** — codes are random per install. Look the code up by plugin name from `public.plugins`.
- **Putting JSX content in a path outside `/admin/config/*`** — it'll render without the sidebar. Stay under the prefix unless you specifically don't want the shell.
- **Cleaning up menu items inside `Cleanup`** — usually unnecessary. The FK `ON DELETE CASCADE` on the `plugins` row sweeps every menu/template/behavior row this plugin owned. Override `Cleanup` only when there's state OUTSIDE the host's view (third-party services, files written outside `plg_<code>`, etc.). For surgical menu sweeps (e.g. "drop a trailing separator I left under a sibling group"), `context.Menus.RemoveMenuItem(id)` is ownership-checked.
- **Hardcoding the plugin's own code in a `PageTemplates/` JSX file** — won't work; codes are random per install. Use the `{{pluginCode}}` / `{{pluginId}}` placeholders the host substitutes at registration time.
- **Forgetting to filter on `req.View` inside a `PluginDataHookFor` handler** — the hook fires for *every* `/data/{view}` call on this plugin's namespace, not just yours. Always early-return the unmodified `current` (which defaults to `404`) when the view isn't one you handle.
- **Doing I/O on the request thread inside an `AuditEventPublished` handler** — the firehose runs synchronously inside the publish path. Push slow work onto a `Task.Run`, a per-plugin `Channel<T>` worker, or a `BackgroundService`-equivalent the plugin owns. Otherwise every audited request waits on you.
