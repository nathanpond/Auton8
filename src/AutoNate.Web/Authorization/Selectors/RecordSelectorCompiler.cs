using System.Linq.Expressions;
using AutoNate.Web.Authorization.Edges;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;

namespace AutoNate.Web.Authorization.Selectors;

// Compiles selectors targeting the `record` kind into LINQ predicates over
// the scaffolded Record entity. EF Core translates the resulting expression
// to SQL — including subqueries against entity_edges — when the predicate is
// fed into IQueryable<Record>.Where.
public sealed class RecordSelectorCompiler : SelectorCompilerBase<RecordEntity>
{
    public override string Kind => EntityKinds.Record;

    protected override Expression<Func<RecordEntity, Guid>> IdSelector => r => r.Id;

    protected override Expression<Func<RecordEntity, bool>> CompileExpr(
        PredicateExpr expr,
        CompilationContext context)
    {
        switch (expr)
        {
            case TagExpr tag:
                return CompileTag(tag, context);
            case ScopeExpr scope:
                throw new SelectorCompilationException(
                    $"Scope expression '{scope.Tag}:{scope.Qualifier}' is not yet supported on records.");
            default:
                throw new SelectorCompilationException(
                    $"Unsupported predicate expression: {expr.GetType().Name}");
        }
    }

    private static Expression<Func<RecordEntity, bool>> CompileTag(TagExpr tag, CompilationContext context)
    {
        return tag.Tag.ToLowerInvariant() switch
        {
            "assignee" => CompileEdgeTag(tag, EdgeKinds.Assignee, context),
            "creator"  => CompileEdgeTag(tag, EdgeKinds.Creator, context),
            "recordtype" => CompileRecordTypeTag(tag, context),
            _ => throw new SelectorCompilationException(
                $"Unknown record tag '{tag.Tag}'.")
        };
    }

    // Compiles tag expressions of the form `<edgeKind>=user[…]?` against
    // entity_edges. Without a nested predicate this is a single hop:
    //
    //     EXISTS (e: edgeKind, user/<subject> → record/r.id)
    //
    // With a nested predicate of the shape `[<innerEdgeKind>=user]` the
    // outer edge's from-user is left unbound, then constrained by an inner
    // EXISTS that requires the actor → that user via the inner edge kind:
    //
    //     EXISTS (eOuter: edgeKind, user/U → record/r.id) AND
    //         EXISTS (eInner: innerEdgeKind, user/<actor> → user/U)
    //
    // That's how `assignee=user[supervisor=user]` resolves to "records whose
    // assignee is a user the actor supervises."
    private static Expression<Func<RecordEntity, bool>> CompileEdgeTag(
        TagExpr tag,
        string edgeKind,
        CompilationContext context)
    {
        if (tag.Value is not CurrentUserValue userValue)
        {
            throw new SelectorCompilationException(
                $"Tag '{tag.Tag}' currently supports only `=user` (the current actor).");
        }

        var db = context.Db;

        if (tag.Nested is null)
        {
            var subjectId = userValue.PinnedId ?? context.ActorUserIdString;
            return r => db.EntityEdges.Any(e =>
                e.EdgeKind == edgeKind
                && e.FromKind == EntityKinds.User
                && e.FromId == subjectId
                && e.ToKind == EntityKinds.Record
                && e.ToId == r.Id.ToString());
        }

        var (innerEdgeKind, innerSubjectId) = ParseNestedUserPredicate(tag.Nested, context);

        return r => db.EntityEdges.Any(eOuter =>
            eOuter.EdgeKind == edgeKind
            && eOuter.FromKind == EntityKinds.User
            && eOuter.ToKind == EntityKinds.Record
            && eOuter.ToId == r.Id.ToString()
            && db.EntityEdges.Any(eInner =>
                eInner.EdgeKind == innerEdgeKind
                && eInner.FromKind == EntityKinds.User
                && eInner.FromId == innerSubjectId
                && eInner.ToKind == EntityKinds.User
                && eInner.ToId == eOuter.FromId));
    }

    // Reads a one-expression nested predicate like `[supervisor=user]` and
    // returns (edge kind, subject user id). Recursion deeper than two hops
    // and shapes other than `<tag>=user` are rejected explicitly so authors
    // get a clear error rather than a silently-wrong predicate.
    private static (string EdgeKind, string SubjectId) ParseNestedUserPredicate(
        PredicateNode nested,
        CompilationContext context)
    {
        if (nested.Expressions.Count != 1)
        {
            throw new SelectorCompilationException(
                "Nested predicates currently support a single inner expression.");
        }

        if (nested.Expressions[0] is not TagExpr inner)
        {
            throw new SelectorCompilationException(
                "Nested predicates must be tag expressions, e.g. supervisor=user.");
        }

        if (inner.Nested is not null)
        {
            throw new SelectorCompilationException(
                "Predicates nested more than two hops deep are not yet supported.");
        }

        if (inner.Value is not CurrentUserValue innerUser)
        {
            throw new SelectorCompilationException(
                "Nested tag values must reference the current user (=user).");
        }

        return (
            inner.Tag.ToLowerInvariant(),
            innerUser.PinnedId ?? context.ActorUserIdString);
    }

    // `recordtype=<value>` matches by short_code (preferred) or by the literal
    // GUID. Resolution happens at compile time so the compiled SQL is a simple
    // FK comparison rather than an extra subquery on every row.
    private static Expression<Func<RecordEntity, bool>> CompileRecordTypeTag(
        TagExpr tag,
        CompilationContext context)
    {
        if (tag.Value is not LiteralValue literal)
        {
            throw new SelectorCompilationException(
                "Tag 'recordtype' requires a literal value, e.g. recordtype=lead.");
        }

        var raw = literal.Text;
        Guid? matchedId = null;

        if (Guid.TryParse(raw, out var asGuid))
        {
            matchedId = asGuid;
        }
        else
        {
            var fromShortCode = context.Db.RecordTypes
                .Where(t => t.ShortCode == raw)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefault();
            if (fromShortCode is { } found)
            {
                matchedId = found;
            }
        }

        if (matchedId is null)
        {
            return ExpressionUtilities.AlwaysFalse<RecordEntity>();
        }

        var id = matchedId.Value;
        return r => r.RecordTypeId == id;
    }
}
