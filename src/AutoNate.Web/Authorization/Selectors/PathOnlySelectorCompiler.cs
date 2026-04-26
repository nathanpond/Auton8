using System.Linq.Expressions;

namespace AutoNate.Web.Authorization.Selectors;

// Selector compiler used for kinds that only need path-level (kind/id) filtering
// in this phase. Tag predicates throw with a clear error so misuse surfaces
// loudly until the relevant kind grows a real compiler.
public sealed class PathOnlySelectorCompiler<T> : SelectorCompilerBase<T> where T : class
{
    private readonly Expression<Func<T, Guid>> _idSelector;

    public PathOnlySelectorCompiler(string kind, Expression<Func<T, Guid>> idSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(idSelector);
        Kind = kind;
        _idSelector = idSelector;
    }

    public override string Kind { get; }

    protected override Expression<Func<T, Guid>> IdSelector => _idSelector;
}
