namespace AutoNate.Plugins.Abstractions;

public interface IAutoNatePlugin
{
    string Name { get; }
    string Version { get; }
    void Configure(IHookRegistrar registrar, IServiceProvider hostServices);
}
