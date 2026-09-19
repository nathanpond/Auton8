using System.Linq.Expressions;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Authorization.Selectors;

// Selector compiler for the workflow_execution_cache table. Tags mirror
// CoreEntityTypes.WorkflowExecution.tags: `processkey`, `definitionkey`,
// `startedby` and `status`. (`tenant` was advertised here until #576 removed
// it — see the note in CoreEntityTypes.)
//
// Path filters (`/workflowexecution/<id>`) ARE supported, as of #575. They
// were not, and the note that used to stand here explained why in a way that
// made it sound settled: "IDs are Flowable strings, not Guids, so
// SelectorCompilerBase's Guid IdSelector contract doesn't apply." True, and it
// explains why the feature was never inherited — but `InMemorySelectorEvaluator`
// honours `ast.Path` regardless, so a path-id selector matched one row in
// memory and EVERY row here. A divergence, not a subset. The filter is now
// written out directly against the string key.
//
// Nested multi-hop predicates (`[startedby=user[supervisor=user]]`) are
// honoured too, as of the same story.
public sealed class WorkflowExecutionCacheSelectorCompiler : ISelectorCompiler<WorkflowExecutionCache>
{
    public string Kind => EntityKinds.WorkflowExecution;

    public Expression<Func<WorkflowExecutionCache, bool>> Compile(SelectorAst ast, CompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(context);

        var predicate = ExpressionUtilities.AlwaysTrue<WorkflowExecutionCache>();

        // PATH IDS ARE HONOURED (#575).
        //
        // `/workflowexecution/pi-1[...]` matched exactly one row in memory and
        // EVERY row here, because nothing read ast.Path. SelectorCompilerBase
        // does this for the kinds that derive from it; its IdSelector is typed
        // to Guid and this table keys on a string, so this compiler implements
        // ISelectorCompiler<T> directly and inherited none of it.
        //
        // Ordinal by construction: `Contains` lowers to `IN (...)` and text
        // equality in Postgres is byte comparison, which is what the
        // evaluator's `StringComparer.Ordinal` asks for.
        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            var idList = ids.ToList();
            predicate = ExpressionUtilities.AndAlso(
                predicate,
                e => idList.Contains(e.FlowableInstanceId));
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

            // `tenant` is gone (#576). It compiled here against a column
            // FlowableExecutionProjection always writes as null, so it matched
            // nothing while the in-memory path -- which never supplied the fact
            // -- denied everything. Falling through to the refusal below is the
            // point: an unknown tag is a loud error, where the old case was a
            // predicate that silently could not be satisfied.
            _ => throw new SelectorCompilationException(
                $"Unknown workflowexecution tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<WorkflowExecutionCache, bool>> CompileStringEquals(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowExecutionCache, string?>> columnAccessor)
    {
        // NESTED PREDICATES RESOLVE THROUGH THE EDGE GRAPH (#575).
        //
        // Checked before the wildcard, mirroring
        // InMemorySelectorEvaluator.EvalTag's order -- a tag carrying a nested
        // predicate never reaches the leaf-value branch there.
        //
        // `[startedby=user[supervisor=user]]` used to compile to plain
        // `started_by = <actor>`, dropping the nesting silently: a different
        // set, not a narrower one.
        if (tag.Nested is not null)
        {
            return CompileNestedUserPredicate(tag, context, columnAccessor);
        }

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

    // Mirrors InMemorySelectorEvaluator's nested branch exactly, including the
    // shapes it answers `false` to (#575). See the twin in
    // WorkflowTaskCacheSelectorCompiler for why those return AlwaysFalse rather
    // than throwing, and for the note on the inner PinnedId.
    private static Expression<Func<WorkflowExecutionCache, bool>> CompileNestedUserPredicate(
        TagExpr tag,
        CompilationContext context,
        Expression<Func<WorkflowExecutionCache, string?>> columnAccessor)
    {
        if (tag.Value is not CurrentUserValue
            || tag.Nested!.Expressions.Count != 1
            || tag.Nested.Expressions[0] is not TagExpr inner
            || inner.Nested is not null
            || inner.Value is not CurrentUserValue)
        {
            return ExpressionUtilities.AlwaysFalse<WorkflowExecutionCache>();
        }

        var innerEdgeKind = inner.Tag.ToLowerInvariant();
        var actorId = context.ActorUserIdString;
        var db = context.Db;

        return ExpressionUtilities.Compose<WorkflowExecutionCache, string?>(
            columnAccessor,
            actual => actual != null && db.EntityEdges.Any(e =>
                e.EdgeKind == innerEdgeKind
                && e.FromKind == EntityKinds.User
                && e.FromId == actorId
                && e.ToKind == EntityKinds.User
                && e.ToId == actual));
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
