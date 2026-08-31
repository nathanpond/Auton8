using LocalUserModel = AutoNate.Web.Models.LocalUser;
using RoleModel = AutoNate.Web.Models.Authorization.Role;
using RecordTypeModel = AutoNate.Web.Models.Records.RecordType;
using RecordModel = AutoNate.Web.Models.Records.Record;
using WorkflowModelModel = AutoNate.Web.Models.WorkflowModel;
using FormModel = AutoNate.Web.Models.Forms.Form;
using ExternalConnectionModel = AutoNate.Web.Persistence.Scaffolded.ExternalConnection;
using SystemIssueModel = AutoNate.Web.Services.SystemIssues.SystemIssue;
using ProjectModel = AutoNate.Web.Persistence.Scaffolded.Project;
using CabinetModel = AutoNate.Web.Persistence.Scaffolded.Cabinet;
using NotebookModel = AutoNate.Web.Persistence.Scaffolded.Notebook;
using PageModel = AutoNate.Web.Persistence.Scaffolded.Page;
using DocumentModel = AutoNate.Web.Persistence.Scaffolded.Document;
using FolderModel = AutoNate.Web.Persistence.Scaffolded.Folder;

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
            Form!, ExternalConnection!, SystemIssue!, SiteConfig!,
            Project!, Cabinet!, Notebook!, Page!, Document!, Folder!
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

    // Workflow executions and tasks live in Flowable, mirrored into the
    // workflow_execution_cache / workflow_task_cache tables that the selector
    // compilers query. The `tags` arrays here must match the predicates each
    // cache compiler handles — if a compiler accepts a tag that's not listed
    // here, the SPA grant authoring picker won't surface it (manually-typed
    // selectors still work, but admins lose discoverability).
    public static EntityTypeDefinition WorkflowExecution { get; } = new(
        kind: EntityKinds.WorkflowExecution,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[]
        {
            Actions.View, Actions.Cancel, Actions.Delete, Actions.Override, Actions.MoveState, Actions.DeleteAll
        },
        // Mirrors WorkflowExecutionCacheSelectorCompiler.CompileExpr.
        tags: new[] { "processkey", "definitionkey", "startedby", "status", "tenant" });

    public static EntityTypeDefinition WorkflowTask { get; } = new(
        kind: EntityKinds.WorkflowTask,
        clrType: typeof(object),
        idClrType: typeof(string),
        actions: new[] { Actions.View, Actions.Complete },
        // Mirrors WorkflowTaskCacheSelectorCompiler.CompileExpr.
        tags: new[] { "processkey", "definitionkey", "assignee", "candidateuser", "candidategroup" });

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

    // Content hierarchy kinds. Authorization for these is handled by
    // IContentAuthorizer (project-role baseline + closest-ancestor override
    // through content_ancestors). Registering them here makes their (kind,
    // action) pairs grantable through the standard Grants admin page; the
    // grants themselves are still stored in permission_grants. Selector
    // predicates / tags are not supported in this phase — selectors must be
    // path-only (e.g. /cabinet/{id}). Note is intentionally absent — notes
    // inherit their page's gate.
    //
    // Action vocabulary intentionally narrow:
    //   - Create is NOT declared on any of these kinds. Creation gates on
    //     the parent resource's Edit (design D9): cabinets gate on
    //     Project.Edit, notebooks on Cabinet.Edit, pages on Notebook.Edit
    //     (and Page.Edit when nesting). Project creation is gated by
    //     authentication alone (the creator becomes Owner).
    //   - Archive is NOT declared. The "archive" toggle is a PATCH on the
    //     same resource gated by Edit; there is no separate enforcement
    //     path that would consult a (kind, Archive) grant. If a narrower
    //     archive-vs-edit split is wanted, carve a dedicated PATCH endpoint
    //     and re-add Archive to actions[] at the same time.
    //   - Page.Delete IS declared: it gates page-version and page-attachment
    //     deletes (PageVersionEndpoints, PageAttachmentEndpoints). The page
    //     ROW delete is owner-only via IContentAuthorizer.IsProjectOwnerAsync
    //     and intentionally narrower than this Contributor-class permission.
    public static EntityTypeDefinition Project { get; } = new(
        kind: EntityKinds.Project,
        clrType: typeof(ProjectModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition Cabinet { get; } = new(
        kind: EntityKinds.Cabinet,
        clrType: typeof(CabinetModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition Notebook { get; } = new(
        kind: EntityKinds.Notebook,
        clrType: typeof(NotebookModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition Page { get; } = new(
        kind: EntityKinds.Page,
        clrType: typeof(PageModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Delete },
        tags: Array.Empty<string>());

    // Document and Folder are enforced on 22 routes and honoured by
    // ContentAuthorizer's /document/… and /folder/… selectors, but were absent
    // from this registry — which is what /api/admin/registry serves to the
    // Grants admin picker. So grants the runtime *does* honour could not be
    // discovered or authored from the standard page; the only way in was
    // ContentPermissionOverrideEndpoints, which carried its own hardcoded
    // action lists as a second source of truth (#25).
    //
    // The action lists here are those same lists, so the picker and the
    // override endpoints agree by construction: Comment is document-only
    // (folders hold no discussion), Create is folder-only (a folder is where a
    // document gets created).
    public static EntityTypeDefinition Document { get; } = new(
        kind: EntityKinds.Document,
        clrType: typeof(DocumentModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Comment, Actions.Edit },
        tags: Array.Empty<string>());

    public static EntityTypeDefinition Folder { get; } = new(
        kind: EntityKinds.Folder,
        clrType: typeof(FolderModel),
        idClrType: typeof(Guid),
        actions: new[] { Actions.View, Actions.Edit, Actions.Create },
        tags: Array.Empty<string>());
}
