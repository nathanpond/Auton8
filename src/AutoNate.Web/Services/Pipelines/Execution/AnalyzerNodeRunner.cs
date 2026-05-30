using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Analyzers;
using AutoNate.Web.Services.Transformers.Code;

namespace AutoNate.Web.Services.Pipelines.Execution;

public sealed class AnalyzerNodeRunner(
    IAnalyzerRegistry registry,
    JetStreamCodeNodeRunner? codeRunner = null) : INodeRunner
{
    public string Kind => PipelineNodeKinds.Analyzer;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        var key = context.Node.Key;
        if (registry.TryGet(key, out var analyzer))
        {
            if (context.Inputs.Count == 0) return DataFrame.Empty;
            var config = context.Node.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
            return await analyzer.RunAsync(context.Inputs[0], config, cancellationToken);
        }
        if (codeRunner is not null)
        {
            var code = await codeRunner.TryResolveAsync(key, cancellationToken);
            if (code is not null && code.Kind == CodeTransformerKinds.Analyzer)
            {
                return await codeRunner.RunCodeAsync(
                    context.PipelineRunId, context.Node, code, context.Inputs, cancellationToken);
            }
        }
        throw new InvalidOperationException(
            $"Pipeline node '{context.Node.Id}' references unknown analyzer '{key}'.");
    }
}
