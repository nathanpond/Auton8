namespace AutoNate.Web.Authorization;

public static class Actions
{
    public const string Wildcard = "*";

    public const string View = "view";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Create = "create";
    public const string List = "list";

    public const string Archive = "archive";
    public const string Restore = "restore";

    public const string Assign = "assign";
    public const string AddMember = "addmember";
    public const string RemoveMember = "removemember";

    public const string DefineFields = "definefields";

    public const string Comment = "comment";

    public const string Unlock = "unlock";

    public const string Publish = "publish";
    public const string Start = "start";
    public const string Pause = "pause";

    public const string Cancel = "cancel";
    public const string DeleteAll = "deleteall";

    public const string Complete = "complete";

    public const string Override = "override";

    public const string MoveState = "movestate";

    public const string Manage = "manage";

    // Documents — re-resolve live data bindings (record fields, AQL
    // tables, etc.) embedded in the document body. Surfaced as a
    // separate action from Edit so we can let a Commenter trigger a
    // "Refresh all" without giving them body edits — they're not
    // changing the document itself, just asking the server to recompute
    // the cached values. v1 wires it into the Editor and Owner
    // role-bundle only; loosen later if the commenter UX needs it.
    public const string RefreshBindings = "refreshbindings";

    // System issues lifecycle (Phase 3 wires Acknowledge/Resolve into the API;
    // Remediate gates the on-demand POST /system-issues/{id}/remediate endpoint
    // landing in Phase 4. Read is the Phase 1 list/detail gate).
    public const string Acknowledge = "acknowledge";
    public const string Resolve = "resolve";
    public const string Remediate = "remediate";

    // Data Stores & Analytics Pipeline (docs/plans/2026-05-30-data-stores-implementation.md).
    // Refresh: trigger a cached dataset / connector pull on demand.
    // Run: execute a pipeline (manual kick-off, in addition to scheduled runs).
    // Schedule: edit refresh frequency / pipeline cron.
    // ExecuteUnsafe: opt out of the Pyodide/V8-isolate sandbox on user-authored
    //   transformer/analyzer code — gates the full-CPython runner path.
    // Share: issue a share token for a saved query (analog of the document
    //   share surface).
    // Connect: invoke a data connector's "test connection" probe.
    public const string Refresh = "refresh";
    public const string Run = "run";
    public const string Schedule = "schedule";
    public const string ExecuteUnsafe = "executeunsafe";
    public const string Share = "share";
    public const string Connect = "connect";
}
