namespace AutoNate.Web.Services.ExternalConnections;

// Pluggable: each connection kind ships its own validator. The Phase-2
// default is a stub that just confirms credentials decrypt cleanly; Phase 4
// replaces it with a kind-specific implementation that actually pings the
// remote provider (Anthropic, OpenAI) with a tiny request and reports
// latency.
public interface ITestConnectionService
{
    Task<TestConnectionResult> TestAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

public sealed record class TestConnectionResult(
    bool Ok,
    long LatencyMs,
    string? ModelEcho,
    string? Error);
