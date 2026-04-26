namespace AutoNate.Web.Authorization.Selectors;

public sealed class SelectorParseException : Exception
{
    public SelectorParseException(string message, int position, string source)
        : base($"{message} at position {position} in '{source}'.")
    {
        Position = position;
        Source = source;
    }

    public int Position { get; }

    public new string Source { get; }
}
