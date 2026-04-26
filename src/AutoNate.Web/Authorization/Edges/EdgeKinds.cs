namespace AutoNate.Web.Authorization.Edges;

public static class EdgeKinds
{
    // User → Resource: the user who created the resource.
    public const string Creator = "creator";

    // User → Resource: the user is assigned to the resource.
    public const string Assignee = "assignee";

    // User → User: the source user supervises the target user.
    public const string Supervisor = "supervisor";

    // User → Resource: the user owns the resource (broader than creator).
    public const string Owner = "owner";
}
