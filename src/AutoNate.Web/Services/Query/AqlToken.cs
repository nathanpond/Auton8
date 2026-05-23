namespace AutoNate.Web.Services.Query;

internal enum TokenKind
{
    Keyword,
    Identifier,
    String,
    Number,
    RelativeDate,
    Bool,
    Null,
    Operator,
    LParen,
    RParen,
    Comma,
    Eof
}

// Position is a 0-based character offset into the source for error reporting.
// Text is the lexeme exactly as the lexer captured it (with surrounding quotes
// stripped for String; including sign+suffix for RelativeDate).
internal readonly record struct AqlToken(TokenKind Kind, string Text, int Position)
{
    public override string ToString() => Kind switch
    {
        TokenKind.Eof => "<eof>",
        TokenKind.String => $"\"{Text}\"",
        _ => Text
    };
}
