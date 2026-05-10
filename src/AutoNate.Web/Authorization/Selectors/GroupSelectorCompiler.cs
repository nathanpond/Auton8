using System.Linq.Expressions;
using GroupEntity = AutoNate.Web.Persistence.Scaffolded.Group;

namespace AutoNate.Web.Authorization.Selectors;

// Compiles selectors for the `group` kind. `name` is a literal column match;
// `member=user` resolves to "the actor is a member of this group" via the
// group_members join table. Group membership lives in its own table rather
// than entity_edges, so this compiler queries it directly.
public sealed class GroupSelectorCompiler : SelectorCompilerBase<GroupEntity>
{
    public override string Kind => EntityKinds.Group;

    protected override Expression<Func<GroupEntity, Guid>> IdSelector => g => g.Id;

    protected override Expression<Func<GroupEntity, bool>> CompileExpr(
        PredicateExpr expr, CompilationContext context)
    {
        if (expr is not TagExpr tag)
        {
            throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}");
        }

        return tag.Tag.ToLowerInvariant() switch
        {
            "name" => CompileName(tag),
            "member" => CompileMember(tag, context),
            _ => throw new SelectorCompilationException(
                $"Unknown group tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<GroupEntity, bool>> CompileName(TagExpr tag)
    {
        var name = RequireLiteral(tag);
        return g => g.Name == name;
    }

    // `member=user` matches groups the actor belongs to. Other shapes
    // (`member=*`, `member=<literal>`) aren't useful for grant selectors and
    // would need a different evaluation path, so reject them here.
    private static Expression<Func<GroupEntity, bool>> CompileMember(
        TagExpr tag, CompilationContext context)
    {
        if (tag.Value is not CurrentUserValue userValue)
        {
            throw new SelectorCompilationException(
                "Tag 'member' currently supports only `=user` (the current actor).");
        }

        var actorId = userValue.PinnedId is { } pinned && Guid.TryParse(pinned, out var pinnedGuid)
            ? pinnedGuid
            : context.ActorUserId;

        var db = context.Db;
        return g => db.GroupMembers.Any(m => m.GroupId == g.Id && m.UserId == actorId);
    }
}
