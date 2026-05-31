using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Analyzers;

public interface IAnalyzer
{
    string Key { get; }

    string DisplayName { get; }

    Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);
}
