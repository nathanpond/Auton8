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
        var p = accessor.Parameters[0];

        // THE WILDCARD HAS ITS OWN FORM, BRANCHED BEFORE THE VALUE IS RESOLVED (#574).
        //
        // It used to fall through `ResolveTagValue`, which mapped it to null,
        // into the branch meant for a null value -- so `tag=*` compiled to
        // `IS NULL` while `InMemorySelectorEvaluator` read the same selector as
        // `actual is not null`. Exact complements: the same stored grant meant
        // opposite things depending on which path evaluated it, measured at 69
        // leaks and 539 lockouts (GHSA-vrw7-qxhw-m9q8).
        //
        // Branching here rather than inside ResolveTagValue is deliberate: the
        // resolver's job is to produce a value, and the wildcard does not have
        // one. Giving it a value of `null` is what caused the defect.
        if (tag.Value is WildcardValue)
        {
            var hasAnyValue = Expression.NotEqual(
                accessor.Body, Expression.Constant(null, typeof(string)));
            return Expression.Lambda<Func<WorkflowTaskCache, bool>>(hasAnyValue, p);
        }

        var value = ResolveTagValue(tag, context);
        var body = Expression.Equal(accessor.Body, Expression.Constant(value, typeof(string)));
        return Expression.Lambda<Func<WorkflowTaskCache, bool>>(body, p);
    }

    private static Expression<Func<WorkflowTaskCache, bool>> CompileArrayContains(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowTaskCache, string[]>> accessor)
    {
        var p0 = accessor.Parameters[0];

        // AN ARRAY TAG'S WILDCARD MEANS "NON-EMPTY" (#574).
        //
        // Settled here rather than left to fall through, because an array tag
        // reaching the same resolver is exactly how this defect would survive
        // its own fix. Before this it threw `requires a non-null value`, so
        // `candidateuser=*` failed to COMPILE -- and an uncompilable grant is
        // skipped with a warning, which for a deny fails open (#577).
        //
        // Non-empty is the only thing decidable: both array columns are
        // `NOT NULL DEFAULT ARRAY[]::TEXT[]`, so a null array cannot occur.
        if (tag.Value is WildcardValue)
        {
            var length = Expression.ArrayLength(accessor.Body);
            var nonEmpty = Expression.GreaterThan(length, Expression.Constant(0));
            return Expression.Lambda<Func<WorkflowTaskCache, bool>>(nonEmpty, p0);
        }

        var value = ResolveTagValue(tag, context);

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

    // Returns the VALUE a tag was given. The wildcard is not a value and is
    // handled by its callers before they get here (#574) -- mapping it to null
    // is what made `tag=*` compile to `IS NULL`.
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
