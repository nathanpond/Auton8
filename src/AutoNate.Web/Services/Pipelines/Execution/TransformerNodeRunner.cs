using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;
using AutoNate.Web.Services.Transformers.Code;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Looks up the transformer by node.Key in the ITransformerRegistry (built-
// ins + plugin-contributed). Falls through to the Phase 6 code-transformer
// store on a miss; matching code rows execute in the `services/executor/`
// sidecar via JetStreamCodeNodeRunner. Plugin-contributed transformers
// keep their existing precedence.
public sealed class TransformerNodeRunner(
    ITransformerRegistry registry,
    JetStreamCodeNodeRunner? codeRunner = null) : INodeRunner
{
    public string Kind => PipelineNodeKinds.Transformer;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        var key = context.Node.Key;
        if (registry.TryGet(key, out var transformer))
        {
            var config = context.Node.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
            return await transformer.RunAsync(context.Inputs, config, cancellationToken);
        }
        if (codeRunner is not null)
        {
            var code = await codeRunner.TryResolveAsync(key, cancellationToken);
            if (code is not null && code.Kind == CodeTransformerKinds.Transformer)
            {
                return await codeRunner.RunCodeAsync(
                    context.PipelineRunId, context.Node, code, context.Inputs, cancellationToken);
            }
        }
        throw new InvalidOperationException(
            $"Pipeline node '{context.Node.Id}' references unknown transformer '{key}'.");
    }
}
