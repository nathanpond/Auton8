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

    // Workflow script identity (#153). Gates authoring a script task that
    // declares `runAs="system"`, which bypasses individual permission checks
    // when identity resolution lands.
    //
    // Its own action rather than a reuse of Publish or Edit, because it must be
    // grantable on its own: most authors should be able to build and publish
    // workflows without ever being able to write a step that runs as the
    // system. It never bypasses the sandbox — process variables and the host
    // API remain the only reachable surface either way.
    //
    // NOT enforced by the registry (see the add-permission-gate skill): the
    // enforcement is the check in the publish handler, which reads the XML.
    // A hidden control in the studio is not a gate.
    public const string ElevateScript = "elevatescript";
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
    // There is deliberately no "execute unsafe" action here.
    //
    // One existed: `executeunsafe` gated an `is_unsafe` flag on user-authored
    // transformer code, planned to select a full-CPython runner instead of the
    // sandbox. That runner was never built, so the flag was inert and the
    // permission protected nothing — an admin granted it gained no capability.
    // Both were removed in #190 rather than left advertising a guarantee that
    // did not exist.
    //
    // The direction of travel is the opposite one: the Python sandbox has been
    // hardened repeatedly, and BPMN script tasks now execute in it too (#147).
    // Reintroducing a sandbox opt-out is a decision to take deliberately, not
    // by reviving a constant.
    // Share: issue a share token for a saved query (analog of the document
    //   share surface).
    // Connect: invoke a data connector's "test connection" probe.
    public const string Refresh = "refresh";
    public const string Run = "run";
    public const string Schedule = "schedule";
    public const string Share = "share";
    public const string Connect = "connect";
}
