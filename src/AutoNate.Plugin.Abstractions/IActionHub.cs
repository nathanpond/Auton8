namespace AutoNate.Plugins.Abstractions;

public interface IActionHub
{
    void Do(string hookName, params object?[] args);
    Task DoAsync(string hookName, CancellationToken cancellationToken = default, params object?[] args);
    bool HasAction(string hookName);
}
