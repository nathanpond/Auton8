namespace AutoNate.Web.Authorization.Selectors;

public sealed record class SelectorAst
{
    public required PathNode Path { get; init; }

    public PredicateNode? Predicate { get; init; }
}

public sealed record class PathNode
{
    public required IReadOnlyList<string> Kinds { get; init; }

    public IReadOnlyList<string>? Ids { get; init; }

    public bool KindsAreWildcard => Kinds.Count == 1 && Kinds[0] == SelectorTokens.Wildcard;

    public bool IdsAreWildcard => Ids is { Count: 1 } list && list[0] == SelectorTokens.Wildcard;
}

public sealed record class PredicateNode
{
    public required IReadOnlyList<PredicateExpr> Expressions { get; init; }
}

public abstract record class PredicateExpr;

public sealed record class TagExpr : PredicateExpr
{
    public required string Tag { get; init; }

    public required ValueNode Value { get; init; }

    public PredicateNode? Nested { get; init; }
}

public sealed record class ScopeExpr : PredicateExpr
{
    public required string Tag { get; init; }

    public required string Qualifier { get; init; }
}

public abstract record class ValueNode;

public sealed record class LiteralValue : ValueNode
{
    public required string Text { get; init; }
}

public sealed record class WildcardValue : ValueNode;

public sealed record class CurrentUserValue : ValueNode
{
    public string? PinnedId { get; init; }
}

public sealed record class QualifiedValue : ValueNode
{
    public required string Qualifier { get; init; }

    public required string Name { get; init; }
}

internal static class SelectorTokens
{
    public const string Wildcard = "*";
    public const string CurrentUser = "user";
}
