using System.Text;

namespace AutoNate.Web.Services.Query;

internal static class AqlLexer
{
    // Reserved words. Case-insensitive at the source; the lexer upper-cases
    // the lexeme before classifying so the parser only needs to compare to
    // the canonical form.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "FROM", "WHERE", "ORDER", "BY", "ASC", "DESC",
        "COLUMNS", "GROUP", "LIMIT",
        "AND", "OR", "AS"
    };

    public static IReadOnlyList<AqlToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<AqlToken>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Numbers and relative dates. A sign prefix counts only when
            // immediately followed by a digit. `-Foo` is never a number; `-2w`
            // always is a RelativeDate.
            if ((c == '-' || c == '+') && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                tokens.Add(ReadNumericOrRelativeDate(source, ref i));
                continue;
            }
            if (char.IsDigit(c))
            {
                tokens.Add(ReadNumericOrRelativeDate(source, ref i));
                continue;
            }

            if (c == '"' || c == '\'')
            {
                tokens.Add(ReadString(source, ref i));
                continue;
            }

            if (c == '(') { tokens.Add(new AqlToken(TokenKind.LParen, "(", i)); i++; continue; }
            if (c == ')') { tokens.Add(new AqlToken(TokenKind.RParen, ")", i)); i++; continue; }
            if (c == ',') { tokens.Add(new AqlToken(TokenKind.Comma, ",", i)); i++; continue; }

            if (c == '=' || c == '<' || c == '>' || c == '!' || c == '~')
            {
                tokens.Add(ReadOperator(source, ref i));
                continue;
            }

            if (IsIdentStart(c))
            {
                tokens.Add(ReadIdentifierOrKeyword(source, ref i));
                continue;
            }

            throw new AqlValidationException($"Unexpected character '{c}' at position {i}.");
        }

        tokens.Add(new AqlToken(TokenKind.Eof, string.Empty, source.Length));
        return tokens;
    }

    private static AqlToken ReadNumericOrRelativeDate(string source, ref int i)
    {
        var start = i;
        if (source[i] == '-' || source[i] == '+') i++;
        while (i < source.Length && char.IsDigit(source[i])) i++;

        // Optional fractional part.
        var hasFraction = false;
        if (i < source.Length && source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
        {
            hasFraction = true;
            i++;
            while (i < source.Length && char.IsDigit(source[i])) i++;
        }

        // Relative-date suffix: h | d | w | m | y, not followed by another
        // identifier char (so `2hours` stays a Number(2) + Identifier(hours)).
        if (!hasFraction && i < source.Length)
        {
            var ch = source[i];
            if ((ch == 'h' || ch == 'd' || ch == 'w' || ch == 'm' || ch == 'y' ||
                 ch == 'H' || ch == 'D' || ch == 'W' || ch == 'M' || ch == 'Y')
                && (i + 1 >= source.Length || !IsIdentPart(source[i + 1])))
            {
                i++;
                return new AqlToken(TokenKind.RelativeDate,
                    source.Substring(start, i - start), start);
            }
        }

        return new AqlToken(TokenKind.Number,
            source.Substring(start, i - start), start);
    }

    private static AqlToken ReadString(string source, ref int i)
    {
        var quote = source[i];
        var start = i;
        i++;
        var sb = new StringBuilder();
        while (i < source.Length && source[i] != quote)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                var next = source[i + 1];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    _ => next
                });
                i += 2;
                continue;
            }
            sb.Append(source[i]);
            i++;
        }
        if (i >= source.Length)
        {
            throw new AqlValidationException(
                $"Unterminated string literal starting at position {start}.");
        }
        i++; // skip closing quote
        return new AqlToken(TokenKind.String, sb.ToString(), start);
    }

    private static AqlToken ReadOperator(string source, ref int i)
    {
        var start = i;
        var c = source[i];
        if (c == '!' && i + 1 < source.Length && source[i + 1] == '=')
        {
            i += 2;
            return new AqlToken(TokenKind.Operator, "!=", start);
        }
        if ((c == '<' || c == '>') && i + 1 < source.Length && source[i + 1] == '=')
        {
            var op = source.Substring(i, 2);
            i += 2;
            return new AqlToken(TokenKind.Operator, op, start);
        }
        if (c == '!')
        {
            throw new AqlValidationException(
                $"Unexpected '!' at position {start}. Did you mean '!='?");
        }
        i++;
        return new AqlToken(TokenKind.Operator, c.ToString(), start);
    }

    private static AqlToken ReadIdentifierOrKeyword(string source, ref int i)
    {
        var start = i;
        while (i < source.Length && IsIdentPart(source[i])) i++;
        var lexeme = source.Substring(start, i - start);
        var upper = lexeme.ToUpperInvariant();

        if (upper == "TRUE" || upper == "FALSE")
        {
            return new AqlToken(TokenKind.Bool, upper, start);
        }
        if (upper == "NULL")
        {
            return new AqlToken(TokenKind.Null, "NULL", start);
        }
        // NOW is desugared at parse time into AqlRelativeDate(0,'d') so every
        // entity's existing relative-date plumbing handles it for free. The
        // lexeme stays "NOW" so error messages quote what the user wrote.
        if (upper == "NOW")
        {
            return new AqlToken(TokenKind.RelativeDate, "NOW", start);
        }
        if (Keywords.Contains(upper))
        {
            return new AqlToken(TokenKind.Keyword, upper, start);
        }
        return new AqlToken(TokenKind.Identifier, lexeme, start);
    }

    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
