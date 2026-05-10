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

    // System issues lifecycle (Phase 3 wires Acknowledge/Resolve into the API;
    // Remediate gates the on-demand POST /system-issues/{id}/remediate endpoint
    // landing in Phase 4. Read is the Phase 1 list/detail gate).
    public const string Acknowledge = "acknowledge";
    public const string Resolve = "resolve";
    public const string Remediate = "remediate";
}
