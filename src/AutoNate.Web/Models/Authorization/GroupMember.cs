namespace AutoNate.Web.Models.Authorization;

public sealed record class GroupMember
{
    public Guid GroupId { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset AddedAtUtc { get; init; }

    public Guid AddedBy { get; init; }
}
