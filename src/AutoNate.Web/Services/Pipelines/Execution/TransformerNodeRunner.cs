using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Looks up the transformer by node.Key in the ITransformerRegistry (built-
// ins + plugin-contributed), then runs it against the materialised upstream
// frames. Plugin-contributed transformers flow through the same path via
// the registry's adapter.
public sealed class TransformerNodeRunner(ITransformerRegistry registry) : INodeRunner
{
    public string Kind => PipelineNodeKinds.Transformer;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        var key = context.Node.Key;
        if (!registry.TryGet(key, out var transformer))
        {
            throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' references unknown transformer '{key}'.");
        }
        var config = context.Node.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return await transformer.RunAsync(context.Inputs, config, cancellationToken);
    }
}
