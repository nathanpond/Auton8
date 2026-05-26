namespace AutoNate.Web.Services.Content;

// Four roles a user can hold on a single project. Stored as lowercase
// strings in project_members.role; the enum is the in-process representation.
// Enum ints are ordered by privilege ascending — code never persists the int,
// so adding Commenter between Viewer and Contributor renumbers but stays safe.
//
// Action bundles (subject to deletions_locked stripping Delete):
//   Owner       — full control + member management + deletion-lock toggle
//   Contributor — full CRUD on content; no member management, no lock toggle
//   Commenter   — read + comment (no edits)
//   Viewer      — read-only
public enum ProjectRole
{
    Viewer = 0,
    Commenter = 1,
    Contributor = 2,
    Owner = 3
}

public static class ProjectRoleNames
{
    public const string Owner = "owner";
    public const string Contributor = "contributor";
    public const string Commenter = "commenter";
    public const string Viewer = "viewer";

    public static string ToWire(ProjectRole role) => role switch
    {
        ProjectRole.Owner => Owner,
        ProjectRole.Contributor => Contributor,
        ProjectRole.Commenter => Commenter,
        ProjectRole.Viewer => Viewer,
        _ => Viewer
    };

    public static ProjectRole? TryParse(string? wire) => wire switch
    {
        Owner => ProjectRole.Owner,
        Contributor => ProjectRole.Contributor,
        Commenter => ProjectRole.Commenter,
        Viewer => ProjectRole.Viewer,
        _ => null
    };
}
