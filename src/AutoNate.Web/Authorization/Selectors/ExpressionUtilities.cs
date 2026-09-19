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
