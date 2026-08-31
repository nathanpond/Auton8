// Long-form plugin documentation rendered inside the Site Configuration shell.
// Authored as JSX so React renders it without dangerouslySetInnerHTML.
import { Card, Stack, Title } from "@mantine/core";
import PageHeader from "@/components/PageHeader";

export default function PluginDocumentation() {
  return (
    <>
      <PageHeader
        title="Plugin Documentation"
        description="How AutoNate plugins work, what the host gives them, and the patterns to use when building one."
      />

      <Stack gap="md">
      <Card withBorder shadow="sm">
        <Title order={5} mb="md">
          Contents
        </Title>
          <ul style={{ margin: 0 }}>
            <li><a href="#what-is-a-plugin">What a plugin is</a></li>
            <li><a href="#project-layout">Project layout &amp; manifest</a></li>
            <li><a href="#lifecycle">Lifecycle</a></li>
            <li><a href="#contract">The IAutoNatePlugin contract</a></li>
            <li><a href="#hooks">Hooks: actions and filters</a></li>
            <li><a href="#data">Plugin-owned data &amp; isolation</a></li>
            <li><a href="#migrations">Migrations</a></li>
            <li><a href="#data-access">IPluginDataAccess</a></li>
            <li><a href="#cross-plugin">Cross-plugin reads</a></li>
            <li><a href="#menus">Menu helpers</a></li>
            <li><a href="#page-templates">Page templates</a></li>
            <li><a href="#cleanup">Cleanup routines</a></li>
            <li><a href="#patterns">Patterns &amp; best practices</a></li>
            <li><a href="#packaging">Building &amp; packaging</a></li>
            <li><a href="#hello">Worked example: HelloPlugin</a></li>
            <li><a href="#troubleshooting">Troubleshooting</a></li>
          </ul>
      </Card>

      <Section id="what-is-a-plugin" title="What a plugin is">
        <p>
          A plugin is a self-contained .NET class library that AutoNate loads at
          runtime and lets extend the host without modifying the application.
          Each plugin runs inside its own collectible <code>AssemblyLoadContext</code>{" "}
          so its dependencies stay isolated from other plugins, and it talks to
          the host through a small, stable abstractions surface
          (<code>AutoNate.Plugin.Abstractions</code>).
        </p>
        <p>
          Plugins do <strong>not</strong> reference <code>AutoNate.Web</code>.
          They get their extension points by:
        </p>
        <ul>
          <li>
            <strong>Subscribing to hooks</strong> the host exposes (filters that
            transform a value, actions that fire-and-forget on an event),
          </li>
          <li>
            <strong>Persisting their own data</strong> in a per-plugin Postgres
            schema that only that plugin can write to, and
          </li>
          <li>
            <strong>Adding menu items</strong> to the host&apos;s sidebar — including
            full pages rendered as JSX or HTML.
          </li>
        </ul>
        <p>
          Anything that needs to mutate application data must go through a host
          hook or service. Plugins have read-only SQL access to app tables — the
          database itself enforces this.
        </p>
      </Section>

      <Section id="project-layout" title="Project layout & manifest">
        <p>
          A plugin is an MSBuild project under{" "}
          <code>/plugins/&lt;PluginName&gt;</code>. The shared{" "}
          <code>Directory.Build.props</code> in <code>/plugins</code> wires the
          common settings — target framework, the abstractions reference (with{" "}
          <code>Private=false</code> so type identity unifies across ALCs), and
          the post-build target that zips the output into{" "}
          <code>dist/&lt;PluginName&gt;.zip</code> ready for upload.
        </p>
        <p>The minimum files a plugin ships in its zip:</p>
        <ul>
          <li>
            <code>plugin.json</code> — the manifest, see below.
          </li>
          <li>
            <code>&lt;PluginName&gt;.dll</code> — the assembly that contains a
            class implementing <code>IAutoNatePlugin</code>.
          </li>
          <li>
            Any <code>.deps.json</code> + transitive DLLs the plugin depends on
            (excluding host-shared assemblies — see Packaging below).
          </li>
          <li>
            <em>Optional:</em> a <code>migrations/</code> folder with numbered{" "}
            <code>.sql</code> files (see Migrations).
          </li>
        </ul>
        <h5 style={{ marginTop: 16 }}>plugin.json</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, marginBottom: 8, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)" }}>
{`{
  "name": "HelloPlugin",
  "version": "1.0.0",
  "entryAssembly": "HelloPlugin.dll",
  "entryType": "AutoNate.Plugins.HelloPlugin.HelloPlugin"
}`}
        </pre>
        <ul>
          <li><code>name</code> — display name shown on the Manage Plugins page.</li>
          <li><code>version</code> — semver. Surfaced in the UI; not enforced at load time.</li>
          <li><code>entryAssembly</code> — the DLL filename (must live at the root of the zip).</li>
          <li>
            <code>entryType</code> — <em>optional</em> fully-qualified type name.
            If omitted the loader scans the assembly for a single
            non-abstract type implementing <code>IAutoNatePlugin</code>; specify
            it explicitly when the assembly contains more than one.
          </li>
        </ul>
      </Section>

      <Section id="lifecycle" title="Lifecycle">
        <p>
          The host owns the plugin lifecycle. Each transition is exposed in the
          Manage Plugins admin page; the same transitions also run automatically
          at startup for any plugin row that was previously enabled.
        </p>
        <h5 style={{ marginTop: 16 }}>Upload</h5>
        <ol>
          <li>The zip is validated (size cap, no path traversal, manifest parses).</li>
          <li>Files are extracted to <code>plugins/&lt;PluginId&gt;/</code> on the host.</li>
          <li>
            <strong>The host generates the plugin&apos;s 8-character code</strong>{" "}
            (lowercase, e.g. <code>a1b2c3d4</code>), creates a per-plugin
            Postgres LOGIN role <code>plg_&lt;code&gt;</code> with a random
            password, and creates a schema <code>plg_&lt;code&gt;</code> that
            the role owns.
          </li>
          <li>
            The <code>plugins</code> row is persisted with{" "}
            <code>status = Disabled</code>, the code, and the encrypted role
            password.
          </li>
        </ol>
        <h5 style={{ marginTop: 16 }}>Enable</h5>
        <ol>
          <li>
            Any pending migration files in the plugin&apos;s{" "}
            <code>migrations/</code> folder are applied as the plugin&apos;s role,
            tracked in <code>plg_&lt;code&gt;.__plugin_migrations</code>. A
            failure aborts enable; the plugin row&apos;s <code>last_error</code> is
            populated and status reverts to Disabled.
          </li>
          <li>
            <strong>Any menu items the plugin previously registered are
            cleared</strong> so <code>Configure()</code> starts from a clean
            slate. The plugin&apos;s source code is the source of truth for which
            menu items it owns.
          </li>
          <li>
            A fresh <code>PluginAssemblyLoadContext</code> is created and the
            plugin DLL is loaded.
          </li>
          <li>
            The host instantiates <code>IAutoNatePlugin</code> via{" "}
            <code>Activator.CreateInstance</code> and calls{" "}
            <code>Configure(IPluginContext)</code>. Hook subscriptions and{" "}
            <code>context.Menus.AddXxx()</code> calls happen here.
          </li>
          <li>Hooks the plugin registers are kept in a per-plugin scoped registrar.</li>
        </ol>
        <h5 style={{ marginTop: 16 }}>Disable</h5>
        <p>
          The host revokes every hook the plugin registered, disposes the
          plugin&apos;s pooled <code>NpgsqlDataSource</code>, and{" "}
          <strong>removes every menu item the plugin registered</strong> (rows
          tagged with the plugin&apos;s id via <code>created_by_plugin_id</code>).
          The ALC stays loaded (assemblies cannot be unloaded mid-process safely
          on every platform), but the plugin is inert until the next process
          restart. <strong>Plugin data is preserved</strong> — the schema and
          role are untouched.
        </p>
        <h5 style={{ marginTop: 16 }}>Delete</h5>
        <p>
          Disable runs first, then the host invokes the plugin&apos;s{" "}
          <code>Cleanup(IPluginContext)</code> callback (loading the assembly
          into a fresh ALC if the plugin was disabled at the time of delete —
          see <a href="#cleanup">Cleanup routines</a>). Then the host drops
          the schema with <code>DROP SCHEMA … CASCADE</code> and drops the
          role. Files are removed last; if a Windows file lock blocks file
          removal the row is marked <code>DeletedPending</code> and the file
          delete (only) is retried at the next startup. The schema and role
          are <em>always</em> dropped immediately on the first delete attempt.
          Any menu items and plugin-owned page templates still tagged with the
          plugin&apos;s id are cleaned up by the FK{" "}
          <code>ON DELETE CASCADE</code>.
        </p>
      </Section>

      <Section id="contract" title="The IAutoNatePlugin contract">
        <p>
          Every plugin implements one interface from{" "}
          <code>AutoNate.Plugin.Abstractions</code>:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public interface IAutoNatePlugin
{
    string Name { get; }
    string Version { get; }
    void Configure(IPluginContext context);

    // Optional teardown callback. Default impl is a no-op, so plugins that
    // don't need extra cleanup don't have to override it.
    void Cleanup(IPluginContext context) { }
}`}
        </pre>
        <p>
          <code>Configure</code> is called <em>once</em> per enable. Use it to
          register hooks and menu items. Don&apos;t perform long-running work or
          blocking I/O here — registration only. The host has already cleared
          any prior menu items this plugin owned, so plain{" "}
          <code>AddXxx()</code> calls are correct on every enable.
        </p>
        <p>
          <code>Cleanup</code> is called <em>once</em> when the host is about
          to delete the plugin, before its schema, role, files, and row are
          torn down. The default implementation does nothing — only override
          it when the plugin created artifacts the host doesn&apos;t sweep
          automatically. See <a href="#cleanup">Cleanup routines</a>.
        </p>
        <p>
          The <code>IPluginContext</code> argument carries everything you need:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public interface IPluginContext
{
    Guid PluginId { get; }
    string Code { get; }              // 8-char namespace, e.g. "a1b2c3d4"
    string SchemaName { get; }        // "plg_<code>"
    IHookRegistrar Hooks { get; }
    IPluginDataAccess Data { get; }
    IPluginMenus Menus { get; }
    IServiceProvider HostServices { get; }
}`}
        </pre>
        <ul>
          <li>
            <strong><code>Hooks</code></strong> — write surface for hook
            registrations. The host wraps the global registrar in a
            per-plugin scope so disable can revoke everything atomically.
          </li>
          <li>
            <strong><code>Data</code></strong> — read/write surface for the
            plugin&apos;s own schema and read-only surface for everything else;
            see Plugin-owned data below.
          </li>
          <li>
            <strong><code>Menus</code></strong> — helpers for adding menu
            items to the host sidebar; see Menu helpers below.
          </li>
          <li>
            <strong><code>HostServices</code></strong> — for cross-cutting
            services (e.g. <code>ILoggerFactory</code>). Don&apos;t use this for
            data access; use <code>Data</code>.
          </li>
        </ul>
      </Section>

      <Section id="hooks" title="Hooks: actions and filters">
        <p>
          Hooks are the plugin&apos;s entry into the host&apos;s behaviour. There are two
          kinds, both registered through <code>IHookRegistrar</code>:
        </p>
        <h5 style={{ marginTop: 16 }}>Actions — fire-and-forget callbacks</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`context.Hooks.AddAction("autonate.something.happened", priority: 100, ctx =>
{
    // log, record metrics, queue a job. Don't return a value; don't block.
});

context.Hooks.AddActionAsync("autonate.something.happened", priority: 100,
    async (ctx, ct) =>
    {
        await SomethingAsync(ctx, ct);
    });`}
        </pre>
        <h5 style={{ marginTop: 16 }}>Filters — transform a value</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
    HookPoints.AuthorizeAuthorize,
    priority: 100,
    async (ctx, _, ct) =>
    {
        if (ShouldDeny(ctx))
        {
            return ctx with
            {
                CurrentDecision = new AuthDecisionDto
                {
                    Effect = AuthEffectDto.Deny,
                    Reason = "my-policy"
                }
            };
        }
        return ctx;
    });`}
        </pre>
        <p>
          Each filter receives the <em>current</em> value (already passed
          through earlier-priority filters), a chain delegate, and a cancellation
          token. Return either the input unchanged or a modified copy — never
          mutate the input.
        </p>
        <h5 style={{ marginTop: 16 }}>Priority</h5>
        <p>
          Hooks fire in ascending <code>priority</code> order. Lower numbers run
          first; same number, undefined order — don&apos;t rely on it. Use a number
          relative to the host hook&apos;s documented anchor (every hook point comments
          its anchor in <code>HookPoints</code>).
        </p>
        <p>
          Every <code>AddXxx</code> call returns a <code>HookHandle</code> you
          can keep and pass to <code>Remove</code> to detach a single hook —
          rarely needed because disable revokes them all in bulk.
        </p>
        <h5 style={{ marginTop: 16 }}>Available hook points</h5>
        <p>
          See <code>AutoNate.Plugin.Abstractions/HookPoints.cs</code> for the
          full canonical list. Each constant ships with a comment describing
          when it fires, what context it carries, and what filter result the
          host expects. Only use the named constants, not raw strings.
        </p>
      </Section>

      <Section id="data" title="Plugin-owned data & isolation">
        <p>
          When a plugin is uploaded, the host provisions:
        </p>
        <ul>
          <li>
            A <strong>per-plugin schema</strong>{" "}
            <code>plg_&lt;code&gt;</code> in the AutoNate database.
          </li>
          <li>
            A <strong>per-plugin LOGIN role</strong>{" "}
            <code>plg_&lt;code&gt;</code> with a random password. The role
            <em>owns</em> the schema; it&apos;s the only role that can write to it.
          </li>
          <li>
            A grant of the shared{" "}
            <strong><code>plg_readers</code></strong> group role to that plugin
            role. <code>plg_readers</code> has <code>USAGE</code> on{" "}
            <code>public</code> and <code>SELECT</code> on every current and
            future table/sequence the host creates, plus <code>USAGE</code> +
            <code>SELECT</code> defaults on every other plugin&apos;s schema.
          </li>
        </ul>
        <p>
          The plugin&apos;s <code>NpgsqlDataSource</code> connects as the per-plugin
          role with <code>search_path = plg_&lt;code&gt;,public</code>. The
          consequences are:
        </p>
        <ul>
          <li>
            Unqualified <code>INSERT</code>/<code>UPDATE</code>/<code>DELETE</code>{" "}
            and <code>CREATE</code>/<code>ALTER</code> hit the plugin&apos;s own
            schema and succeed.
          </li>
          <li>
            Unqualified <code>SELECT</code> resolves against the plugin&apos;s schema
            first, falling back to <code>public</code> for app tables. App reads
            succeed; app writes are rejected by Postgres with{" "}
            <code>SQLSTATE 42501</code> (insufficient privilege).
          </li>
          <li>
            Cross-plugin SQL needs an explicit schema prefix:{" "}
            <code>SELECT * FROM plg_other.widgets</code>. Reads succeed; writes
            (or any DDL) fail at the database level.
          </li>
        </ul>
        <p>
          Because isolation is enforced by the database — not by the wrapper
          API — a plugin cannot escape it by getting clever with the raw
          connection. The worst it can do is throw <code>PostgresException</code>{" "}
          on a denied operation.
        </p>
      </Section>

      <Section id="migrations" title="Migrations">
        <p>
          Plugins ship schema as numbered SQL files in a top-level{" "}
          <code>migrations/</code> folder. The host applies them lexically at
          enable time, in their own transactions, and tracks applied filenames
          in <code>plg_&lt;code&gt;.__plugin_migrations</code>.
        </p>
        <h5 style={{ marginTop: 16 }}>Authoring</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`migrations/
  001_init.sql
  002_add_seen_at.sql
  003_index_label.sql`}
        </pre>
        <p>
          Filenames are sorted by ordinal string compare; the recommended
          pattern is a zero-padded numeric prefix. Use plain DDL — there&apos;s no
          DSL or templating layer.
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`-- 001_init.sql
CREATE TABLE IF NOT EXISTS widgets (
    id BIGSERIAL PRIMARY KEY,
    label TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);`}
        </pre>
        <h5 style={{ marginTop: 16 }}>Rules</h5>
        <ul>
          <li>
            <strong>Files are immutable once shipped.</strong> Never edit a
            migration that may have been applied in a customer environment;
            ship a new file with a higher number.
          </li>
          <li>
            <strong>One transaction per file.</strong> If the file fails halfway
            through, the entire file rolls back and the migration runner
            reports the offending filename. Fix the file, re-enable.
          </li>
          <li>
            <strong>Don&apos;t reach outside your own schema.</strong> The plugin
            role can&apos;t write to <code>public</code>; attempting to{" "}
            <code>INSERT INTO public.x</code> from a migration will fail.
          </li>
          <li>
            <strong>Use <code>IF NOT EXISTS</code> for safety</strong> — even
            though tracked migrations don&apos;t re-run, defensive DDL is cheap and
            keeps a manually-recovered environment idempotent.
          </li>
        </ul>
        <h5 style={{ marginTop: 16 }}>MSBuild integration</h5>
        <p>
          The shared <code>plugins/Directory.Build.props</code> automatically
          copies any <code>migrations/*.sql</code> next to <code>plugin.json</code>{" "}
          in the build output and into the upload zip. Just create the folder
          and add files — no per-plugin csproj edits.
        </p>
      </Section>

      <Section id="data-access" title="IPluginDataAccess">
        <p>
          <code>context.Data</code> is the plugin&apos;s data API. It wraps the
          per-plugin <code>NpgsqlDataSource</code> and exposes Dapper-style
          helpers — your plugin doesn&apos;t need to take a Dapper dependency itself,
          the abstractions assembly already does.
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public interface IPluginDataAccess
{
    Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default);

    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default);
}`}
        </pre>
        <h5 style={{ marginTop: 16 }}>Common patterns</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`// Insert
await context.Data.ExecuteAsync(
    "INSERT INTO greetings (saw_action, saw_kind) VALUES (@action, @kind);",
    new { action, kind },
    ct);

// Read your own table
var rows = await context.Data.QueryAsync<Greeting>(
    "SELECT id, saw_action AS Action, saw_kind AS Kind FROM greetings WHERE saw_kind = @k;",
    new { k = "record" },
    ct);

// Read an app table (qualified — search_path also resolves it unqualified)
var userCount = await context.Data.QuerySingleOrDefaultAsync<long>(
    "SELECT COUNT(*) FROM public.local_users;",
    ct: ct);

// Anything richer (COPY, multi-statement scripts, prepared)
await using var conn = await context.Data.OpenConnectionAsync(ct);
// … Npgsql/Dapper directly`}
        </pre>
        <p>
          Always pass a <code>CancellationToken</code> from the surrounding hook
          context — long-running queries should cancel cleanly when the request
          is abandoned.
        </p>
      </Section>

      <Section id="cross-plugin" title="Cross-plugin reads">
        <p>
          Plugin B can SELECT from plugin A&apos;s schema using the qualified name:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`var labels = await context.Data.QueryAsync<string>(
    "SELECT label FROM plg_a1b2c3d4.widgets ORDER BY id;",
    ct: ct);`}
        </pre>
        <p>
          Hard-coding another plugin&apos;s code is brittle. Resolve it at runtime by
          name from the <code>plugins</code> table:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`var otherCode = await context.Data.QuerySingleOrDefaultAsync<string>(
    "SELECT code FROM public.plugins WHERE name = @name AND status = 1;",
    new { name = "OtherPlugin" },
    ct);

if (otherCode is null) return Array.Empty<string>();

// Build the SQL with the schema name; it's not parameterizable.
var sql = $"SELECT label FROM plg_{otherCode}.widgets;";
return await context.Data.QueryAsync<string>(sql, ct: ct);`}
        </pre>
        <p>
          The <code>plugins.code</code> column is constrained to{" "}
          <code>[a-z][a-z0-9]&#123;7&#125;</code> at provisioning time, so
          string-interpolating it into SQL is safe — but only if you trust that
          source. Never interpolate values that came from outside the database.
        </p>
        <p>
          Cross-plugin <em>writes</em> are deliberately impossible. If two
          plugins need to coordinate on shared state, the pattern is: one
          plugin owns the table, the other plugin posts events through a hook,
          and the owning plugin handles them.
        </p>
      </Section>

      <Section id="menus" title="Menu helpers">
        <p>
          A plugin can add items to any of the host&apos;s menus through{" "}
          <code>context.Menus</code>. There are three add helpers (specialised
          to common cases) and one introspection helper:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public interface IPluginMenus
{
    // Snapshot of every menu and its items so a plugin can introspect
    // existing structure (e.g. find a parent group's id).
    IReadOnlyList<MenuInfo> ListMenus();

    // Single item under the existing "Plugins" group in Site Configuration.
    // The host resolves the parent for you.
    Guid AddPluginMenuItem(NewMenuItem item);

    // Creates a new top-level group inside Site Configuration and populates
    // it with the given children, in declared order.
    Guid AddSiteConfigGroup(string displayName, string? icon, IEnumerable<NewMenuItem> children);

    // Generic insert: any menu (looked up by key) under any parent
    // (null = top-level).
    Guid AddMenuItem(string menuKey, Guid? parentId, NewMenuItem item);

    // Removes every menu_items row this plugin previously added. Mirrors
    // the sweep the host runs on disable / FK CASCADE on delete; expose it
    // so plugins can call it explicitly from Cleanup() or to stage a
    // re-registration mid-session.
    int RemoveAll();

    // Removes a single menu_items row by id IF it was added by this plugin.
    // No-op for items the plugin doesn't own — useful for surgical cleanup
    // (e.g. "remove the trailing separator I added under Settings").
    bool RemoveMenuItem(Guid id);
}`}
        </pre>

        <h5 style={{ marginTop: 16 }}>NewMenuItem</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public sealed record NewMenuItem(
    string DisplayName,
    string ItemType,
    string? Icon = null,
    object? Config = null,        // JSON-serialized into the config column
    int? SortOrder = null,        // null = append after existing siblings
    bool IsVisible = true);`}
        </pre>

        <h5 style={{ marginTop: 16 }}>Item types &amp; config shapes</h5>
        <p>
          The <code>ItemType</code> string drives how the menu item renders.
          The host&apos;s config table:
        </p>
        <table className="table table-sm">
          <thead>
            <tr>
              <th>ItemType</th>
              <th>Config shape</th>
              <th>Behaviour</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><code>&quot;template&quot;</code></td>
              <td><code>{"{ templateKey, path }"}</code></td>
              <td>Bind to a built-in React component shipped by the host.</td>
            </tr>
            <tr>
              <td><code>&quot;page&quot;</code></td>
              <td>
                <code>{"{ path, content, contentType: \"html\"|\"jsx\" }"}</code>
              </td>
              <td>
                Render arbitrary content the plugin ships in the row itself.
              </td>
            </tr>
            <tr>
              <td><code>&quot;link&quot;</code></td>
              <td><code>{"{ href }"}</code></td>
              <td>External URL.</td>
            </tr>
            <tr>
              <td><code>&quot;action&quot;</code></td>
              <td><code>{"{ action }"}</code></td>
              <td>Predefined client action (e.g. <code>&quot;logout&quot;</code>).</td>
            </tr>
            <tr>
              <td><code>&quot;separator&quot;</code></td>
              <td><code>{`{}`}</code></td>
              <td>Visual divider in a dropdown / submenu.</td>
            </tr>
            <tr>
              <td><code>&quot;group&quot;</code></td>
              <td>
                <code>{"{ startsExpanded?, dynamicChildren? }"}</code>
              </td>
              <td>Container for child items.</td>
            </tr>
          </tbody>
        </table>

        <h5 style={{ marginTop: 16 }}>JSX content (recommended for plugin pages)</h5>
        <p>
          For a page that needs interactivity, use{" "}
          <code>contentType: &quot;jsx&quot;</code> and ship JSX source code. The host
          compiles it at runtime via Sucrase. The contract is{" "}
          <strong>define a function called <code>Page</code></strong>:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`context.Menus.AddPluginMenuItem(new NewMenuItem(
    DisplayName: "My Plugin Settings",
    ItemType:    "page",
    Icon:        "fa fa-gear",
    Config: new {
        path        = $"/admin/config/plugins/{context.Code}/settings",
        contentType = "jsx",
        content     = """
            function Page() {
              const [count, setCount] = useState(0);
              return (
                <div className="page-head">
                  <h1>My plugin</h1>
                  <button onClick={() => setCount(count + 1)}>
                    Clicked {count} times
                  </button>
                </div>
              );
            }
        """,
    }));`}
        </pre>
        <p>
          Available in JSX scope: <code>React</code>,{" "}
          <code>useState</code>, <code>useEffect</code>, <code>useMemo</code>,{" "}
          <code>useCallback</code>, <code>useRef</code>,{" "}
          <code>navigate</code> (react-router), <code>Link</code>,{" "}
          <code>NavLink</code>, <code>api</code> (typed JSON HTTP client to the
          host&apos;s REST API), and <code>logout</code>. TypeScript is also
          accepted — Sucrase strips the type annotations.
        </p>

        <h5 style={{ marginTop: 16 }}>HTML content</h5>
        <p>
          For plain pages, <code>contentType: &quot;html&quot;</code> renders the{" "}
          <code>content</code> string with{" "}
          <code>dangerouslySetInnerHTML</code>. Embedded{" "}
          <code>&lt;script&gt;</code> tags are re-injected so they actually
          execute (innerHTML-inserted scripts no-op by browser spec).
        </p>

        <h5 style={{ marginTop: 16 }}>Path conventions</h5>
        <ul>
          <li>
            Paths under <code>/admin/config/*</code> render <em>inside</em> the
            Site Configuration sidebar shell. This is usually what you want for
            a plugin settings page.
          </li>
          <li>
            Paths anywhere else render in the top-level app shell only.
          </li>
          <li>
            Convention:{" "}
            <code>{"/admin/config/plugins/{context.Code}/{key}"}</code> — the
            plugin&apos;s 8-char code keeps your paths globally unique even if
            another plugin picks the same key.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>Lifecycle &amp; ownership</h5>
        <p>
          Every row inserted by these helpers has{" "}
          <code>menu_items.created_by_plugin_id</code> set to the plugin&apos;s id.
          The host enforces three lifecycle rules off that column:
        </p>
        <ul>
          <li>
            <strong>Before each enable</strong>: the host{" "}
            <code>DELETE</code>s every menu item tagged with the plugin&apos;s id,
            so <code>Configure()</code> always runs against a clean slate. You
            don&apos;t need to check whether a menu item already exists; just call{" "}
            <code>AddXxx()</code> unconditionally.
          </li>
          <li>
            <strong>On disable</strong>: the host runs the same{" "}
            <code>DELETE</code>, removing the plugin&apos;s items from the sidebar.
            They re-appear on next enable.
          </li>
          <li>
            <strong>On delete</strong>: the FK{" "}
            <code>ON DELETE CASCADE</code> on{" "}
            <code>created_by_plugin_id → plugins(id)</code> sweeps everything.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>Examples</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`// Single item under the Plugins group:
context.Menus.AddPluginMenuItem(new NewMenuItem(
    DisplayName: "My Plugin Settings",
    ItemType:    "page",
    Icon:        "fa fa-gear",
    Config: new { path = "/admin/config/plugins/abcd1234/settings",
                  contentType = "jsx",
                  content = "function Page() { return <h1>Hi</h1>; }" }));

// New top-level group with two children:
context.Menus.AddSiteConfigGroup(
    displayName: "Reporting",
    icon:        "fa fa-chart-line",
    children: new[] {
        new NewMenuItem("Daily summary", "page", "fa fa-calendar-day",
            new { path = "/admin/config/reporting/daily",
                  contentType = "html",
                  content = "<h1>Daily</h1>" }),
        new NewMenuItem("Weekly summary", "page", "fa fa-calendar-week",
            new { path = "/admin/config/reporting/weekly",
                  contentType = "html",
                  content = "<h1>Weekly</h1>" }),
    });

// Anywhere, anywhere: add a "Reset cache" entry under the user's avatar menu:
var menus = context.Menus.ListMenus();
var userMenu = menus.First(m => m.Key == "user");
context.Menus.AddMenuItem("user", parentId: null, new NewMenuItem(
    DisplayName: "Reset cache (debug)",
    ItemType:    "page",
    Icon:        "fa fa-eraser",
    Config: new { path = "/debug/reset-cache",
                  contentType = "jsx",
                  content = "function Page() { return <p>...</p>; }" }));`}
        </pre>
      </Section>

      <Section id="page-templates" title="Page templates">
        <p>
          Plugins can ship reusable page templates that augment the host&apos;s
          built-in template set. They appear in the <em>Pages / Menus</em>{" "}
          template-picker dropdown alongside <code>home</code>,{" "}
          <code>busWatcher</code>, etc., so an admin can mount any of them in
          any menu without the plugin having to register a menu item itself.
          Use them when the page is reusable; use a plain{" "}
          <code>item_type = &quot;page&quot;</code> menu item when the page is a
          one-off settings screen tied to a specific sidebar entry.
        </p>

        <h5 style={{ marginTop: 16 }}>Folder convention</h5>
        <p>
          Drop <code>.template</code> files in a top-level{" "}
          <code>PageTemplates/</code> folder next to your csproj. The shared{" "}
          <code>plugins/Directory.Build.props</code> copies them into the zip
          automatically.
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`MyPlugin/
  MyPlugin.csproj
  plugin.json
  MyPlugin.cs
  migrations/
    001_init.sql
  PageTemplates/
    AuditLog.template
    Dashboard.template`}
        </pre>
        <ul>
          <li>
            <strong>The filename stem becomes the template{" "}
            <code>key</code></strong>. <code>AuditLog.template</code> →{" "}
            <code>key = &quot;AuditLog&quot;</code>. The key is unique across the
            install (host built-ins included), so namespace yours to avoid
            collisions — the host won&apos;t clobber a key it doesn&apos;t own.
          </li>
          <li>
            <strong>The contents are JSX</strong>, exactly the same shape as
            the JSX page strings in <code>contentType: &quot;jsx&quot;</code> menu
            items. Define a top-level <code>function Page()</code> and you
            have access to <code>useState</code>, <code>useEffect</code>,{" "}
            <code>navigate</code>, <code>api</code>, and the rest of the
            JsxPage scope.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>Auto-registration on enable</h5>
        <p>
          On every enable, the host&apos;s <code>PluginRuntime</code> walks{" "}
          <code>PageTemplates/*.template</code> and{" "}
          <strong>UPSERTs each row by key</strong> into{" "}
          <code>public.page_templates</code> with{" "}
          <code>created_by_plugin_id = &lt;your-plugin-id&gt;</code> and{" "}
          <code>content_type = &quot;jsx&quot;</code>. Templates the plugin
          registered on a previous enable but no longer ships are deleted —
          file presence is the source of truth.
        </p>
        <p>
          Three things happen automatically and you don&apos;t have to manage them:
        </p>
        <ul>
          <li>
            <strong>Default path</strong> is set to{" "}
            <code>/plugins/&lt;code&gt;/&lt;key-lowercased&gt;</code> so two
            plugins can&apos;t collide on the unique <code>default_path</code>{" "}
            constraint.
          </li>
          <li>
            <strong>Placeholder substitution</strong> happens before the JSX
            is persisted. <code>&#123;&#123;pluginCode&#125;&#125;</code> is
            replaced with the plugin&apos;s 8-char code and{" "}
            <code>&#123;&#123;pluginId&#125;&#125;</code> with its UUID. Use
            them to address your own per-plugin endpoints (the data hook
            below) without knowing the code at build time.
          </li>
          <li>
            <strong>Conflict guard</strong>: if a row with the same key is
            owned by the host or another plugin, registration is skipped
            with a warning rather than overwriting it. Pick a key with the
            plugin&apos;s name as a prefix.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>Mounting a template in a menu</h5>
        <p>
          Once registered, a template is identified by its key wherever a
          menu item uses <code>item_type = &quot;template&quot;</code>. The plugin can
          mount its own template:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`context.Menus.AddMenuItem("icon", settingsGroupId, new NewMenuItem(
    DisplayName: "Audit Log",
    ItemType:    "template",
    Icon:        "fa fa-clipboard-list",
    Config: new {
        templateKey = "AuditLog",
        path        = $"/plugins/{context.Code}/auditlog",
    }));`}
        </pre>
        <p>
          The same template is also available in the admin{" "}
          <em>Pages / Menus</em> editor&apos;s template picker, so a non-coding
          admin can mount it under any group without touching the plugin.
        </p>

        <h5 style={{ marginTop: 16 }}>Fetching plugin data from a template</h5>
        <p>
          A template that needs to read plugin data hits the host&apos;s per-plugin
          data endpoint. The host fires{" "}
          <code>HookPoints.PluginDataHookFor(code)</code>, the plugin
          subscribes in <code>Configure()</code> and returns a JSON payload:
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`// In the plugin's Configure(), wired once:
context.Hooks.AddFilterAsync<PluginDataResponse>(
    HookPoints.PluginDataHookFor(context.Code),
    priority: 100,
    async (current, args, ct) =>
    {
        if (args.Length == 0 || args[0] is not PluginDataRequest req) return current;
        if (req.View != "audit-log") return current;
        var rows = await context.Data.QueryAsync<MyRow>("SELECT … FROM audit_log …", ct: ct);
        return new PluginDataResponse {
            StatusCode  = 200,
            ContentJson = JsonSerializer.Serialize(new { rows }),
        };
    });

// In the .template file, the JSX uses the substituted code:
api.get("/api/admin/plugins/by-code/{{pluginCode}}/data/audit-log",
        { params: { limit: 50, offset: 0 } })
   .then((res) => setRows(res.data.rows));`}
        </pre>

        <h5 style={{ marginTop: 16 }}>Lifecycle</h5>
        <ul>
          <li>
            <strong>Enable</strong>: registers / refreshes the rows; sweeps
            stale rows whose source files were removed.
          </li>
          <li>
            <strong>Disable</strong>: rows stay (they&apos;re reference data; the
            menu items that point at them are removed instead). On the next
            enable they&apos;re refreshed in place.
          </li>
          <li>
            <strong>Delete</strong>: FK <code>ON DELETE CASCADE</code> on{" "}
            <code>page_templates.created_by_plugin_id → plugins(id)</code>{" "}
            sweeps every template the plugin ever registered.
          </li>
        </ul>
      </Section>

      <Section id="cleanup" title="Cleanup routines">
        <p>
          When the host is about to delete a plugin, it calls{" "}
          <code>IAutoNatePlugin.Cleanup(IPluginContext)</code>{" "}
          <strong>before</strong> tearing down the plugin&apos;s schema, role,
          on-disk files, and database row. This is the plugin&apos;s last chance
          to remove anything <em>outside</em> the host&apos;s automatic teardown.
          The default implementation is a no-op; only override it when your
          plugin actually owns artifacts beyond what&apos;s listed below.
        </p>

        <h5 style={{ marginTop: 16 }}>When it runs</h5>
        <ul>
          <li>
            <strong>On every delete, even if the plugin is disabled</strong>.
            The host loads the assembly into a transient ALC just for this
            call, instantiates the plugin via{" "}
            <code>Activator.CreateInstance</code>, and invokes{" "}
            <code>Cleanup</code> with a fresh context — the same shape{" "}
            <code>Configure</code> got, with a working{" "}
            <code>Hooks</code> / <code>Data</code> / <code>Menus</code>{" "}
            surface. The transient ALC is unloaded as soon as cleanup
            returns.
          </li>
          <li>
            <strong>Never on disable, upload, or enable</strong>. Disable
            revokes hooks and sweeps menu items; enable re-registers them.
            Cleanup is delete-only.
          </li>
          <li>
            <strong>Errors are logged and swallowed</strong>. A throw inside
            Cleanup never blocks the delete. Log loudly so operators see the
            breakage; don&apos;t rely on cleanup-must-succeed semantics.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>What the host already cleans up for free</h5>
        <p>
          You do <strong>not</strong> need to do any of this in{" "}
          <code>Cleanup</code> — the host handles it after your callback
          returns:
        </p>
        <ul>
          <li>
            <strong>The plugin&apos;s per-plugin schema</strong> (
            <code>plg_&lt;code&gt;</code>) and every table in it via{" "}
            <code>DROP SCHEMA … CASCADE</code>.
          </li>
          <li>
            <strong>The plugin&apos;s per-plugin Postgres role</strong> (
            <code>plg_&lt;code&gt;</code>).
          </li>
          <li>
            <strong>Menu items and page templates</strong> the plugin
            registered, via FK <code>ON DELETE CASCADE</code> on{" "}
            <code>created_by_plugin_id → plugins(id)</code>.
          </li>
          <li>
            <strong>The plugin&apos;s on-disk folder</strong> under{" "}
            <code>plugins/&lt;PluginId&gt;/</code>.
          </li>
          <li>
            <strong>Audit / lifecycle event publication</strong> for the
            delete itself (<code>plugin.deleted</code>).
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>What Cleanup is for</h5>
        <p>
          Anything the plugin created that the host has no FK / schema
          ownership over:
        </p>
        <ul>
          <li>
            <strong>Menu items the plugin wants removed before the FK runs</strong>
            (so the sidebar updates cleanly without waiting for a refresh).
            Use <code>context.Menus.RemoveAll()</code> for the bulk case or{" "}
            <code>context.Menus.RemoveMenuItem(id)</code> when the plugin
            wants to be surgical (e.g. remove an item but leave a separator
            another plugin owns). Both helpers are ownership-checked: a
            plugin cannot remove items it didn&apos;t create.
          </li>
          <li>
            <strong>Application-data records the plugin created via host
            hooks</strong> (record types, role grants, workflow definitions,
            etc.). The host doesn&apos;t track these as &quot;owned by this plugin&quot;,
            so they outlive the delete unless Cleanup removes them through
            the same hooks.
          </li>
          <li>
            <strong>Files, queues, or external state outside the
            database</strong> — anything the plugin wrote to a shared
            directory, an outbound queue it provisioned, a third-party
            service it registered with.
          </li>
        </ul>

        <h5 style={{ marginTop: 16 }}>Example</h5>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`public void Cleanup(IPluginContext context)
{
    var logger = context.HostServices
        .GetService<ILoggerFactory>()
        ?.CreateLogger("MyPlugin");

    // Remove a specific menu item we added in Configure(), then a
    // trailing separator only if we own it. RemoveMenuItem is a no-op
    // for items the plugin didn't create, so this is always safe.
    var menus = context.Menus.ListMenus();
    var icon = menus.FirstOrDefault(m => m.Key == "icon");
    if (icon is not null)
    {
        var settings = icon.Items.FirstOrDefault(
            i => i.DisplayName == "Settings" && i.ItemType == "group");
        if (settings is not null)
        {
            // ... locate and remove our items by id ...
        }
    }

    // Bulk-remove anything else this plugin owns. Equivalent to the
    // sweep the host runs on disable; runs again here so the sidebar
    // is consistent BEFORE the FK CASCADE.
    var removed = context.Menus.RemoveAll();
    logger?.LogInformation("MyPlugin cleanup removed {Count} menu item(s).", removed);

    // The plugin's plg_<code> schema is dropped by the host immediately
    // after this returns, so don't bother dropping its tables here.
}`}
        </pre>

        <h5 style={{ marginTop: 16 }}>What you should NOT do in Cleanup</h5>
        <ul>
          <li>
            Don&apos;t try to mutate <code>public.*</code> tables directly. The
            plugin role still can&apos;t write to them; use a host hook or skip
            the cleanup if no hook exists.
          </li>
          <li>
            Don&apos;t register hooks. The host wraps Cleanup&apos;s hook registrar in
            a scope and discards everything it added the moment Cleanup
            returns.
          </li>
          <li>
            Don&apos;t depend on long-running side effects. The transient ALC is
            unloaded right after Cleanup, so any background <code>Task</code>{" "}
            you start may be cut short.
          </li>
        </ul>
      </Section>

      <Section id="patterns" title="Patterns & best practices">
        <h5>Hooks are the only way to mutate app data</h5>
        <p>
          A plugin cannot UPDATE a record in <code>public</code>. To change app
          state, register a filter on the relevant hook point and return a
          modified context, or invoke a host-exposed action that performs the
          mutation on the plugin&apos;s behalf. <em>Adding</em> a hook point when one
          you need doesn&apos;t exist is a host-side change, not a plugin-side
          workaround.
        </p>
        <h5 style={{ marginTop: 16 }}>Configure() does registration only</h5>
        <p>
          Heavy work — schema bootstrap, network calls, background loops —
          belongs inside hook handlers (so it runs on the relevant request) or
          in a long-lived service the plugin owns. Don&apos;t block enable.
        </p>
        <h5 style={{ marginTop: 16 }}>Idempotent hook handlers</h5>
        <p>
          Action hooks may fire more than once for the same logical event during
          replays, retries, or hot-reload scenarios. Use natural keys
          (<code>ON CONFLICT DO NOTHING</code>, <code>WHERE NOT EXISTS</code>)
          so a duplicate fire is a no-op rather than a duplicate row.
        </p>
        <h5 style={{ marginTop: 16 }}>Don&apos;t capture <code>IPluginContext</code> long-term</h5>
        <p>
          Stash the bits you need (<code>context.Data</code>, the logger
          factory) in your own fields. The context object itself is fine to
          capture, but don&apos;t expose it as a public surface — your plugin&apos;s
          internals shouldn&apos;t leak across an ABI boundary.
        </p>
        <h5 style={{ marginTop: 16 }}>Logging</h5>
        <p>
          Resolve <code>ILoggerFactory</code> from{" "}
          <code>context.HostServices</code> and create one logger per plugin
          subsystem. Logs flow into the same sinks the host uses; your plugin&apos;s
          messages appear under whatever category name you pass to{" "}
          <code>CreateLogger</code>.
        </p>
        <h5 style={{ marginTop: 16 }}>Don&apos;t ship host-shared assemblies</h5>
        <p>
          Five DLLs are loaded by the host and shared into every plugin ALC for
          type identity:
        </p>
        <ul>
          <li><code>AutoNate.Plugin.Abstractions</code></li>
          <li><code>Microsoft.Extensions.DependencyInjection.Abstractions</code></li>
          <li><code>Microsoft.Extensions.Logging.Abstractions</code></li>
          <li><code>Npgsql</code></li>
          <li><code>Dapper</code></li>
        </ul>
        <p>
          The build target excludes them from the zip automatically. If you add
          a NuGet that brings in another assembly the host already loads,
          extend the exclusion list rather than shipping a duplicate — two
          copies in two ALCs means two distinct <code>Type</code> instances
          and silently failed casts.
        </p>
        <h5 style={{ marginTop: 16 }}>Versioning</h5>
        <p>
          Bump <code>plugin.json</code>&apos;s <code>version</code> when shipping a
          new build; the admin Manage Plugins page shows the version, and{" "}
          <code>plugin.disabled</code> / <code>plugin.deleted</code> events
          carry it for audit consumers.
        </p>
      </Section>

      <Section id="packaging" title="Building & packaging">
        <p>
          Each plugin&apos;s csproj inherits from{" "}
          <code>plugins/Directory.Build.props</code> and{" "}
          <code>plugins/Directory.Build.targets</code>. After every successful
          build, the targets file zips the bin output (minus host-shared DLLs
          and PDBs) into <code>&lt;ProjectDir&gt;/dist/&lt;PluginName&gt;.zip</code>.
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`# from the repo root:
dotnet build plugins/HelloPlugin/HelloPlugin.csproj
# → plugins/HelloPlugin/dist/HelloPlugin.zip

# Then upload via Site Configuration → Plugins → Manage Plugins → "Upload plugin…"`}
        </pre>
        <p>
          A minimal plugin csproj is empty — Directory.Build supplies the
          settings. If your plugin needs extra packages, add them to the csproj
          as normal <code>PackageReference</code> items; transitively-referenced
          assemblies that aren&apos;t in the host-shared list are bundled into the
          zip.
        </p>
        <h5 style={{ marginTop: 16 }}>Reload semantics</h5>
        <p>
          Re-uploading a plugin <em>creates a new install</em> with a new code
          and a new schema. To upgrade in place, delete the existing install
          (data is lost) and upload the new version. To carry data forward,
          ship a new migration file in the same plugin and re-enable.
        </p>
      </Section>

      <Section id="hello" title="Worked example: HelloPlugin">
        <p>
          The repository&apos;s reference plugin lives at{" "}
          <code>plugins/HelloPlugin/</code>. It exercises every plugin
          extension point: a hook, a per-plugin schema with a migration, and a
          JSX settings page added to the sidebar.
        </p>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`// HelloPlugin.cs
public sealed class HelloPlugin : IAutoNatePlugin
{
    public string Name => "HelloPlugin";
    public string Version => "1.0.0";

    public void Configure(IPluginContext context)
    {
        var logger = context.HostServices
            .GetService<ILoggerFactory>()?.CreateLogger("HelloPlugin");

        // 1) Add a settings page under the "Plugins" group in Site Config.
        context.Menus.AddPluginMenuItem(new NewMenuItem(
            DisplayName: "HelloPlugin Settings",
            ItemType:    "page",
            Icon:        "fa fa-comment",
            Config: new {
                path        = $"/admin/config/plugins/{context.Code}/hello-config",
                contentType = "jsx",
                content     = HelloConfigJsx,
            }));

        // 2) Hook the authorize chain and append a row to the plugin's own
        //    greetings table on every invocation.
        context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize,
            priority: 100,
            async (ctx, _, ct) =>
            {
                logger?.LogInformation(
                    "HelloPlugin saw authorize: {Action} {Kind}:{Id} -> {Effect}",
                    ctx.Action, ctx.Target.Kind, ctx.Target.Id,
                    ctx.CurrentDecision.Effect);

                await context.Data.ExecuteAsync(
                    "INSERT INTO greetings (saw_action, saw_kind, saw_id, saw_effect) " +
                    "VALUES (@action, @kind, @id, @effect);",
                    new
                    {
                        action = ctx.Action,
                        kind   = ctx.Target.Kind,
                        id     = ctx.Target.Id,
                        effect = ctx.CurrentDecision.Effect.ToString()
                    },
                    ct);

                return ctx;
            });
    }

    private const string HelloConfigJsx = """
        function Page() {
          const [count, setCount] = useState(0);
          return (
            <>
              <h1>HelloPlugin Settings</h1>
              <button onClick={() => setCount(count + 1)}>
                Clicked {count} times
              </button>
            </>
          );
        }
    """;
}`}
        </pre>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`-- migrations/001_init.sql
CREATE TABLE IF NOT EXISTS greetings (
    id BIGSERIAL PRIMARY KEY,
    saw_action TEXT NOT NULL,
    saw_kind TEXT NOT NULL,
    saw_id TEXT NOT NULL,
    saw_effect TEXT NOT NULL,
    seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);`}
        </pre>
        <p>After upload-enable:</p>
        <ul>
          <li>
            &quot;HelloPlugin Settings&quot; appears under{" "}
            <em>Site Configuration → Plugins</em>; clicking it renders the JSX
            inside the config sidebar shell at{" "}
            <code>/admin/config/plugins/&lt;code&gt;/hello-config</code>.
          </li>
          <li>
            Every authorize call appends a row to{" "}
            <code>plg_&lt;code&gt;.greetings</code>.
          </li>
        </ul>
        <pre style={{ background: "var(--mantine-color-default-hover)", padding: "1rem", fontSize: 13, whiteSpace: "pre-wrap", borderRadius: "var(--mantine-radius-default)", margin: 0 }}>
{`\\dn plg_*
SELECT * FROM plg_<code>.greetings ORDER BY seen_at DESC LIMIT 10;
SELECT id, display_name, item_type, config->>'path' AS path
FROM   menu_items
WHERE  created_by_plugin_id = '<plugin-uuid>';`}
        </pre>
      </Section>

      <Section id="troubleshooting" title="Troubleshooting">
        <dl>
          <dt><code>SQLSTATE 42501</code> on an INSERT/UPDATE</dt>
          <dd>
            The plugin tried to write to a table it doesn&apos;t own. Either the
            target is a public app table (intentional — write through a hook),
            or the target is missing from this plugin&apos;s schema (check the
            migration ran).
          </dd>
          <dt className="mt-2">Plugin enables but the hook never fires</dt>
          <dd>
            Verify the hook name matches a constant in <code>HookPoints</code>;
            string typos won&apos;t error, they just never match. Also confirm
            another plugin earlier in priority isn&apos;t short-circuiting the chain.
          </dd>
          <dt className="mt-2">&quot;Plugin row is missing code or role password&quot;</dt>
          <dd>
            The plugin row predates the data-isolation feature (uploaded before
            this version). Re-upload to provision the schema.
          </dd>
          <dt className="mt-2">Migration fails on enable</dt>
          <dd>
            Plugin status flips back to Disabled and <code>last_error</code>{" "}
            shows the offending filename. Fix the SQL, rebuild, re-upload as a
            new plugin install (or for in-flight dev: edit the file in the
            staged plugin folder and re-enable).
          </dd>
          <dt className="mt-2">&quot;Plugin folder still locked&quot; at delete</dt>
          <dd>
            Windows-only. The schema and role were already dropped; only files
            remain. Status is <code>DeletedPending</code>; the next host
            startup retries the file delete.
          </dd>
          <dt className="mt-2">Plugin&apos;s menu item doesn&apos;t appear in the sidebar</dt>
          <dd>
            Check that <code>Configure()</code> actually called{" "}
            <code>context.Menus.AddXxx()</code> (no early return / try-swallow).
            If <code>AddPluginMenuItem</code> is used, the host bootstrap must
            have created the &quot;Plugins&quot; group in <code>site-config</code> — it
            does, but a hand-edited menu may have removed it. Falling back to{" "}
            <code>AddMenuItem(&quot;site-config&quot;, parentId, item)</code> with an
            explicit parent always works. The sidebar caches the menu in the
            SPA; a hard refresh after enable shows the new items.
          </dd>
          <dt className="mt-2">Plugin page renders without the config sidebar</dt>
          <dd>
            The path falls outside <code>/admin/config/*</code>. Place it under
            that prefix (convention:{" "}
            <code>{"/admin/config/plugins/{context.Code}/{key}"}</code>) so it
            mounts inside <code>ConfigLayout</code>.
          </dd>
          <dt className="mt-2">JSX page shows &quot;Define a function Page()…&quot;</dt>
          <dd>
            The compiled source didn&apos;t expose a top-level{" "}
            <code>function Page()</code>. Make sure the function is declared
            with that exact name and not nested inside another function or
            conditional. The error surfaces verbatim from{" "}
            <code>JsxPage</code>.
          </dd>
          <dt className="mt-2">Cast fails inside Configure</dt>
          <dd>
            Almost always a type-identity issue from shipping a host-shared
            assembly inside the zip. Re-check the build&apos;s exclusion list and
            confirm the zip doesn&apos;t contain <code>AutoNate.Plugin.Abstractions.dll</code>,{" "}
            <code>Npgsql.dll</code>, or <code>Dapper.dll</code>.
          </dd>
        </dl>
      </Section>
      </Stack>
    </>
  );
}

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <Card withBorder shadow="sm" id={id}>
      <Title order={5} mb="md">
        {title}
      </Title>
      <div>{children}</div>
    </Card>
  );
}
