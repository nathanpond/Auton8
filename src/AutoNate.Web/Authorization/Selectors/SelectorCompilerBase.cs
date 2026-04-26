using System.Linq.Expressions;

namespace AutoNate.Web.Authorization.Selectors;

public abstract class SelectorCompilerBase<T> : ISelectorCompiler<T> where T : class
{
    public abstract string Kind { get; }

    // Lambda that extracts the entity's primary identifier so the path-level
    // `[id]` filter can be expressed without each subclass rebuilding the
    // Contains expression.
    protected abstract Expression<Func<T, Guid>> IdSelector { get; }

    public Expression<Func<T, bool>> Compile(SelectorAst ast, CompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(context);

        var predicate = ExpressionUtilities.AlwaysTrue<T>();

        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            var guids = ParseGuids(ids);
            if (guids.Count == 0)
            {
                return ExpressionUtilities.AlwaysFalse<T>();
            }

            predicate = ExpressionUtilities.AndAlso(predicate, BuildIdFilter(guids));
        }

        if (ast.Predicate is { } pred)
        {
            foreach (var expr in pred.Expressions)
            {
                var step = CompileExpr(expr, context);
                predicate = ExpressionUtilities.AndAlso(predicate, step);
            }
        }

        return predicate;
    }

    protected virtual Expression<Func<T, bool>> CompileExpr(PredicateExpr expr, CompilationContext context)
    {
        throw new SelectorCompilationException(
            $"Selector predicates are not supported for kind '{Kind}'.");
    }

    private Expression<Func<T, bool>> BuildIdFilter(IReadOnlyList<Guid> ids)
    {
        // x => ids.Contains(IdSelector(x))
        var parameter = Expression.Parameter(typeof(T), "x");
        var idAccess = Expression.Invoke(IdSelector, parameter);
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { typeof(Guid) },
            Expression.Constant(ids.ToList()),
            idAccess);
        return Expression.Lambda<Func<T, bool>>(containsCall, parameter);
    }

    private static List<Guid> ParseGuids(IEnumerable<string> raw)
    {
        var list = new List<Guid>();
        foreach (var s in raw)
        {
            if (Guid.TryParse(s, out var g))
            {
                list.Add(g);
            }
        }

        return list;
    }
}
