namespace AutoNate.Plugins.Abstractions;

public interface IFilterHub
{
    T Apply<T>(string hookName, T value, params object?[] args);
    Task<T> ApplyAsync<T>(string hookName, T value, CancellationToken cancellationToken = default, params object?[] args);
    bool HasFilter(string hookName);
}
