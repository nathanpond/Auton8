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
}
