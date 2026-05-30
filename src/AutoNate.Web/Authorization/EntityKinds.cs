namespace AutoNate.Web.Authorization;

public static class EntityKinds
{
    public const string User = "user";
    public const string Group = "group";
    public const string Role = "role";
    public const string RecordType = "recordtype";
    public const string Record = "record";
    public const string WorkflowModel = "workflowmodel";
    public const string WorkflowExecution = "workflowexecution";
    public const string WorkflowTask = "workflowtask";
    public const string SiteConfig = "siteconfig";
    public const string Plugin = "plugin";

    // Self-healing platform: rows in system_issues. Kind-only gate (no
    // instance authorizer) — every issue is administrative and we
    // currently grant access at the kind level. If per-row visibility is
    // ever needed (e.g. plugin-scoped issues), add an instance authorizer
    // alongside this kind.
    public const string SystemIssue = "systemissue";

    public const string Form = "form";

    // Generic kind-discriminated outbound integration config (LLM provider api
    // keys today, future SMTP/S3/IdP). Single coarse Manage action gates
    // create/edit/delete/test/set-default; View gates list+read.
    public const string ExternalConnection = "externalconnection";

    // Content hierarchy: Project → Cabinet → Notebook → Page → Note. Permission
    // checks for these kinds are routed through IContentAuthorizer rather than
    // the generic IAuthorizer because effective access combines a project-role
    // baseline (project_members table) with closest-ancestor overrides from
    // permission_grants. Notes are intentionally not a permissionable kind —
    // they inherit their page's access.
    public const string Project = "project";
    public const string Cabinet = "cabinet";
    public const string Notebook = "notebook";
    public const string Page = "page";

    // Documents subsystem: Project → Folder (self-nesting, unlimited depth) →
    // Document. Same IContentAuthorizer plumbing as the notes hierarchy
    // (closure rows in content_ancestors). Documents land in a later phase;
    // the Folder kind ships in Phase 1 so the folder tree + override grants
    // are functional without a document editor.
    public const string Folder = "folder";
    public const string Document = "document";

    // Data Stores & Analytics Pipeline (docs/plans/2026-05-30-data-stores-implementation.md).
    // Selector compilers, IAuthorizer<T> instances, and [RequirePermission] filters
    // for each of these land alongside the endpoint that gates the kind, not all at
    // once — see the plan's per-phase file list.
    public const string DataStore = "datastore";
    public const string DataConnector = "dataconnector";
    public const string Dataset = "dataset";
    public const string Transformer = "transformer";
    public const string Analyzer = "analyzer";
    public const string Pipeline = "pipeline";
    public const string PipelineRun = "pipelinerun";
}
