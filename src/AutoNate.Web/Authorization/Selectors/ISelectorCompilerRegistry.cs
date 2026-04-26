namespace AutoNate.Web.Authorization.Selectors;

public interface ISelectorCompilerRegistry
{
    ISelectorCompiler<T>? TryGetFor<T>(string kind) where T : class;
}
