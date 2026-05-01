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
}
