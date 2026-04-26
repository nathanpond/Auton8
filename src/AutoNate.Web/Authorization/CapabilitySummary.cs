namespace AutoNate.Web.Authorization;

public sealed record class CapabilitySummary
{
    public required Guid UserId { get; init; }

    public required bool IsSuperAdmin { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Capabilities { get; init; }
}
