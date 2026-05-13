using LocalUserModel = AutoNate.Web.Models.LocalUser;
using RoleModel = AutoNate.Web.Models.Authorization.Role;
using RecordTypeModel = AutoNate.Web.Models.Records.RecordType;
using RecordModel = AutoNate.Web.Models.Records.Record;
using WorkflowModelModel = AutoNate.Web.Models.WorkflowModel;
using FormModel = AutoNate.Web.Models.Forms.Form;
using ExternalConnectionModel = AutoNate.Web.Persistence.Scaffolded.ExternalConnection;
using SystemIssueModel = AutoNate.Web.Services.SystemIssues.SystemIssue;

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
            WorkflowModel!, WorkflowExecution!, WorkflowTask!, Plugin!,
            Form!, ExternalConnection!, SystemIssue!, SiteConfig!
        });

    public static EntityTypeDefinition User { get; } = new(
        kind: EntityKinds.User,
        clrType: typeof(LocalUserModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Create, Actions.Edit, Actions.Delete, Actions.Unlock },
        tags: Array.Empty<string>());

    // The Group CLR model arrives in Phase 3. The kind is registered now so
    // selectors and grants can already reference `/group/...` without changing
    // the registry shape.
    public static EntityTypeDefinition Group { get; } = new(
        kind: EntityKinds.Group,
        clrType: typeof(object),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.AddMember, Actions.RemoveMember
        },
        tags: new[] { "name", "member" });

    public static EntityTypeDefinition Role { get; } = new(
        kind: EntityKinds.Role,
        clrType: typeof(RoleModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Create, Actions.Edit, Actions.Delete, Actions.Assign },
        tags: new[] { "name" });

    public static EntityTypeDefinition RecordType { get; } = new(
        kind: EntityKinds.RecordType,
        clrType: typeof(RecordTypeModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Create, Actions.Edit, Actions.Delete,
            Actions.DefineFields
        },
        tags: new[] { "shortcode", "archived" });

    public static EntityTypeDefinition Record { get; } = new(
        kind: EntityKinds.Record,
        clrType: typeof(RecordModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Create, Actions.Edit,
            Actions.Assign, Actions.Comment, Actions.Archive,
            // `Delete` is the hard-delete (record + cascaded edges/comments/
            // history/watches) — distinct from `Archive` which is the soft
            // tombstone. Granted separately so admins can hand out routine
            // archive without unlocking irreversible purges.
            Actions.Delete
        },
        tags: new[] { "recordtype", "status", "assignee", "creator" });

    // Actions.Start uses the processKey route token for instance gating (see
    // WorkflowEndpoints.cs); Pause/Resume both use the Pause action so a
    // single grant covers the lifecycle pair. Delete hard-deletes the
    // workflow_models row (versions cascade) — Flowable deployments are not
    // auto-undeployed, so operators should pause + handle Flowable cleanup
    // first when removing a published workflow.
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
            Actions.View, Actions.Cancel, Actions.Delete, Actions.Override, Actions.MoveState, Actions.DeleteAll
        },
        tags: new[] { "processkey", "definitionkey", "startedby" });

    public static EntityTypeDefinition WorkflowTask { get; } = new(
        kind: EntityKinds.WorkflowTask,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[] { Actions.View, Actions.Complete },
        tags: new[] { "processkey", "definitionkey", "assignee" });

    // Single coarse Manage action gates list/view/upload/enable/disable/delete
    // for plugins. Granular split is a v2 conversation if it ever comes up.
    public static EntityTypeDefinition Plugin { get; } = new(
        kind: EntityKinds.Plugin,
        clrType: typeof(object),
        idClrType: typeof(Guid),
        actions: new[] { Actions.Manage },
        tags: Array.Empty<string>());

    // Admin-authored JSX forms. Drafts live in `forms`, every save snapshots
    // into `form_versions`. Publish flips `is_draft=false` and points
    // `published_version_number` at the active version.
    public static EntityTypeDefinition Form { get; } = new(
        kind: EntityKinds.Form,
        clrType: typeof(FormModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Create, Actions.Edit,
            Actions.Delete, Actions.Publish
        },
        tags: new[] { "shortcode", "siteAvailable", "draft", "published" });

    // Outbound integration config registered through the External Connections
    // admin page. Manage gates write paths (create/edit/delete/test/set-default);
    // View gates list and read so admins without write authority can still
    // inspect what's configured.
    public static EntityTypeDefinition ExternalConnection { get; } = new(
        kind: EntityKinds.ExternalConnection,
        clrType: typeof(ExternalConnectionModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Manage },
        tags: Array.Empty<string>());

    // Self-healing platform: rows in system_issues. View gates list+detail,
    // Acknowledge/Resolve gate the operator-action endpoints, Remediate gates
    // the on-demand POST /system-issues/{id}/remediate endpoint. EntityKinds
    // notes this is a kind-only gate (no instance authorizer) — every issue
    // is administrative and we currently grant access at the kind level.
    public static EntityTypeDefinition SystemIssue { get; } = new(
        kind: EntityKinds.SystemIssue,
        clrType: typeof(SystemIssueModel),
        idClrType: typeof(Guid),
        actions: new[]
        {
            Actions.View, Actions.Acknowledge, Actions.Resolve, Actions.Remediate
        },
        tags: Array.Empty<string>());

    // Platform configuration (menus, site settings, appearance, role/grant
    // admin, debug surfaces). Kind-only enforcement throughout; a single
    // SiteConfig:Edit grant unlocks every authoring screen, View unlocks
    // read-only debug + admin reads.
    public static EntityTypeDefinition SiteConfig { get; } = new(
        kind: EntityKinds.SiteConfig,
        clrType: typeof(object),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete },
        tags: Array.Empty<string>());
}
