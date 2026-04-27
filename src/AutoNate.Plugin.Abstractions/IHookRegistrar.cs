namespace AutoNate.Plugins.Abstractions;

public interface IHookRegistrar
{
    HookHandle AddAction(string hookName, int priority, Action<object?[]> callback);
    HookHandle AddActionAsync(string hookName, int priority, Func<object?[], CancellationToken, Task> callback);
    void RemoveAction(HookHandle handle);

    HookHandle AddFilter<T>(string hookName, int priority, Func<T, object?[], T> callback);
    HookHandle AddFilterAsync<T>(string hookName, int priority, Func<T, object?[], CancellationToken, Task<T>> callback);
    void RemoveFilter(HookHandle handle);
}
