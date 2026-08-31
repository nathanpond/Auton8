namespace AutoNate.Web.Services.Datasets.Files;

// DI-resolved index of every IDatasetFileParser registered in the host.
// Looked up by datasets.parser_kind. Unknown kinds throw rather than
// silently fall back so a misconfigured dataset surfaces a clean error
// instead of returning empty rows.
public sealed class DatasetFileParserRegistry
{
    private readonly IReadOnlyDictionary<string, IDatasetFileParser> _byKind;

    public DatasetFileParserRegistry(IEnumerable<IDatasetFileParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _byKind = parsers.ToDictionary(p => p.Kind, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> Kinds => _byKind.Keys;

    public IDatasetFileParser Get(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InvalidOperationException("Dataset parser kind is required.");
        }
        if (!_byKind.TryGetValue(kind, out var parser))
        {
            throw new InvalidOperationException(
                $"No dataset file parser is registered for kind '{kind}'. " +
                $"Registered kinds: [{string.Join(", ", _byKind.Keys)}].");
        }
        return parser;
    }
}
