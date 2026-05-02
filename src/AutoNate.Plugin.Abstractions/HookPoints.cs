namespace AutoNate.Plugins.Abstractions;

public static class HookPoints
{
    public const string AuthorizeAuthorize = "autonate.authorize";

    // Fired as an async action whenever the host publishes an audit event onto
    // the bus. The single argument is an AuditEventNotification carrying the
    // fully-formed envelope plus a flat view of the AuditContext. Subscribers
    // run after the envelope has been enqueued to the outbox and run on the
    // request thread, so do anything I/O-heavy off the hot path.
    public const string AuditEventPublished = "autonate.audit.event_published";

    // Per-plugin filter hook fired by the host when a SPA call reaches
    // /api/admin/plugins/by-code/{code}/data/{view}. The plugin owns the
    // namespace (one hook per plugin code) so handlers don't need to filter
    // for their own code; just subscribe and return a typed response. See
    // PluginDataRequest / PluginDataResponse for the payload shape.
    public static string PluginDataHookFor(string pluginCode) =>
        "autonate.plugin.data." + pluginCode;
}
