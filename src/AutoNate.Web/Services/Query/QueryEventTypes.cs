namespace AutoNate.Web.Services.Query;

public static class QueryEventTopic
{
    public const string TopicRoot = "query";
    public const string TopicName = "query.events";
}

public static class QueryResourceKinds
{
    public const string Query = "query";
    public const string SavedQuery = "saved_query";
}

public static class QueryEventTypes
{
    // Fires once per /api/query call (success or validation failure). Details
    // carry the parsed entity name, the query text, row counts, and timing so
    // ops can spot abusive queries.
    public const string Executed = "query.executed";
    public const string Failed = "query.failed";

    // Saved-query lifecycle. Fires post-commit from the /api/saved-queries
    // endpoints. Resource carries id + name; details carry the boolean
    // is_shared flag and (on saved/updated) the underlying query text.
    public const string SavedQuerySaved = "query.saved_query.saved";
    public const string SavedQueryUpdated = "query.saved_query.updated";
    public const string SavedQueryDeleted = "query.saved_query.deleted";
}
