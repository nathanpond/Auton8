---
name: plugin-creator
description: Use when creating a new AutoNate plugin or extending an existing one. Covers the project skeleton under `/plugins`, the `IAutoNatePlugin` contract, hook subscriptions (actions / filters), the per-plugin Postgres schema and SQL migrations, the menu-item helpers (`IPluginMenus`), packaging, and the type-identity pitfalls of the host's collectible AssemblyLoadContext. Invoke whenever the user asks to "build a plugin", "add a plugin that does X", "create an extension", or modify code under `/plugins/<Name>/`.
---

# Building an AutoNate plugin

A plugin is a .NET 10 class library under `plugins/<Name>/` that the host loads at runtime into its own collectible `AssemblyLoadContext`. The plugin gets three kinds of extension surface, all reached through `IPluginContext`:

- **Hooks** — register filter/action callbacks on host hook points (`context.Hooks`).
- **Per-plugin Postgres schema** — read/write its own tables, read app + other plugins' tables (`context.Data`).
- **Menu items** — append to any of the host's menus, including JSX/HTML pages (`context.Menus`).

The reference is `plugins/HelloPlugin/`. Read it first when in doubt.

## Critical files

- `src/AutoNate.Plugin.Abstractions/IAutoNatePlugin.cs` — the contract (read-only, don't change unless you mean to change it for every plugin).
- `src/AutoNate.Plugin.Abstractions/IPluginContext.cs` — what the host hands you.
- `src/AutoNate.Plugin.Abstractions/IPluginDataAccess.cs` — Dapper-style data API.
- `src/AutoNate.Plugin.Abstractions/IPluginMenus.cs` — menu helpers.
- `src/AutoNate.Plugin.Abstractions/HookPoints.cs` — canonical hook constants.
- `src/AutoNate.Web/Plugins/PluginAssemblyLoadContext.cs` — `SharedAssemblies` set; matters for type identity.
- `plugins/Directory.Build.props` / `plugins/Directory.Build.targets` — shared MSBuild settings + zip target.
- `plugins/HelloPlugin/` — full worked example.

## Steps

### 1. Scaffold the project

```
plugins/
  <PluginName>/
    <PluginName>.csproj          # empty; settings come from Directory.Build.props
    plugin.json                  # manifest
    <PluginName>.cs              # IAutoNatePlugin implementation
    migrations/                  # optional, only if the plugin has its own tables
      001_init.sql
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
  "entryType": "AutoNate.Plugins.<PluginName>.<PluginName>"
}
```

`entryType` is optional — the loader scans for a single `IAutoNatePlugin` implementation if omitted. Specify it when you have multiple candidate types.

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
        // The host has already cleared any prior menu items this plugin
        // owned, so AddXxx() calls are correct on every enable.
    }
}
```

Rules for `Configure`:

- It runs **once per enable**. Treat it like a synchronous init pass.
- Stash anything you need to keep (`context.Data`, the logger factory) in fields. Don't expose `IPluginContext` itself on a public surface.
- Don't block. If you need background work, spawn a `Task.Run` or own a long-lived service.

### 3. Add hook subscriptions

`context.Hooks` is an `IHookRegistrar`. Two kinds of subscriptions:

```csharp
// Action: fire-and-forget callback.
context.Hooks.AddActionAsync("autonate.something", priority: 100, async (ctx, ct) =>
{
    await DoWorkAsync(ctx, ct);
});

// Filter: receive value, return modified value.
context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
    HookPoints.AuthorizeAuthorize, priority: 100, async (ctx, _, ct) =>
    {
        if (ShouldDeny(ctx))
        {
            return ctx with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = AuthEffectDto.Deny,
                    Reason = "<plugin-name>"
                }
            };
        }
        return ctx;
    });
```

Always use the named constants in `HookPoints` (current canonical set: `HookPoints.AuthorizeAuthorize`). String typos won't error — they'll silently never fire.

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

### 6. Build & package

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

### 7. Upload & enable

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
5. **Smoke** — upload the zip via the admin UI, enable it, exercise the hook the plugin registered, check `psql` for any rows it should have written, and confirm any menu items it added show up in the sidebar.

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
