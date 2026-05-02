using LocalUserModel = AutoNate.Web.Models.LocalUser;
using RoleModel = AutoNate.Web.Models.Authorization.Role;
using RecordTypeModel = AutoNate.Web.Models.Records.RecordType;
using RecordModel = AutoNate.Web.Models.Records.Record;
using WorkflowModelModel = AutoNate.Web.Models.WorkflowModel;

namespace AutoNate.Web.Authorization.EntityTypes;

// Phase 1 entity-type registrations. These are pure metadata: kind name, CLR
// types, action vocabulary, and the tag set selectors are allowed to reference.
// Phase 4 introduces predicate compilation per kind.
public static class CoreEntityTypes
{
    public static IReadOnlyList<IEntityType> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<IEntityType>> _all = new(() =>
        new IEntityType[]
        {
            User!, Group!, Role!, RecordType!, Record!,
            WorkflowModel!, WorkflowExecution!, WorkflowTask!, Plugin!
        });

    public static EntityTypeDefinition User { get; } = new(
        kind: EntityKinds.User,
        clrType: typeof(LocalUserModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Deactivate, Actions.Unlock },
        tags: new[] { "username", "email", "supervisor", "manager" });

    // The Group CLR model arrives in Phase 3. The kind is registered now so
    // selectors and grants can already reference `/group/...` without changing
    // the registry shape.
    public static EntityTypeDefinition Group { get; } = new(
        kind: EntityKinds.Group,
        clrType: typeof(object),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Edit, Actions.Delete,
            Actions.AddMember, Actions.RemoveMember
        },
        tags: new[] { "name", "member" });

    public static EntityTypeDefinition Role { get; } = new(
        kind: EntityKinds.Role,
        clrType: typeof(RoleModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete, Actions.Assign },
        tags: new[] { "name" });

    public static EntityTypeDefinition RecordType { get; } = new(
        kind: EntityKinds.RecordType,
        clrType: typeof(RecordTypeModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Edit, Actions.Delete,
            Actions.CreateRecord, Actions.DefineFields
        },
        tags: new[] { "shortcode", "archived" });

    public static EntityTypeDefinition Record { get; } = new(
        kind: EntityKinds.Record,
        clrType: typeof(RecordModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Edit, Actions.Delete,
            Actions.Assign, Actions.Comment, Actions.Archive
        },
        tags: new[] { "recordtype", "status", "assignee", "creator", "duedate" });

    public static EntityTypeDefinition WorkflowModel { get; } = new(
        kind: EntityKinds.WorkflowModel,
        clrType: typeof(WorkflowModelModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Edit, Actions.Delete,
            Actions.Publish, Actions.Start, Actions.Pause
        },
        tags: new[] { "processkey", "draft", "published" });

    // Workflow executions and tasks live in Flowable. Phase 6 introduces the
    // resolver that fetches metadata; for Phase 1 we register kind-only.
    public static EntityTypeDefinition WorkflowExecution { get; } = new(
        kind: EntityKinds.WorkflowExecution,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[]
        {
            Actions.View, Actions.Cancel, Actions.Delete, Actions.Signal, Actions.Terminate, Actions.Override, Actions.MoveState, Actions.DeleteAll
        },
        tags: new[] { "processkey", "definitionkey", "startedby", "assignee" });

    public static EntityTypeDefinition WorkflowTask { get; } = new(
        kind: EntityKinds.WorkflowTask,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[]
        {
            Actions.View, Actions.Claim, Actions.Assign,
            Actions.Complete, Actions.Unclaim
        },
        tags: new[] { "processkey", "definitionkey", "assignee", "candidategroup" });

    // Single coarse Manage action gates list/view/upload/enable/disable/delete
    // for plugins. Granular split is a v2 conversation if it ever comes up.
    public static EntityTypeDefinition Plugin { get; } = new(
        kind: EntityKinds.Plugin,
        clrType: typeof(object),
        idClrType: typeof(Guid),
        actions: new[] { Actions.Manage },
        tags: new[] { "name", "version", "status" });
}
