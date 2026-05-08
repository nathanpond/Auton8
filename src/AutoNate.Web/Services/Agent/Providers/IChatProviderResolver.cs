namespace AutoNate.Web.Services.Agent.Providers;

// Resolves an IChatProvider from an External Connection row: loads the row,
// decrypts the secret via IConnectionSecretProtector, parses the kind-specific
// metadata, and constructs the provider with a configured HttpClient. The
// agent loop calls this once per turn, gives the provider the request, and
// discards it — so providers can hold per-conversation state safely if needed.
public interface IChatProviderResolver
{
    Task<IChatProvider?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default);

    Task<IChatProvider?> ResolveDefaultForKindAsync(string kind, CancellationToken cancellationToken = default);
}
