using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoNate.Plugins.HelloPlugin;

// Demo plugin. Subscribes to autonate.authorize as a pass-through filter:
//   * logs each invocation,
//   * appends a row to its own greetings table inside its plg_<code> schema.
// The greetings table is created by /migrations/001_init.sql, applied by the
// host on the first enable after upload.
//
// Also demonstrates IPluginMenus by adding a "HelloPlugin Settings" item under
// the Plugins group in Site Configuration. The item is an item_type='page'
// with contentType='jsx', so the host renders the embedded JSX source as a
// real React component.
public sealed class HelloPlugin : IAutoNatePlugin
{
    public string Name => "HelloPlugin";
    public string Version => "1.0.0";

    public void Configure(IPluginContext context)
    {
        var logger = context.HostServices.GetService<ILoggerFactory>()?.CreateLogger("HelloPlugin");

        // Register a menu item under the Plugins group in site-config. The
        // host already cleared any prior items this plugin owned, so a fresh
        // INSERT is exactly what we want here.
        var pagePath = $"/admin/config/plugins/{context.Code}/hello-config";
        try
        {
            context.Menus.AddPluginMenuItem(new NewMenuItem(
                DisplayName: "HelloPlugin Settings",
                ItemType: "page",
                Icon: "fa fa-comment",
                Config: new
                {
                    path = pagePath,
                    contentType = "jsx",
                    content = HelloConfigJsx,
                }));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "HelloPlugin failed to register its config menu item.");
        }

        // Register a sample chatbot tool the plugin contributes. Demonstrates
        // the Phase 4 IPluginAgentSkills surface: a single skill named
        // "hello-plugin" with one echo tool that returns the args it received,
        // gated by an authorization probe on EntityKinds.SiteConfig:view (so
        // the model can see the CanAsync path exercised).
        try
        {
            using var schemaDoc = JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "message": { "type": "string", "description": "Text to echo back." }
                  },
                  "required": ["message"],
                  "additionalProperties": false
                }
                """);
            var schema = schemaDoc.RootElement.Clone();

            context.AgentSkills.Register(
                skillName: "hello-plugin",
                skillDescription: "Demo plugin-contributed tool (echoes the message back).",
                tools: new[]
                {
                    new PluginAgentTool(
                        Name: "hello_echo",
                        Description:
                            "Returns the supplied message back as a structured envelope. " +
                            "Demonstrates plugin-contributed chatbot tools.",
                        JsonSchema: schema,
                        Invoke: async (args, ctx, ct) =>
                        {
                            // Exercise the authorization probe — useful for the
                            // multi-cycle enable/disable test to verify the
                            // CanAsync wrapper still works after a reload.
                            var canViewSite = await ctx.Session.CanAsync("siteconfig", "view", null, ct);
                            var message = args.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                                ? (m.GetString() ?? string.Empty)
                                : string.Empty;
                            return JsonSerializer.SerializeToElement(new
                            {
                                kind = "hello_echo",
                                source = "HelloPlugin",
                                data = new
                                {
                                    message,
                                    userId = ctx.Session.UserId,
                                    pageKey = ctx.Session.PageKey,
                                    callerCanViewSiteConfig = canViewSite
                                }
                            });
                        })
                },
                systemPromptFragment: session =>
                    "HelloPlugin contributes the `hello_echo` tool; use it as a connectivity test for plugin-contributed skills.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "HelloPlugin failed to register its sample agent skill.");
        }

        context.Hooks.AddFilterAsync<AuthorizeFilterContext>(
            HookPoints.AuthorizeAuthorize,
            priority: 100,
            async (ctx, _, ct) =>
            {
                logger?.LogInformation(
                    "HelloPlugin saw authorize: action={Action} target={Kind}:{Id} effect={Effect}",
                    ctx.Action, ctx.Target.Kind, ctx.Target.Id, ctx.CurrentDecision.Effect);

                try
                {
                    await context.Data.ExecuteAsync(
                        "INSERT INTO greetings (saw_action, saw_kind, saw_id, saw_effect) " +
                        "VALUES (@action, @kind, @id, @effect);",
                        new
                        {
                            action = ctx.Action,
                            kind = ctx.Target.Kind,
                            id = ctx.Target.Id,
                            effect = ctx.CurrentDecision.Effect.ToString(),
                        },
                        ct);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "HelloPlugin failed to record greeting.");
                }

                return ctx;
            });
    }

    public void Cleanup(IPluginContext context)
    {
        var logger = context.HostServices
            .GetService<ILoggerFactory>()
            ?.CreateLogger("HelloPlugin");

        // Sweep the settings menu item we registered in Configure. The host
        // would also drop these via FK CASCADE during delete, but calling
        // RemoveAll() here makes the intent explicit and gives admins watching
        // the sidebar a clean before/after.
        try
        {
            var removed = context.Menus.RemoveAll();
            logger?.LogInformation("HelloPlugin cleanup removed {Count} menu item(s).", removed);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "HelloPlugin cleanup failed to remove its menu items.");
        }

        // The greetings table lives in the plugin's plg_<code> schema, which
        // the host DROP SCHEMA CASCADEs right after this returns — nothing
        // more to do.
    }

    // JSX source rendered by the host's JsxPage component. The contract is:
    // define a `function Page() { return <jsx/> }`. Available in scope:
    // React, useState/useEffect/useMemo/useCallback/useRef, navigate, Link,
    // NavLink, api (typed JSON HTTP client), logout.
    private const string HelloConfigJsx = """
        function Page() {
          const [tone, setTone] = useState("friendly");
          const [count, setCount] = useState(0);

          return (
            <>
              <div className="page-head">
                <h1 className="page-header mb-1">HelloPlugin Settings</h1>
                <p className="page-head-copy">
                  Example plugin configuration page rendered as JSX from inside the plugin.
                </p>
              </div>

              <div className="panel panel-inverse">
                <div className="panel-heading">
                  <h4 className="panel-title">About this plugin</h4>
                </div>
                <div className="panel-body">
                  <p>
                    HelloPlugin subscribes to the <code>autonate.authorize</code> hook and
                    appends a row to its own <code>greetings</code> table on every authorize
                    call. The table lives in the plugin's per-plugin Postgres schema, so
                    only this plugin can write to it.
                  </p>
                  <p className="mb-0">
                    This page itself is shipped <em>by the plugin</em>: a single
                    <code>AddPluginMenuItem</code> call from <code>Configure()</code> places a
                    menu item under "Plugins" with <code>item_type = "page"</code> and
                    <code>contentType = "jsx"</code>. The host compiles the source at
                    runtime and renders it like any other admin page.
                  </p>
                </div>
              </div>

              <div className="panel panel-inverse">
                <div className="panel-heading">
                  <h4 className="panel-title">Example settings (read-only)</h4>
                </div>
                <div className="panel-body">
                  <p className="text-muted small">
                    Plugins can render full React UIs here. State persistence and
                    settings APIs are up to the plugin author — usually via
                    <code>context.Data</code> (the per-plugin schema) plus a hook the
                    plugin author defines.
                  </p>
                  <div className="mb-3">
                    <label htmlFor="tone-select" className="form-label">Greeting tone</label>
                    <select
                      id="tone-select"
                      className="form-select form-select-sm"
                      style={{ maxWidth: "20rem" }}
                      value={tone}
                      onChange={(e) => setTone(e.target.value)}
                    >
                      <option value="friendly">Friendly</option>
                      <option value="formal">Formal</option>
                      <option value="brief">Brief</option>
                    </select>
                  </div>
                  <div className="mb-3">
                    <button
                      type="button"
                      className="btn btn-sm btn-primary me-2"
                      onClick={() => setCount(count + 1)}
                    >
                      Click me
                    </button>
                    <span className="text-muted small">
                      Counter: <strong>{count}</strong> (state is JSX-local, not persisted)
                    </span>
                  </div>
                </div>
              </div>

              <div className="panel panel-inverse">
                <div className="panel-heading">
                  <h4 className="panel-title">Wiring this menu item</h4>
                </div>
                <div className="panel-body">
                  <pre className="bg-light p-3 small mb-0">
        {`context.Menus.AddPluginMenuItem(new NewMenuItem(
            DisplayName: "HelloPlugin Settings",
            ItemType: "page",
            Icon: "fa fa-comment",
            Config: new {
                path = $"/admin/config/plugins/{context.Code}/hello-config",
                contentType = "jsx",
                content = "function Page() { return <h1>Hi</h1>; }"
            }));`}
                  </pre>
                </div>
              </div>
            </>
          );
        }
        """;
}
