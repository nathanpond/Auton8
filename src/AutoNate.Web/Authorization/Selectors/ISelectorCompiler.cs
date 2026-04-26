using System.Linq.Expressions;

namespace AutoNate.Web.Authorization.Selectors;

// Per-kind compiler that turns a SelectorAst into a LINQ predicate that EF Core
// can translate to SQL. Kinds register one compiler each. Compilers may throw
// SelectorCompilationException for syntactically valid selectors that the kind
// doesn't yet support (e.g. an unsupported tag).
public interface ISelectorCompiler
{
    string Kind { get; }
}

public interface ISelectorCompiler<T> : ISelectorCompiler
{
    Expression<Func<T, bool>> Compile(SelectorAst ast, CompilationContext context);
}

public sealed class SelectorCompilationException : Exception
{
    public SelectorCompilationException(string message) : base(message) { }
}
