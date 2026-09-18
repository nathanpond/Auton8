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
// IdSelector contract doesn't apply.
//
// THAT IS A DIVERGENCE, NOT A SUBSET (#575). `InMemorySelectorEvaluator`
// DOES honour `ast.Path`, so a path-id selector matches one row in memory
// and EVERY row here. "Tag-only is the working subset for every grant we
// issue today" was the old wording, and it reads as a scope decision when
// it is an unclosed gap. #575 owns it.
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
        var p = columnAccessor.Parameters[0];

        // THE WILDCARD HAS ITS OWN FORM, BRANCHED BEFORE THE VALUE IS RESOLVED (#574).
        //
        // The branch that used to stand here read `tag=null matches rows whose
        // column is also null. Useful for selectors like tenant=null` -- but the
        // grammar has NO null literal (`SelectorParser.ParseValue` produces only
        // wildcard, literal, current-user and qualified values, and
        // `ActorUserIdString` is never null), so the only thing that ever
        // reached it was the WILDCARD. `tenant=null` parses as the literal
        // string "null" and compiles to `= 'null'`.
        //
        // So the comment described a feature that does not exist while the code
        // silently inverted one that does: `tag=*` compiled to `IS NULL` where
        // `InMemorySelectorEvaluator` reads `actual is not null`
        // (GHSA-vrw7-qxhw-m9q8).
        if (tag.Value is WildcardValue)
        {
            var hasAnyValue = Expression.NotEqual(
                columnAccessor.Body, Expression.Constant(null, typeof(string)));
            return Expression.Lambda<Func<WorkflowExecutionCache, bool>>(hasAnyValue, p);
        }

        var value = ResolveTagValue(tag, context);
        var eq = Expression.Equal(columnAccessor.Body, Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<WorkflowExecutionCache, bool>>(eq, p);
    }

    // Returns the VALUE a tag was given. The wildcard is not a value and is
    // handled by its caller before it gets here (#574) -- mapping it to null is
    // what made `tag=*` compile to `IS NULL`.
    private static string ResolveTagValue(TagExpr tag, CompilationContext context) => tag.Value switch
    {
        LiteralValue lit => lit.Text,
        CurrentUserValue cu => cu.PinnedId ?? context.ActorUserIdString,
        WildcardValue => throw new SelectorCompilationException(
            $"Tag '{tag.Tag}': the wildcard has no value and must be compiled before this point."),
        _ => throw new SelectorCompilationException(
            $"Tag '{tag.Tag}' value type {tag.Value.GetType().Name} is not supported.")
    };
}
