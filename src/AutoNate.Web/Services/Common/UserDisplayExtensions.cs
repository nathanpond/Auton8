namespace AutoNate.Web.Services.Common;

public static class UserDisplayExtensions
{
    // Composes a user's display name from first/last/username with a final
    // fallback. "First Last" wins when either name part is set; otherwise the
    // username; otherwise `fallback`. The default fallback is "Someone" because
    // that's what the first caller (the page-share notification body) needed —
    // other callers (e.g. the notes query entity) pass a non-empty username
    // and never reach it.
    public static string GetDisplayName(
        string? firstName,
        string? lastName,
        string? username,
        string fallback = "Someone")
    {
        var combined = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(combined)) return combined;
        if (!string.IsNullOrWhiteSpace(username)) return username;
        return fallback;
    }
}
