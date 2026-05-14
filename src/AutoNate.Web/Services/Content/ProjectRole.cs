namespace AutoNate.Web.Services.Content;

// Three roles a user can hold on a single project. Stored as lowercase
// strings in project_members.role; the enum is the in-process representation.
//
// Action bundles (subject to deletions_locked stripping Delete):
//   Owner       — full control + member management + deletion-lock toggle
//   Contributor — full CRUD on content; no member management, no lock toggle
//   Viewer      — read-only
public enum ProjectRole
{
    Viewer = 0,
    Contributor = 1,
    Owner = 2
}

public static class ProjectRoleNames
{
    public const string Owner = "owner";
    public const string Contributor = "contributor";
    public const string Viewer = "viewer";

    public static string ToWire(ProjectRole role) => role switch
    {
        ProjectRole.Owner => Owner,
        ProjectRole.Contributor => Contributor,
        ProjectRole.Viewer => Viewer,
        _ => Viewer
    };

    public static ProjectRole? TryParse(string? wire) => wire switch
    {
        Owner => ProjectRole.Owner,
        Contributor => ProjectRole.Contributor,
        Viewer => ProjectRole.Viewer,
        _ => null
    };
}
