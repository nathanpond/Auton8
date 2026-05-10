using System.Linq.Expressions;
using FormEntity = AutoNate.Web.Persistence.Scaffolded.Form;

namespace AutoNate.Web.Authorization.Selectors;

// Compiles selectors for `form`. CoreEntityTypes advertises `shortcode`,
// `siteAvailable`, `draft`, `published` — all map to columns on the entity.
// `published` is derived: a form row is "published" iff PublishedVersionNumber
// is non-null, mirroring how the SPA labels the same rows.
public sealed class FormSelectorCompiler : SelectorCompilerBase<FormEntity>
{
    public override string Kind => EntityKinds.Form;

    protected override Expression<Func<FormEntity, Guid>> IdSelector => f => f.Id;

    protected override Expression<Func<FormEntity, bool>> CompileExpr(
        PredicateExpr expr, CompilationContext context)
    {
        if (expr is not TagExpr tag)
        {
            throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}");
        }

        return tag.Tag.ToLowerInvariant() switch
        {
            "shortcode" => CompileShortCode(tag),
            "siteavailable" => CompileSiteAvailable(tag),
            "draft" => CompileDraft(tag),
            "published" => CompilePublished(tag),
            _ => throw new SelectorCompilationException(
                $"Unknown form tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<FormEntity, bool>> CompileShortCode(TagExpr tag)
    {
        var code = RequireLiteral(tag);
        return f => f.ShortCode == code;
    }

    private static Expression<Func<FormEntity, bool>> CompileSiteAvailable(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return f => f.SiteAvailable == expected;
    }

    private static Expression<Func<FormEntity, bool>> CompileDraft(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return f => f.IsDraft == expected;
    }

    private static Expression<Func<FormEntity, bool>> CompilePublished(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return expected
            ? f => f.PublishedVersionNumber != null
            : f => f.PublishedVersionNumber == null;
    }
}
