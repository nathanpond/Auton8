using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Tests;

/// <summary>
/// An <see cref="ILogger{T}"/> that remembers what it was told (#547).
/// </summary>
/// <remarks>
/// <para>
/// There was no logger-capture anywhere in this suite, which is why a whole
/// acceptance criterion went unguarded: #524's AC3 is "starts nothing **and says
/// so**", and the only test covering it injected <c>NullLogger</c>, so deleting
/// the warning left everything green — reducing the AC to "starts nothing
/// silently", the behaviour it was written to forbid.
/// </para>
/// <para>
/// Deliberately minimal. It records level, the rendered message and the
/// exception, which is all an assertion about "did we say so" needs. Structured
/// state is not captured: a test asserting on the shape of a log's property bag
/// is asserting on the logging framework, not on the behaviour.
/// </para>
/// </remarks>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries;

    public IEnumerable<string> MessagesAt(LogLevel level) =>
        _entries.Where(e => e.Level == level).Select(e => e.Message);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    // Everything is enabled: a capturing logger that filtered would make a test
    // pass because the level was off, which is the same false green in miniature.
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add((logLevel, formatter(state, exception), exception));

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
