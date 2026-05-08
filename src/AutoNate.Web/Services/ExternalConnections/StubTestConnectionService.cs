using System.Diagnostics;

namespace AutoNate.Web.Services.ExternalConnections;

// Phase-2 fallback. Confirms the secret decrypts cleanly and the metadata
// parses; doesn't actually contact the remote provider. Phase 4 ships
// AnthropicTestConnectionService / OpenAITestConnectionService and registers
// them in front of this one (kind-routed via IChatProviderResolver).
public sealed class StubTestConnectionService : ITestConnectionService
{
    private readonly IExternalConnectionStore _store;

    public StubTestConnectionService(IExternalConnectionStore store)
    {
        _store = store;
    }

    public async Task<TestConnectionResult> TestAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var revealed = await _store.RevealForResolverAsync(connectionId, cancellationToken);
            sw.Stop();
            if (revealed is null)
            {
                return new TestConnectionResult(
                    Ok: false,
                    LatencyMs: sw.ElapsedMilliseconds,
                    ModelEcho: null,
                    Error: "Connection has no stored secret. Enter an api key and save before testing.");
            }
            return new TestConnectionResult(
                Ok: true,
                LatencyMs: sw.ElapsedMilliseconds,
                ModelEcho: $"stub:{revealed.Kind}",
                Error: null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestConnectionResult(
                Ok: false,
                LatencyMs: sw.ElapsedMilliseconds,
                ModelEcho: null,
                Error: ex.Message);
        }
    }
}
