namespace AutoNate.Web.Models;

public sealed record LocalUser
{
    public long Id { get; init; }

    public Guid UserId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public DateTimeOffset CreatedDate { get; init; }

    public DateTimeOffset? LastLoginDate { get; init; }

    public string IdpKey { get; init; } = string.Empty;
}
