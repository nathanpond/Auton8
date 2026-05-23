namespace AutoNate.Web.Services.Query;

public static class QueryEventTopic
{
    public const string TopicRoot = "query";
    public const string TopicName = "query.events";
}

public static class QueryResourceKinds
{
    public const string Query = "query";
}

public static class QueryEventTypes
{
    // Fires once per /api/query call (success or validation failure). Details
    // carry the parsed entity name, the query text, row counts, and timing so
    // ops can spot abusive queries.
    public const string Executed = "query.executed";
    public const string Failed = "query.failed";
}
