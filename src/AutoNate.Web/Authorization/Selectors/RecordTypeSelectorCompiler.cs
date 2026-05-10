using System.Linq.Expressions;
using AutoNate.Web.Services.Records;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

namespace AutoNate.Web.Authorization.Selectors;

// Compiles selectors for `recordtype`. CoreEntityTypes advertises `shortcode`
// and `archived` tags; both map to columns on the entity directly.
public sealed class RecordTypeSelectorCompiler : SelectorCompilerBase<RecordTypeEntity>
{
    public override string Kind => EntityKinds.RecordType;

    protected override Expression<Func<RecordTypeEntity, Guid>> IdSelector => t => t.Id;

    protected override Expression<Func<RecordTypeEntity, bool>> CompileExpr(
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
            "archived" => CompileArchived(tag),
            _ => throw new SelectorCompilationException(
                $"Unknown recordtype tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<RecordTypeEntity, bool>> CompileShortCode(TagExpr tag)
    {
        // Stored shortcodes are uppercased by RecordTypeShortCode.Normalize on
        // create, so admins who author `[shortcode=lead]` (lowercase) need the
        // same normalization here or the grant silently misses every row.
        var code = RecordTypeShortCode.Normalize(RequireLiteral(tag));
        return t => t.ShortCode == code;
    }

    private static Expression<Func<RecordTypeEntity, bool>> CompileArchived(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return t => t.IsArchived == expected;
    }
}
