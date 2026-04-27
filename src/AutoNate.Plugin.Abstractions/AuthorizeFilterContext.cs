using System.Security.Claims;

namespace AutoNate.Plugins.Abstractions;

public sealed record AuthorizeFilterContext
{
    public required ClaimsPrincipal Actor { get; init; }
    public required string Action { get; init; }
    public required EntityRefDto Target { get; init; }
    public required AuthDecisionDto CurrentDecision { get; init; }
}

public readonly record struct EntityRefDto(string Kind, string Id);

public sealed record AuthDecisionDto
{
    public required AuthEffectDto Effect { get; init; }
    public required string Reason { get; init; }
}

public enum AuthEffectDto
{
    Deny = 0,
    Allow = 1,
}
