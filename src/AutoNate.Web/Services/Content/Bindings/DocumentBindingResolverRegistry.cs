namespace AutoNate.Web.Services.Content.Bindings;

// Maps a binding's `kind` discriminator to the resolver instance that
// handles it. Registered as singleton in DI; per-kind resolvers are
// stateless except for their injected dependencies.
public interface IDocumentBindingResolverRegistry
{
    IDocumentBindingResolver Get(string kind);
    bool Has(string kind);
}

public sealed class DocumentBindingResolverRegistry : IDocumentBindingResolverRegistry
{
    private readonly Dictionary<string, IDocumentBindingResolver> _byKind;

    public DocumentBindingResolverRegistry(IEnumerable<IDocumentBindingResolver> resolvers)
    {
        _byKind = resolvers.ToDictionary(r => r.Kind, StringComparer.Ordinal);
    }

    public IDocumentBindingResolver Get(string kind) =>
        _byKind.TryGetValue(kind, out var r)
            ? r
            : throw new DocumentBindingResolveException(
                $"No resolver registered for binding kind '{kind}'.", 400);

    public bool Has(string kind) => _byKind.ContainsKey(kind);
}
