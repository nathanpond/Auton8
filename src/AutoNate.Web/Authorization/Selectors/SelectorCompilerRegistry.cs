namespace AutoNate.Web.Authorization.Selectors;

public sealed class SelectorCompilerRegistry : ISelectorCompilerRegistry
{
    private readonly Dictionary<(string Kind, Type Clr), ISelectorCompiler> _byKindAndType;

    public SelectorCompilerRegistry(IEnumerable<ISelectorCompiler> compilers)
    {
        ArgumentNullException.ThrowIfNull(compilers);

        _byKindAndType = new Dictionary<(string, Type), ISelectorCompiler>();
        foreach (var compiler in compilers)
        {
            var clrType = ResolveTargetType(compiler.GetType());
            if (clrType is null) continue;

            var key = (compiler.Kind, clrType);
            if (!_byKindAndType.TryAdd(key, compiler))
            {
                throw new InvalidOperationException(
                    $"Duplicate selector compiler registration for kind '{compiler.Kind}' targeting {clrType.FullName}.");
            }
        }
    }

    public ISelectorCompiler<T>? TryGetFor<T>(string kind) where T : class
    {
        if (_byKindAndType.TryGetValue((kind, typeof(T)), out var compiler))
        {
            return (ISelectorCompiler<T>)compiler;
        }

        return null;
    }

    private static Type? ResolveTargetType(Type compilerType)
    {
        foreach (var iface in compilerType.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(ISelectorCompiler<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
