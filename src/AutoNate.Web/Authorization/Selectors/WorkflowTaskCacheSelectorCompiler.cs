using System.Linq.Expressions;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Authorization.Selectors;

// Selector compiler for the workflow_task_cache table. Supports
// CoreEntityTypes.WorkflowTask.tags (`processkey`, `definitionkey`,
// `assignee`) plus the array-membership predicates the cache enables:
// `candidategroup=<name>` and `candidateuser=<user>` translate to
// Postgres ANY() over the text[] columns.
public sealed class WorkflowTaskCacheSelectorCompiler : ISelectorCompiler<WorkflowTaskCache>
{
    public string Kind => EntityKinds.WorkflowTask;

    public Expression<Func<WorkflowTaskCache, bool>> Compile(SelectorAst ast, CompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(context);

        var predicate = ExpressionUtilities.AlwaysTrue<WorkflowTaskCache>();
        if (ast.Predicate is { } pred)
        {
            foreach (var expr in pred.Expressions)
            {
                predicate = ExpressionUtilities.AndAlso(predicate, CompileExpr(expr, context));
            }
        }

        return predicate;
    }

    private static Expression<Func<WorkflowTaskCache, bool>> CompileExpr(PredicateExpr expr, CompilationContext context)
    {
        if (expr is not TagExpr tag)
        {
            throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}");
        }

        return tag.Tag.ToLowerInvariant() switch
        {
            "processkey"     => CompileStringEquals(tag, context, t => t.ProcessDefinitionKey),
            "definitionkey"  => CompileStringEquals(tag, context, t => t.TaskDefinitionKey),
            "assignee"       => CompileStringEquals(tag, context, t => t.Assignee),
            "candidateuser"  => CompileArrayContains(tag, context, t => t.CandidateUsers),
            "candidategroup" => CompileArrayContains(tag, context, t => t.CandidateGroups),
            _ => throw new SelectorCompilationException(
                $"Unknown workflowtask tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<WorkflowTaskCache, bool>> CompileStringEquals(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowTaskCache, string?>> accessor)
    {
        var value = ResolveTagValue(tag, context);
        var p = accessor.Parameters[0];
        Expression body = value is null
            ? Expression.Equal(accessor.Body, Expression.Constant(null, typeof(string)))
            : Expression.Equal(accessor.Body, Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<WorkflowTaskCache, bool>>(body, p);
    }

    private static Expression<Func<WorkflowTaskCache, bool>> CompileArrayContains(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowTaskCache, string[]>> accessor)
    {
        var value = ResolveTagValue(tag, context)
            ?? throw new SelectorCompilationException(
                $"Tag '{tag.Tag}' requires a non-null value.");

        // Translates to "WHERE :value = ANY(candidate_users)" — the Npgsql
        // provider lowers Enumerable.Contains on an array-typed property to
        // ANY(), which uses the column's GIN index.
        var p = accessor.Parameters[0];
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { typeof(string) },
            accessor.Body,
            Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<WorkflowTaskCache, bool>>(containsCall, p);
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
