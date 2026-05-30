using System.Security.Claims;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Per-node executor contract (Phase 5 of the Data Stores plan). The
// orchestrator instantiates one runner per node kind and calls
// RunAsync with the materialised upstream DataFrames. Returning null is
// reserved for sink nodes whose output isn't consumed by downstream nodes;
// transformer/analyzer/source runners must return a non-null frame.
public interface INodeRunner
{
    string Kind { get; }

    Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default);
}

public sealed record class NodeRunnerContext(
    PipelineNode Node,
    IReadOnlyList<DataFrame> Inputs,
    ClaimsPrincipal Actor,
    // The pipeline run that owns the current invocation. Phase 6's
    // JetStream code-node runner uses it as part of the subject name so
    // the sidecar's per-message log lines and any future cancel signals
    // can attribute the work back to the run.
    Guid PipelineRunId);

public sealed class NodeRunnerNotFoundException(string kind)
    : Exception($"No node runner registered for kind '{kind}'.");

public interface INodeRunnerRegistry
{
    bool TryGet(string kind, out INodeRunner runner);
}

public sealed class NodeRunnerRegistry : INodeRunnerRegistry
{
    private readonly IReadOnlyDictionary<string, INodeRunner> _byKind;

    public NodeRunnerRegistry(IEnumerable<INodeRunner> runners)
    {
        _byKind = runners.ToDictionary(r => r.Kind, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string kind, out INodeRunner runner)
    {
        if (_byKind.TryGetValue(kind, out var found))
        {
            runner = found;
            return true;
        }
        runner = null!;
        return false;
    }
}
