using System.Linq.Expressions;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Authorization.Selectors;

// Selector compiler for the workflow_execution_cache table. Tags mirror
// CoreEntityTypes.WorkflowExecution.tags (`processkey`, `definitionkey`,
// `startedby`) plus the new `status` and `tenant` predicates the cache
// makes possible.
//
// Path filters (e.g. `/workflowexecution/<id>`) aren't supported here —
// IDs are Flowable strings, not Guids, so SelectorCompilerBase's Guid
// IdSelector contract doesn't apply. Tag-only is the working subset for
// every grant we issue against this kind today.
public sealed class WorkflowExecutionCacheSelectorCompiler : ISelectorCompiler<WorkflowExecutionCache>
{
    public string Kind => EntityKinds.WorkflowExecution;

    public Expression<Func<WorkflowExecutionCache, bool>> Compile(SelectorAst ast, CompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(context);

        var predicate = ExpressionUtilities.AlwaysTrue<WorkflowExecutionCache>();
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

    private static Expression<Func<WorkflowExecutionCache, bool>> CompileExpr(PredicateExpr expr, CompilationContext context)
    {
        if (expr is not TagExpr tag)
        {
            throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}");
        }

        return tag.Tag.ToLowerInvariant() switch
        {
            "processkey"    => CompileStringEquals(tag, context, e => e.ProcessDefinitionKey),
            "definitionkey" => CompileStringEquals(tag, context, e => e.ProcessDefinitionId),
            "startedby"     => CompileStringEquals(tag, context, e => e.StartedBy),
            "status"        => CompileStringEquals(tag, context, e => e.Status),
            "tenant"        => CompileStringEquals(tag, context, e => e.TenantId),
            _ => throw new SelectorCompilationException(
                $"Unknown workflowexecution tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<WorkflowExecutionCache, bool>> CompileStringEquals(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowExecutionCache, string?>> columnAccessor)
    {
        var value = ResolveTagValue(tag, context);
        if (value is null)
        {
            // `tag=null` matches rows whose column is also null. Useful for
            // selectors like `tenant=null`.
            var parameter = columnAccessor.Parameters[0];
            var isNull = Expression.Equal(columnAccessor.Body, Expression.Constant(null, typeof(string)));
            return Expression.Lambda<Func<WorkflowExecutionCache, bool>>(isNull, parameter);
        }

        var p = columnAccessor.Parameters[0];
        var eq = Expression.Equal(columnAccessor.Body, Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<WorkflowExecutionCache, bool>>(eq, p);
    }

    private static string? ResolveTagValue(TagExpr tag, CompilationContext context) => tag.Value switch
    {
        LiteralValue lit => lit.Text,
        CurrentUserValue cu => cu.PinnedId ?? context.ActorUserIdString,
        WildcardValue => null,
        _ => throw new SelectorCompilationException(
            $"Tag '{tag.Tag}' value type {tag.Value.GetType().Name} is not supported.")
    };
}
