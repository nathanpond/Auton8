namespace AutoNate.Web.Services.Records;

// Topic + event-type names for the record-schema.events bus topic. Phase 3 of
// the audit-events plan introduces this domain — every change to record-type
// schemas, fields, edge-type schemas, edge instances, and record comments
// publishes an event here. Distinct from record.events (which carries record
// instance lifecycle) so subscribers can pick the granularity they care about.
public static class RecordSchemaEventTopic
{
    public const string TopicRoot = "record-schema";
    public const string TopicName = "record-schema.events";
}

public static class RecordSchemaResourceKinds
{
    public const string RecordType = "record-type";
    public const string RecordTypeField = "record-type.field";
    public const string RecordEdgeType = "record-edge-type";
    public const string RecordEdgeTypeField = "record-edge-type.field";
    public const string RecordEdge = "record-edge";
    public const string RecordComment = "record.comment";
}

public static class RecordSchemaEventTypes
{
    // Record-type lifecycle
    public const string RecordTypeCreated = "record-type.created";
    public const string RecordTypeUpdated = "record-type.updated";
    public const string RecordTypeArchived = "record-type.archived";
    public const string RecordTypeRestored = "record-type.restored";

    // Record-type field lifecycle
    public const string RecordTypeFieldCreated = "record-type.field.created";
    public const string RecordTypeFieldUpdated = "record-type.field.updated";
    public const string RecordTypeFieldArchived = "record-type.field.archived";
    public const string RecordTypeFieldRestored = "record-type.field.restored";

    // Record-edge-type lifecycle
    public const string RecordEdgeTypeCreated = "record-edge-type.created";
    public const string RecordEdgeTypeUpdated = "record-edge-type.updated";
    public const string RecordEdgeTypeArchived = "record-edge-type.archived";
    public const string RecordEdgeTypeRestored = "record-edge-type.restored";

    // Record-edge-type field lifecycle
    public const string RecordEdgeTypeFieldCreated = "record-edge-type.field.created";
    public const string RecordEdgeTypeFieldUpdated = "record-edge-type.field.updated";
    public const string RecordEdgeTypeFieldDeleted = "record-edge-type.field.deleted";

    // Record edge instance lifecycle
    public const string RecordEdgeCreated = "record-edge.created";
    public const string RecordEdgeDeleted = "record-edge.deleted";

    // Record comment lifecycle
    public const string RecordCommentCreated = "record.comment.created";
    public const string RecordCommentEdited = "record.comment.edited";
    public const string RecordCommentDeleted = "record.comment.deleted";

    // View events (Phase 4)
    public const string RecordTypeViewed = "record-type.viewed";
    public const string RecordTypeListViewed = "record-type.list.viewed";
    public const string RecordTypeAuditViewed = "record-type.audit.viewed";
    public const string RecordTypeFieldListViewed = "record-type.field.list.viewed";
    public const string RecordTypeFieldViewed = "record-type.field.viewed";
    public const string RecordEdgeTypeListViewed = "record-edge-type.list.viewed";
    public const string RecordEdgeTypeViewed = "record-edge-type.viewed";
    public const string RecordEdgeTypeFieldListViewed = "record-edge-type.field.list.viewed";
    public const string RecordEdgeListViewed = "record-edge.list.viewed";
    public const string RecordEdgeTraversed = "record-edge.traversed";
    public const string RecordCommentListViewed = "record.comment.list.viewed";
    public const string RecordCommentRevisionsViewed = "record.comment.revisions.viewed";
}
