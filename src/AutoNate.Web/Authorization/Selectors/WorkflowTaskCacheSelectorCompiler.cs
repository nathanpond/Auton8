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

        // PATH IDS ARE HONOURED (#575).
        //
        // They were not, and the reason is worth recording: SelectorCompilerBase
        // does this filter for every kind that derives from it, but its
        // IdSelector is typed `Expression<Func<T, Guid>>` and this table keys on
        // a string. So these two compilers implement ISelectorCompiler<T>
        // directly, and inherited nothing -- including the path filter nobody
        // noticed was missing.
        //
        // Until now `/workflowtask/tid-1[...]` matched exactly one row in memory
        // and EVERY row in SQL. Not a narrower answer: an unrelated one.
        //
        // Ordinal by construction -- `Contains` lowers to `IN (...)`, and text
        // equality in Postgres is byte comparison -- which is what
        // InMemorySelectorEvaluator's `StringComparer.Ordinal` asks for.
        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            var idList = ids.ToList();
            predicate = ExpressionUtilities.AndAlso(
                predicate,
                t => idList.Contains(t.FlowableTaskId));
        }

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

            // `candidateuser` and `candidategroup` are gone (#581). Nothing
            // populates the columns they read -- the task projection writes empty
            // arrays unconditionally -- so they matched nothing in SQL and denied
            // everything in memory. Falling through to the refusal below is the
            // point: an unknown tag is a loud error, where these were predicates
            // that could not be satisfied. See the note in CoreEntityTypes.
            _ => throw new SelectorCompilationException(
                $"Unknown workflowtask tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<WorkflowTaskCache, bool>> CompileStringEquals(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowTaskCache, string?>> accessor)
    {
        // NESTED PREDICATES RESOLVE THROUGH THE EDGE GRAPH (#575).
        //
        // Checked FIRST, before the wildcard, because that is the order
        // InMemorySelectorEvaluator.EvalTag uses: a tag carrying a nested
        // predicate never reaches the leaf-value branch there, so it must not
        // reach it here either.
        //
        // `tag.Nested` used to be dropped on the floor, which meant
        // `[assignee=user[supervisor=user]]` compiled to plain
        // `assignee = <actor>` -- a different set, leaking one way and locking
        // out the other.
        if (tag.Nested is not null)
        {
            return CompileNestedUserPredicate(tag, context, accessor);
        }

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

    // Mirrors InMemorySelectorEvaluator's nested branch EXACTLY, including the
    // shapes it answers `false` to (#575).
    //
    // Those shapes return AlwaysFalse rather than throwing, and that is the
    // deliberate part. Throwing would raise SelectorCompilationException, which
    // Authorizer catches and turns into "skip this grant" -- and a skipped DENY
    // stops denying (#577). AlwaysFalse is also what the in-memory evaluator
    // answers for the same input, so the two paths agree, which is the whole
    // point of the milestone this lands in.
    //
    // NOTE the inner PinnedId: the in-memory evaluator ignores it and always
    // walks the ACTOR's outbound edges, while RecordSelectorCompiler honours
    // `PinnedId ?? actor`. Mirrored here on the in-memory side on purpose --
    // agreeing with the evaluator is this story's job, and the record pair's
    // disagreement is its own defect, filed separately rather than fixed here
    // by widening this story's blast radius.
    private static Expression<Func<WorkflowTaskCache, bool>> CompileNestedUserPredicate(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowTaskCache, string?>> accessor)
    {
        if (tag.Value is not CurrentUserValue
            || tag.Nested!.Expressions.Count != 1
            || tag.Nested.Expressions[0] is not TagExpr inner
            || inner.Nested is not null
            || inner.Value is not CurrentUserValue)
        {
            return ExpressionUtilities.AlwaysFalse<WorkflowTaskCache>();
        }

        var innerEdgeKind = inner.Tag.ToLowerInvariant();
        var actorId = context.ActorUserIdString;
        var db = context.Db;

        // "the actor has an outbound <innerEdgeKind> edge to the user this
        // row's tag names" -- the outer value identifies some user U, and the
        // nested predicate constrains U, exactly as the evaluator reads it.
        return ExpressionUtilities.Compose<WorkflowTaskCache, string?>(
            accessor,
            actual => actual != null && db.EntityEdges.Any(e =>
                e.EdgeKind == innerEdgeKind
                && e.FromKind == EntityKinds.User
                && e.FromId == actorId
                && e.ToKind == EntityKinds.User
                && e.ToId == actual));
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
