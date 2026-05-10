using System.Linq.Expressions;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Authorization.Selectors;

// Compiles selectors targeting `workflowmodel` into LINQ predicates over the
// scaffolded WorkflowModel entity. Replaces the path-only compiler so the
// `processkey` / `draft` / `published` tags advertised in CoreEntityTypes
// actually apply at filter time instead of being silently skipped.
public sealed class WorkflowModelSelectorCompiler : SelectorCompilerBase<WorkflowModelEntity>
{
    public override string Kind => EntityKinds.WorkflowModel;

    protected override Expression<Func<WorkflowModelEntity, Guid>> IdSelector => m => m.Id;

    protected override Expression<Func<WorkflowModelEntity, bool>> CompileExpr(
        PredicateExpr expr, CompilationContext context)
    {
        if (expr is not TagExpr tag)
        {
            throw new SelectorCompilationException(
                $"Unsupported predicate expression: {expr.GetType().Name}");
        }

        return tag.Tag.ToLowerInvariant() switch
        {
            "processkey" => CompileProcessKey(tag),
            "draft" => CompileDraft(tag),
            "published" => CompilePublished(tag),
            _ => throw new SelectorCompilationException(
                $"Unknown workflowmodel tag '{tag.Tag}'.")
        };
    }

    private static Expression<Func<WorkflowModelEntity, bool>> CompileProcessKey(TagExpr tag)
    {
        var key = RequireLiteral(tag);
        return m => m.ProcessKey == key;
    }

    private static Expression<Func<WorkflowModelEntity, bool>> CompileDraft(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return m => m.IsDraft == expected;
    }

    // `published` is derived: a workflow_models row is "published" iff it has
    // a non-null PublishedVersionNumber. Modeling it this way keeps grant
    // selectors aligned with how the SPA labels the same rows.
    private static Expression<Func<WorkflowModelEntity, bool>> CompilePublished(TagExpr tag)
    {
        var expected = ParseBoolLiteral(tag);
        return expected
            ? m => m.PublishedVersionNumber != null
            : m => m.PublishedVersionNumber == null;
    }
}
