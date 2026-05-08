namespace AutoNate.Web.Services.ExternalConnections;

// Topic + event-type names for the external-connections.events bus topic. An
// audit consumer can answer "who registered or rotated which integration
// connection, when, and against what kind?" by reading this topic alone.
// Plaintext api keys never appear in any payload — only the secret fingerprint
// (first/last 4 chars + sha256 prefix) does, so the audit log is safe to
// retain alongside lower-privileged operational logs.
public static class ExternalConnectionEventTopic
{
    public const string TopicRoot = "external-connections";
    public const string TopicName = "external-connections.events";
    public const string ResourceKind = "external-connection";
}

public static class ExternalConnectionEventTypes
{
    public const string Created = "external_connection.created";
    public const string Updated = "external_connection.updated";
    public const string Deleted = "external_connection.deleted";
    public const string Viewed = "external_connection.viewed";
    public const string ListViewed = "external_connection.list_viewed";
    public const string Tested = "external_connection.tested";
    public const string SetDefault = "external_connection.set_default";
}
