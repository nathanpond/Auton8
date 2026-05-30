using System.Globalization;

namespace AutoNate.Web.Services.Query;

// Recursive-descent parser. Strict clause order:
//   [FROM <entity>] [WHERE <expr>] [ORDER BY ...] [COLUMNS(...)] [GROUP(...)] [LIMIT n]
// Each clause is optional. With FROM omitted, the leading WHERE keyword is
// also optional so `RecordType = "Car"` is a complete query equivalent to
// `FROM Records WHERE RecordType = "Car"`. Identifiers and keywords are both
// case-insensitive; the lexer canonicalizes keywords to upper-case.
internal sealed class AqlParser
{
    private readonly IReadOnlyList<AqlToken> _tokens;
    private int _pos;

    private AqlParser(IReadOnlyList<AqlToken> tokens)
    {
        _tokens = tokens;
    }

    public static AqlQuery Parse(string source)
    {
        var tokens = AqlLexer.Tokenize(source);
        var parser = new AqlParser(tokens);
        return parser.ParseQuery();
    }

    private AqlQuery ParseQuery()
    {
        string entity = "Records";
        string? entityArgument = null;
        var fromWasExplicit = false;

        if (Peek().Kind == TokenKind.Keyword && Peek().Text == "FROM")
        {
            Advance();
            var ent = Expect(TokenKind.Identifier, "Expected entity name after FROM.");
            entity = ent.Text;
            fromWasExplicit = true;

            // Parameterized FROM: `FROM Entity("argument")`. The argument is a
            // single string literal that the entity uses to resolve a concrete
            // surface (e.g. `Dataset("sales")` resolves to the Dataset named
            // "sales"). Per-entity validation in AqlValidator rejects args on
            // entities that haven't opted in, and missing args on entities
            // that require them.
            if (Peek().Kind == TokenKind.LParen)
            {
                Advance();
                var arg = Expect(TokenKind.String, "Expected a string literal argument after '(' in parameterized FROM.");
                entityArgument = arg.Text;
                Expect(TokenKind.RParen, "Expected ')' to close parameterized FROM.");
            }
        }

        AqlWhere? where = null;
        if (Peek().Kind == TokenKind.Keyword && Peek().Text == "WHERE")
        {
            Advance();
            where = ParseWhere();
        }
        else if (!fromWasExplicit && !IsClauseKeyword(Peek()) && Peek().Kind != TokenKind.Eof)
        {
            // FROM omitted, no leading WHERE — the rest is a bare expression
            // that we parse as a WHERE clause.
            where = ParseWhere();
        }

        var orderBy = ParseOrderBy();
        var columns = ParseColumns();
        var group = ParseGroup();
        var limit = ParseLimit();

        if (Peek().Kind != TokenKind.Eof)
        {
            throw new AqlValidationException(
                $"Unexpected token '{Peek().Text}' at position {Peek().Position}. " +
                "Clauses must appear in order: FROM, WHERE, ORDER BY, COLUMNS, GROUP, LIMIT.");
        }

        return new AqlQuery(entity, where, orderBy, columns, group, limit, entityArgument);
    }

    private static bool IsClauseKeyword(AqlToken t) =>
        t.Kind == TokenKind.Keyword && t.Text is "ORDER" or "COLUMNS" or "GROUP" or "LIMIT" or "WHERE";

    private IReadOnlyList<AqlOrderItem> ParseOrderBy()
    {
        if (Peek().Kind != TokenKind.Keyword || Peek().Text != "ORDER")
        {
            return Array.Empty<AqlOrderItem>();
        }
        Advance();
        if (Peek().Kind != TokenKind.Keyword || Peek().Text != "BY")
        {
            throw new AqlValidationException("Expected 'BY' after 'ORDER'.");
        }
        Advance();

        var items = new List<AqlOrderItem>();
        items.Add(ParseOrderItem());
        while (Peek().Kind == TokenKind.Comma)
        {
            Advance();
            items.Add(ParseOrderItem());
        }
        return items;
    }

    private AqlOrderItem ParseOrderItem()
    {
        var item = ParseSelectItem();
        var descending = false;
        if (Peek().Kind == TokenKind.Keyword)
        {
            if (Peek().Text == "ASC") { Advance(); descending = false; }
            else if (Peek().Text == "DESC") { Advance(); descending = true; }
        }
        return new AqlOrderItem(item, descending);
    }

    private IReadOnlyList<AqlSelectItem>? ParseColumns()
    {
        if (Peek().Kind != TokenKind.Keyword || Peek().Text != "COLUMNS") return null;
        Advance();
        Expect(TokenKind.LParen, "Expected '(' after COLUMNS.");
        var items = new List<AqlSelectItem> { ParseSelectItem() };
        while (Peek().Kind == TokenKind.Comma)
        {
            Advance();
            items.Add(ParseSelectItem());
        }
        Expect(TokenKind.RParen, "Expected ')' to close COLUMNS().");
        return items;
    }

    private IReadOnlyList<string>? ParseGroup()
    {
        if (Peek().Kind != TokenKind.Keyword || Peek().Text != "GROUP") return null;
        Advance();
        Expect(TokenKind.LParen, "Expected '(' after GROUP.");
        var items = new List<string>
        {
            Expect(TokenKind.Identifier, "Expected column name inside GROUP().").Text
        };
        while (Peek().Kind == TokenKind.Comma)
        {
            Advance();
            items.Add(Expect(TokenKind.Identifier, "Expected column name inside GROUP().").Text);
        }
        Expect(TokenKind.RParen, "Expected ')' to close GROUP().");
        return items;
    }

    private int? ParseLimit()
    {
        if (Peek().Kind != TokenKind.Keyword || Peek().Text != "LIMIT") return null;
        Advance();
        var num = Expect(TokenKind.Number, "Expected an integer after LIMIT.");
        if (!int.TryParse(num.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            throw new AqlValidationException($"LIMIT must be a positive integer; got '{num.Text}'.");
        }
        return n;
    }

    private AqlSelectItem ParseSelectItem()
    {
        var name = Expect(TokenKind.Identifier, "Expected a column name or aggregate function.");
        AqlSelectItem item;
        if (Peek().Kind == TokenKind.LParen)
        {
            Advance();
            string? argField = null;
            if (Peek().Kind != TokenKind.RParen)
            {
                argField = Expect(TokenKind.Identifier, "Aggregate functions take a single column argument.").Text;
            }
            Expect(TokenKind.RParen, $"Expected ')' to close {name.Text}().");
            item = new AqlSelectItem(
                Field: null,
                AggregateFn: name.Text.ToUpperInvariant(),
                AggregateField: argField);
        }
        else
        {
            item = new AqlSelectItem(Field: name.Text, AggregateFn: null, AggregateField: null);
        }

        // Optional `AS <alias>` — renames the result column without affecting
        // the underlying field/aggregate. Works for both fields and aggregates.
        if (Peek().Kind == TokenKind.Keyword && Peek().Text == "AS")
        {
            Advance();
            var alias = Expect(TokenKind.Identifier, "Expected an alias name after AS.").Text;
            item = item with { Alias = alias };
        }
        return item;
    }

    // ---- WHERE expression --------------------------------------------------
    // Precedence: OR  <  AND  <  primary (comparison, function call, parens).

    private AqlWhere ParseWhere() => ParseOr();

    private AqlWhere ParseOr()
    {
        var left = ParseAnd();
        while (Peek().Kind == TokenKind.Keyword && Peek().Text == "OR")
        {
            Advance();
            var right = ParseAnd();
            left = new AqlBinary("OR", left, right);
        }
        return left;
    }

    private AqlWhere ParseAnd()
    {
        var left = ParsePrimary();
        while (Peek().Kind == TokenKind.Keyword && Peek().Text == "AND")
        {
            Advance();
            var right = ParsePrimary();
            left = new AqlBinary("AND", left, right);
        }
        return left;
    }

    private AqlWhere ParsePrimary()
    {
        if (Peek().Kind == TokenKind.LParen)
        {
            Advance();
            var inner = ParseWhere();
            Expect(TokenKind.RParen, "Expected ')' to close grouped expression.");
            return inner;
        }

        var first = Expect(TokenKind.Identifier, "Expected a field name or function call.");

        // Function-style filter: CONTAINS(...), IN(...), BETWEEN(...), or a
        // scalar function like NUMNODES(), USESNODE("type").
        if (Peek().Kind == TokenKind.LParen)
        {
            Advance();
            var fn = first.Text.ToUpperInvariant();
            var args = new List<AqlValue>();

            // CONTAINS/IN/BETWEEN have a field as their first argument; we
            // grab it as an AqlString so downstream code can read it. For
            // every other function, all args are plain values.
            var isFieldArg = fn is "CONTAINS" or "IN" or "BETWEEN";
            if (Peek().Kind == TokenKind.Identifier && isFieldArg)
            {
                args.Add(new AqlString(Expect(TokenKind.Identifier, "expected field").Text));
                while (Peek().Kind == TokenKind.Comma)
                {
                    Advance();
                    args.Add(ParseValue());
                }
            }
            else if (Peek().Kind != TokenKind.RParen)
            {
                args.Add(ParseValue());
                while (Peek().Kind == TokenKind.Comma)
                {
                    Advance();
                    args.Add(ParseValue());
                }
            }
            Expect(TokenKind.RParen, $"Expected ')' to close {fn}().");

            // If the function call is followed by a comparison operator, it's
            // a scalar function on the LHS of a comparison: NUMNODES() > 5.
            if (Peek().Kind == TokenKind.Operator)
            {
                var op = Advance().Text;
                var rhs = ParseValue();
                return new AqlFunctionCompare(fn, args, op, rhs);
            }

            return fn switch
            {
                "CONTAINS" => BuildContains(args),
                "IN" => BuildIn(args),
                "BETWEEN" => BuildBetween(args),
                _ => new AqlFunctionCall(fn, args)
            };
        }

        // SQL-style infix: `field IN (v1, v2, ...)`. The prefix form
        // `IN(field, v1, v2, ...)` still works above and is identical.
        // Case-insensitive match against the next token's text — IN is
        // not a lexer keyword (we don't want to forbid it as a field name)
        // so we recognize it positionally here instead.
        if (Peek().Kind == TokenKind.Identifier
            && string.Equals(Peek().Text, "IN", StringComparison.OrdinalIgnoreCase)
            && PeekAt(1).Kind == TokenKind.LParen)
        {
            Advance(); // consume IN
            Advance(); // consume (
            var values = new List<AqlValue>();
            if (Peek().Kind != TokenKind.RParen)
            {
                values.Add(ParseValue());
                while (Peek().Kind == TokenKind.Comma)
                {
                    Advance();
                    values.Add(ParseValue());
                }
            }
            Expect(TokenKind.RParen, "Expected ')' to close IN(...) value list.");
            if (values.Count == 0)
            {
                throw new AqlValidationException(
                    $"IN(...) on field '{first.Text}' requires at least one value.");
            }
            return new AqlIn(first.Text, values);
        }

        // Otherwise, expect a comparison operator.
        var opTok = Expect(TokenKind.Operator, $"Expected an operator after field '{first.Text}'.");
        var value = ParseValue();
        return new AqlCompare(first.Text, opTok.Text, value);
    }

    private AqlValue ParseValue()
    {
        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.String:
                Advance();
                return new AqlString(t.Text);
            case TokenKind.Number:
                Advance();
                if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    throw new AqlValidationException($"Invalid number literal '{t.Text}'.");
                }
                return new AqlNumber(d);
            case TokenKind.Bool:
                Advance();
                return new AqlBool(t.Text == "TRUE");
            case TokenKind.Null:
                Advance();
                return new AqlNull();
            case TokenKind.RelativeDate:
                Advance();
                var rel = ParseRelativeDate(t.Text);
                // Optional "ago" sugar: `2w ago` → `-2w`. Matches how the
                // typical LLM and human phrase past-relative dates. We flip
                // sign only on positive magnitudes; `-2w ago` and `NOW ago`
                // are contradictory and rejected with a friendly error.
                if (Peek().Kind == TokenKind.Identifier
                    && string.Equals(Peek().Text, "ago", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    if (rel.Magnitude < 0)
                    {
                        throw new AqlValidationException(
                            $"'{t.Text} ago' is contradictory — drop the sign or drop 'ago'. " +
                            "For past dates write either '2w ago' or '-2w'.");
                    }
                    if (rel.Magnitude == 0)
                    {
                        throw new AqlValidationException(
                            $"'{t.Text} ago' has no offset. Use NOW for the current time, " +
                            "or a positive magnitude with 'ago' (e.g. 2w ago).");
                    }
                    rel = rel with { Magnitude = -rel.Magnitude };
                }
                return rel;
            default:
                throw new AqlValidationException(
                    $"Expected a value (string, number, bool, null, or relative date) at position {t.Position}; got '{t.Text}'.");
        }
    }

    private static AqlRelativeDate ParseRelativeDate(string lexeme)
    {
        // NOW is a parser-level alias for "zero offset from now"; AqlRelativeDate
        // already resolves (0,'d') to DateTime.UtcNow.AddDays(0) = now.
        if (string.Equals(lexeme, "NOW", StringComparison.OrdinalIgnoreCase))
        {
            return new AqlRelativeDate(0, 'd');
        }
        // RelativeDate token already conformed to [+-]?\d+[hdwmy].
        var suffix = char.ToLowerInvariant(lexeme[^1]);
        var numPart = lexeme[..^1];
        if (!int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var magnitude))
        {
            throw new AqlValidationException($"Invalid relative-date literal '{lexeme}'.");
        }
        return new AqlRelativeDate(magnitude, suffix);
    }

    private static AqlContains BuildContains(IReadOnlyList<AqlValue> args)
    {
        if (args.Count != 2 || args[0] is not AqlString fld || args[1] is not AqlString s)
        {
            throw new AqlValidationException("CONTAINS(field, \"substring\") requires a field and a string argument.");
        }
        return new AqlContains(fld.Value, s.Value);
    }

    private static AqlIn BuildIn(IReadOnlyList<AqlValue> args)
    {
        if (args.Count < 2 || args[0] is not AqlString fld)
        {
            throw new AqlValidationException("IN(field, v1, v2, ...) requires a field and at least one value.");
        }
        return new AqlIn(fld.Value, args.Skip(1).ToList());
    }

    private static AqlBetween BuildBetween(IReadOnlyList<AqlValue> args)
    {
        if (args.Count != 3 || args[0] is not AqlString fld)
        {
            throw new AqlValidationException("BETWEEN(field, low, high) requires three arguments.");
        }
        return new AqlBetween(fld.Value, args[1], args[2]);
    }

    // ---- Helpers -----------------------------------------------------------

    private AqlToken Peek() => _tokens[_pos];

    private AqlToken PeekAt(int offset)
    {
        var idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : _tokens[^1]; // last token is always Eof
    }

    private AqlToken Advance() => _tokens[_pos++];

    private AqlToken Expect(TokenKind kind, string message)
    {
        var t = Peek();
        if (t.Kind != kind)
        {
            throw new AqlValidationException(
                $"{message} (got '{t.Text}' at position {t.Position}.)");
        }
        return Advance();
    }
}
