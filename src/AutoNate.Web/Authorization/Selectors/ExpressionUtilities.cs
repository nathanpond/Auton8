using System.Linq.Expressions;

namespace AutoNate.Web.Authorization.Selectors;

internal static class ExpressionUtilities
{
    // Combines two predicates using && while rebinding the parameter so the
    // resulting expression is a single, EF-translatable lambda over one input.
    public static Expression<Func<T, bool>> AndAlso<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var leftBody = new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body)!;
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(leftBody, rightBody), parameter);
    }

    public static Expression<Func<T, bool>> OrElse<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var leftBody = new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body)!;
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(leftBody, rightBody), parameter);
    }

    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> source)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var body = new ParameterReplacer(source.Parameters[0], parameter).Visit(source.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.Not(body), parameter);
    }

    // Inlines `accessor` into `predicate`, producing a single lambda over the
    // source. Used by the nested-predicate path (#575), which needs to talk
    // about the VALUE of a tag column inside a subquery.
    //
    // Inlining rather than `Expression.Invoke`: an invocation node survives
    // into the query tree, and EF Core translates it only in the cases it has
    // been taught. A replaced parameter leaves a tree indistinguishable from
    // one written by hand, which needs no such luck.
    public static Expression<Func<TSource, bool>> Compose<TSource, TValue>(
        Expression<Func<TSource, TValue>> accessor,
        Expression<Func<TValue, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(predicate);

        var parameter = accessor.Parameters[0];
        var body = new NodeReplacer(predicate.Parameters[0], accessor.Body).Visit(predicate.Body)!;
        return Expression.Lambda<Func<TSource, bool>>(body, parameter);
    }

    public static Expression<Func<T, bool>> AlwaysTrue<T>() => _ => true;

    public static Expression<Func<T, bool>> AlwaysFalse<T>() => _ => false;

    // Replaces one specific node (by reference) anywhere in a tree. Broader
    // than ParameterReplacer, which can only swap a parameter for another
    // parameter; Compose needs to swap a parameter for an arbitrary member
    // access.
    private sealed class NodeReplacer : ExpressionVisitor
    {
        private readonly Expression _from;
        private readonly Expression _to;

        public NodeReplacer(Expression from, Expression to)
        {
            _from = from;
            _to = to;
        }

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(node))]
        public override Expression? Visit(Expression? node) =>
            node == _from ? _to : base.Visit(node);
    }

    /// <summary>
    /// `lower(column) = &lt;value, lowered here&gt;` — the ONE place a selector tag
    /// value is compared to a column (#631).
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists.</b> <c>InMemorySelectorEvaluator</c> compares tag
    /// values with <c>OrdinalIgnoreCase</c> — pinned by a deliberate test — while
    /// every compiler emitted <c>=</c>, which Postgres evaluates case-sensitively
    /// (measured: <c>'alice' = 'ALICE'</c> is <c>false</c>). So a grant reading
    /// <c>[assignee=Alice]</c> matched on a single-instance check and matched
    /// nothing in a list. Nine sites each had their own <c>==</c>; they now share
    /// this one, because a rule with nine copies is how the two paths drifted
    /// apart to begin with.</para>
    ///
    /// <para>It lives here rather than on <c>SelectorCompilerBase&lt;T&gt;</c>
    /// because the two workflow-cache compilers deliberately implement
    /// <c>ISelectorCompiler&lt;T&gt;</c> directly and do not derive from it — so a
    /// helper on the base class would have been reachable by seven of the nine
    /// sites, which is exactly the shape that lets a rule drift.</para>
    ///
    /// <para><b>Not <c>EF.Functions.ILike</c>.</b> <c>ILIKE</c> reads <c>%</c> and
    /// <c>_</c> as wildcards, so a tag value containing either would silently
    /// match rows it does not name — a widening hidden inside the widening this
    /// change already is.</para>
    ///
    /// <para><b>ASCII, not Unicode.</b> .NET's <c>OrdinalIgnoreCase</c> and
    /// Postgres's <c>lower()</c> are not the same function. They agree across
    /// ASCII, which is what process keys, usernames, statuses and short codes are;
    /// they do not agree on, for instance, <c>U+0130</c>. This NARROWS the
    /// divergence to that residue rather than closing it.</para>
    ///
    /// <para>A NULL column stays false, exactly as <c>=</c> did:
    /// <c>lower(NULL) = 'x'</c> is NULL, which is not true.</para>
    /// </remarks>
    public static Expression CaseInsensitiveEqualsBody(Expression column, string value)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(value);

        return Expression.Equal(
            Expression.Call(column, LowerMethod),
            Expression.Constant(value.ToLowerInvariant(), typeof(string)));
    }

    private static readonly System.Reflection.MethodInfo LowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)
        ?? throw new InvalidOperationException("string.ToLower() not found.");

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}
