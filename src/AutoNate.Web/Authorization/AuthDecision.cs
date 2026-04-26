namespace AutoNate.Web.Authorization;

public enum AuthEffect
{
    Deny = 0,
    Allow = 1
}

public sealed record class AuthDecision
{
    public required AuthEffect Effect { get; init; }

    public required string Reason { get; init; }

    public bool IsAllowed => Effect == AuthEffect.Allow;

    public static AuthDecision Allow(string reason) =>
        new() { Effect = AuthEffect.Allow, Reason = reason };

    public static AuthDecision Deny(string reason) =>
        new() { Effect = AuthEffect.Deny, Reason = reason };
}
