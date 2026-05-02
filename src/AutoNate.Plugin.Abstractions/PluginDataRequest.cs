namespace AutoNate.Plugins.Abstractions;

// Request the host hands a plugin when an admin SPA call hits
// /api/admin/plugins/by-code/{code}/data/{view}. The plugin subscribes to its
// own per-plugin filter hook (HookPoints.PluginDataHookFor(context.Code)),
// inspects View + Query, and returns a PluginDataResponse with whatever JSON
// payload the calling page expects.
//
// Use this as the data side of a JSX page template a plugin ships in
// /PageTemplates: the template uses `api.get(...)` against the host endpoint,
// the host fires this hook, and the plugin returns rows from its own
// plg_<code> schema. No new HTTP routes per plugin, no leaking the plugin
// role to the browser.
public sealed record PluginDataRequest(
    string Code,
    string View,
    IReadOnlyDictionary<string, string> Query);

// Plugin's reply. ContentJson is the raw JSON body the host writes to the
// response (it is NOT re-serialized, so the plugin controls field names and
// shape). StatusCode lets the plugin signal 404/400/etc. Default-construction
// returns 404 with an empty body so the host can hand the unhandled-request
// case back as a clean Not Found.
public sealed record PluginDataResponse
{
    public int StatusCode { get; init; } = 404;
    public string ContentJson { get; init; } = "";
    public string ContentType { get; init; } = "application/json";
}
