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

    public static Expression<Func<T, bool>> AlwaysTrue<T>() => _ => true;

    public static Expression<Func<T, bool>> AlwaysFalse<T>() => _ => false;

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
