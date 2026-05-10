using System.Text;
using AutoNate.Web.Authorization.Edges;

namespace AutoNate.Web.Authorization.Selectors;

// SQL counterpart to RecordSelectorCompiler. Emits a boolean SQL expression
// (in `{N}` placeholder form for ExecuteSqlRawAsync) that references the
// outer `records` table by name. The shared SqlBuildContext accumulates
// parameters and aliases across multiple Compile calls so a list of grants
// can be combined into one OR/AND-NOT expression.
public sealed class RecordSelectorSqlCompiler
{
    public string Compile(SelectorAst ast, RecordSqlBuildContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(ctx);

        var parts = new List<string>();

        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            var guids = ParseGuids(ids);
            if (guids.Count == 0)
            {
                return "(FALSE)";
            }

            var placeholders = new List<string>(guids.Count);
            foreach (var g in guids)
            {
                placeholders.Add(ctx.AddParameter(g) + "::uuid");
            }

            parts.Add($"(records.id IN ({string.Join(", ", placeholders)}))");
        }

        if (ast.Predicate is { } pred)
        {
            foreach (var expr in pred.Expressions)
            {
                parts.Add(CompileExpr(expr, ctx));
            }
        }

        return parts.Count == 0
            ? "(TRUE)"
            : "(" + string.Join(" AND ", parts) + ")";
    }

    private static string CompileExpr(PredicateExpr expr, RecordSqlBuildContext ctx) =>
        expr switch
        {
            TagExpr tag => CompileTag(tag, ctx),
            ScopeExpr scope => throw new SelectorCompilationException(
                $"Scope expression '{scope.Tag}:{scope.Qualifier}' is not yet supported on records."),
            _ => throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}")
        };

    private static string CompileTag(TagExpr tag, RecordSqlBuildContext ctx) =>
        tag.Tag.ToLowerInvariant() switch
        {
            "assignee" => CompileEdgeTag(tag, EdgeKinds.Assignee, ctx),
            "creator" => CompileEdgeTag(tag, EdgeKinds.Creator, ctx),
            "recordtype" => CompileRecordTypeTag(tag, ctx),
            "status" => CompileStatusTag(tag, ctx),
            _ => throw new SelectorCompilationException($"Unknown record tag '{tag.Tag}'.")
        };

    private static string CompileStatusTag(TagExpr tag, RecordSqlBuildContext ctx)
    {
        if (tag.Value is not LiteralValue literal)
        {
            throw new SelectorCompilationException(
                "Tag 'status' requires a literal value, e.g. status=open.");
        }

        var p = ctx.AddParameter(literal.Text);
        return $"(records.status = {p})";
    }

    private static string CompileEdgeTag(TagExpr tag, string edgeKind, RecordSqlBuildContext ctx)
    {
        if (tag.Value is not CurrentUserValue user)
        {
            throw new SelectorCompilationException(
                $"Tag '{tag.Tag}' currently supports only `=user`.");
        }

        var aliasOuter = ctx.NextAlias();

        if (tag.Nested is null)
        {
            var subjectId = user.PinnedId ?? ctx.ActorUserId.ToString();
            var pSubject = ctx.AddParameter(subjectId);
            var sb = new StringBuilder();
            sb.Append("EXISTS (SELECT 1 FROM entity_edges ").Append(aliasOuter)
              .Append(" WHERE ").Append(aliasOuter).Append(".edge_kind = '").Append(edgeKind).Append('\'')
              .Append(" AND ").Append(aliasOuter).Append(".from_kind = 'user'")
              .Append(" AND ").Append(aliasOuter).Append(".from_id = ").Append(pSubject)
              .Append(" AND ").Append(aliasOuter).Append(".to_kind = 'record'")
              .Append(" AND ").Append(aliasOuter).Append(".to_id = records.id::text)");
            return sb.ToString();
        }

        var (innerKind, innerSubject) = ParseNestedUserPredicate(tag.Nested, ctx);
        var pInner = ctx.AddParameter(innerSubject);
        var aliasInner = ctx.NextAlias();

        var multi = new StringBuilder();
        multi.Append("EXISTS (SELECT 1 FROM entity_edges ").Append(aliasOuter)
             .Append(" WHERE ").Append(aliasOuter).Append(".edge_kind = '").Append(edgeKind).Append('\'')
             .Append(" AND ").Append(aliasOuter).Append(".from_kind = 'user'")
             .Append(" AND ").Append(aliasOuter).Append(".to_kind = 'record'")
             .Append(" AND ").Append(aliasOuter).Append(".to_id = records.id::text")
             .Append(" AND EXISTS (SELECT 1 FROM entity_edges ").Append(aliasInner)
             .Append(" WHERE ").Append(aliasInner).Append(".edge_kind = '").Append(innerKind).Append('\'')
             .Append(" AND ").Append(aliasInner).Append(".from_kind = 'user'")
             .Append(" AND ").Append(aliasInner).Append(".from_id = ").Append(pInner)
             .Append(" AND ").Append(aliasInner).Append(".to_kind = 'user'")
             .Append(" AND ").Append(aliasInner).Append(".to_id = ").Append(aliasOuter).Append(".from_id))");
        return multi.ToString();
    }

    private static string CompileRecordTypeTag(TagExpr tag, RecordSqlBuildContext ctx)
    {
        if (tag.Value is not LiteralValue literal)
        {
            throw new SelectorCompilationException(
                "Tag 'recordtype' requires a literal value, e.g. recordtype=lead.");
        }

        var raw = literal.Text;
        Guid? matchedId = Guid.TryParse(raw, out var asGuid)
            ? asGuid
            : ctx.RecordTypeIdsByShortCode.TryGetValue(raw, out var fromShort)
                ? fromShort
                : null;

        if (matchedId is null)
        {
            return "(FALSE)";
        }

        var p = ctx.AddParameter(matchedId.Value);
        return $"(records.record_type_id = {p}::uuid)";
    }

    private static (string EdgeKind, string SubjectId) ParseNestedUserPredicate(
        PredicateNode nested, RecordSqlBuildContext ctx)
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
            innerUser.PinnedId ?? ctx.ActorUserId.ToString());
    }

    private static List<Guid> ParseGuids(IReadOnlyCollection<string> raw)
    {
        var list = new List<Guid>(raw.Count);
        foreach (var s in raw)
        {
            if (Guid.TryParse(s, out var g)) list.Add(g);
        }
        return list;
    }
}

public sealed class RecordSqlBuildContext
{
    private int _aliasCounter;

    public RecordSqlBuildContext(
        Guid actorUserId,
        int parameterOffset,
        IReadOnlyDictionary<string, Guid> recordTypeIdsByShortCode)
    {
        ActorUserId = actorUserId;
        NextIndex = parameterOffset;
        RecordTypeIdsByShortCode = recordTypeIdsByShortCode;
    }

    public Guid ActorUserId { get; }

    public List<object?> Parameters { get; } = new();

    public IReadOnlyDictionary<string, Guid> RecordTypeIdsByShortCode { get; }

    public int NextIndex { get; private set; }

    public string AddParameter(object? value)
    {
        Parameters.Add(value);
        return "{" + NextIndex++ + "}";
    }

    public string NextAlias() => "e" + _aliasCounter++;
}
