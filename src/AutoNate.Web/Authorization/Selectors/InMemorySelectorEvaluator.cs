namespace AutoNate.Web.Authorization.Selectors;

// Walks a SelectorAst against a flat key/value "facts" dictionary describing
// a single entity. Used by external-system kinds (Flowable) where the entity
// can't be expressed as an EF queryable. The actor's user id resolves the
// `=user` value sentinel.
//
// Multi-hop predicates (e.g. `[assignee=user[supervisor=user]]`) need the
// graph: "is the assignee a user the actor supervises?" Rather than reach
// back into the database from inside an in-memory evaluator, callers
// pre-load the actor's outbound user→user edges keyed by edge_kind and
// hand them in. The evaluator answers nested predicates from that map.
public sealed class InMemorySelectorEvaluator
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyEdges =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    private readonly Guid _actorUserId;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _actorOutboundUserEdges;

    public InMemorySelectorEvaluator(Guid actorUserId)
        : this(actorUserId, EmptyEdges) { }

    public InMemorySelectorEvaluator(
        Guid actorUserId,
        IReadOnlyDictionary<string, IReadOnlySet<string>> actorOutboundUserEdges)
    {
        ArgumentNullException.ThrowIfNull(actorOutboundUserEdges);
        _actorUserId = actorUserId;
        _actorOutboundUserEdges = actorOutboundUserEdges;
    }

    public bool Matches(SelectorAst ast, string id, IReadOnlyDictionary<string, string?> facts)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(facts);

        // Path-level id filter (kind matching is the caller's job).
        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                return false;
            }
        }

        if (ast.Predicate is null)
        {
            return true;
        }

        foreach (var expr in ast.Predicate.Expressions)
        {
            if (!EvalExpr(expr, facts))
            {
                return false;
            }
        }

        return true;
    }

    private bool EvalExpr(PredicateExpr expr, IReadOnlyDictionary<string, string?> facts)
    {
        switch (expr)
        {
            case TagExpr tag:
                return EvalTag(tag, facts);
            case ScopeExpr:
                // Scope expressions are reserved for the structured-grammar form
                // (`tag:qualifier`); selectors authored today use the tag form
                // instead, so we fail closed here.
                return false;
            default:
                return false;
        }
    }

    private bool EvalTag(TagExpr tag, IReadOnlyDictionary<string, string?> facts)
    {
        var actual = facts.TryGetValue(tag.Tag, out var v) ? v : null;

        if (tag.Nested is null)
        {
            // Leaf predicate — match the fact value directly against whatever
            // the selector specified.
            return tag.Value switch
            {
                WildcardValue => actual is not null,
                CurrentUserValue userVal => string.Equals(
                    actual,
                    userVal.PinnedId ?? _actorUserId.ToString(),
                    StringComparison.OrdinalIgnoreCase),
                LiteralValue lit => string.Equals(actual, lit.Text, StringComparison.OrdinalIgnoreCase),
                // role:supervisor and similar qualified lookups need the
                // relational graph; not modeled by the in-memory evaluator.
                QualifiedValue => false,
                _ => false
            };
        }

        // Multi-hop: the outer `=user[…]` identifies *some* user U; the nested
        // predicate constrains U via the actor's outbound user edges. We
        // mirror RecordSelectorCompiler here — when nested predicates exist
        // the outer value must be a user reference but it does NOT need to
        // equal the actor. The actor is the *subject* of the nested edge,
        // not the value of the outer tag.
        if (tag.Value is not CurrentUserValue)
        {
            return false;
        }

        if (actual is null)
        {
            return false;
        }

        return EvalNestedUserPredicate(tag.Nested, actual);
    }

    // Supports a single-expression nested predicate of the shape
    // `<innerEdgeKind>=user`, which resolves to "the actor has an outbound
    // <innerEdgeKind> edge to the user just matched."
    private bool EvalNestedUserPredicate(PredicateNode nested, string? subjectUserId)
    {
        if (subjectUserId is null) return false;
        if (nested.Expressions.Count != 1) return false;
        if (nested.Expressions[0] is not TagExpr inner) return false;
        if (inner.Nested is not null) return false; // recursion deeper than two hops not supported
        if (inner.Value is not CurrentUserValue) return false;

        var innerEdgeKind = inner.Tag.ToLowerInvariant();
        if (!_actorOutboundUserEdges.TryGetValue(innerEdgeKind, out var targets))
        {
            return false;
        }

        return targets.Contains(subjectUserId);
    }
}
