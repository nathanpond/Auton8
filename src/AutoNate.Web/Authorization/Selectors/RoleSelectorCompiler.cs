using System.Linq.Expressions;
using RoleEntity = AutoNate.Web.Persistence.Scaffolded.Role;

namespace AutoNate.Web.Authorization.Selectors;

// `role` only declares a `name` tag in CoreEntityTypes, so this compiler is
// thin. Exists primarily to replace the path-only compiler so a grant like
// `/role/*[name=Editors]` actually filters on role name rather than being
// silently skipped.
public sealed class RoleSelectorCompiler : SelectorCompilerBase<RoleEntity>
{
    public override string Kind => EntityKinds.Role;

    protected override Expression<Func<RoleEntity, Guid>> IdSelector => r => r.Id;

    protected override Expression<Func<RoleEntity, bool>> CompileExpr(
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
            _ => throw new SelectorCompilationException(
                $"Unknown role tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<RoleEntity, bool>> CompileName(TagExpr tag)
    {
        var name = RequireLiteral(tag);
        return r => r.Name == name;
    }
}
