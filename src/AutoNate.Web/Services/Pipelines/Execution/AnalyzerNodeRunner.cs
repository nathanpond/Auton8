using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Analyzers;

namespace AutoNate.Web.Services.Pipelines.Execution;

public sealed class AnalyzerNodeRunner(IAnalyzerRegistry registry) : INodeRunner
{
    public string Kind => PipelineNodeKinds.Analyzer;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        var key = context.Node.Key;
        if (!registry.TryGet(key, out var analyzer))
        {
            throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' references unknown analyzer '{key}'.");
        }
        if (context.Inputs.Count == 0)
        {
            return DataFrame.Empty;
        }
        var config = context.Node.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return await analyzer.RunAsync(context.Inputs[0], config, cancellationToken);
    }
}
