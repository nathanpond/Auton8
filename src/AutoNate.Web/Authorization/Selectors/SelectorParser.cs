namespace AutoNate.Web.Authorization.Selectors;

public static class SelectorParser
{
    public static SelectorAst Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cursor = new Cursor(input);
        cursor.SkipWhitespace();
        var ast = ParseSelector(ref cursor);
        cursor.SkipWhitespace();
        if (!cursor.IsEnd)
        {
            throw cursor.Error("unexpected trailing input");
        }

        return ast;
    }

    private static SelectorAst ParseSelector(ref Cursor c)
    {
        c.Expect('/');
        var kinds = ParseSetOrSingle(ref c, Context.Path);

        IReadOnlyList<string>? ids = null;
        if (c.TryConsume('/'))
        {
            // The selector either continues with an idset, or jumps straight to
            // the predicate (e.g. `/record/[...]`). Peek to decide.
            if (c.Peek() == '[')
            {
                ids = null;
            }
            else
            {
                ids = ParseSetOrSingle(ref c, Context.Path);
            }
        }

        // Allow an optional `/` between idset and predicate so paths can read like
        // `/workflowexecution/{a,b,c}/[scope=role:supervisor;assignee=user]`.
        if (c.Peek() == '/' && c.PeekAt(1) == '[')
        {
            c.Advance();
        }

        PredicateNode? predicate = null;
        if (c.Peek() == '[')
        {
            predicate = ParsePredicate(ref c);
        }

        return new SelectorAst
        {
            Path = new PathNode { Kinds = kinds, Ids = ids },
            Predicate = predicate
        };
    }

    private static IReadOnlyList<string> ParseSetOrSingle(ref Cursor c, Context context)
    {
        if (c.TryConsume('{'))
        {
            var items = new List<string>();
            while (true)
            {
                c.SkipWhitespace();
                items.Add(ParseToken(ref c, context));
                c.SkipWhitespace();
                if (c.TryConsume(','))
                {
                    continue;
                }

                c.Expect('}');
                break;
            }

            if (items.Count == 0)
            {
                throw c.Error("empty set");
            }

            return items;
        }

        var single = ParseToken(ref c, context);
        return new[] { single };
    }

    private static string ParseToken(ref Cursor c, Context context)
    {
        if (c.TryConsume('*'))
        {
            return SelectorTokens.Wildcard;
        }

        if (c.Peek() == '"')
        {
            return ParseQuoted(ref c);
        }

        var start = c.Position;
        while (!c.IsEnd && IsTokenChar(c.Peek(), context))
        {
            c.Advance();
        }

        if (c.Position == start)
        {
            throw c.Error("expected name");
        }

        return c.Slice(start, c.Position);
    }

    private static string ParseName(ref Cursor c)
    {
        var start = c.Position;
        while (!c.IsEnd && IsNameChar(c.Peek()))
        {
            c.Advance();
        }

        if (c.Position == start)
        {
            throw c.Error("expected name");
        }

        return c.Slice(start, c.Position);
    }

    private static string ParseQuoted(ref Cursor c)
    {
        c.Expect('"');
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            if (c.IsEnd)
            {
                throw c.Error("unterminated quoted value");
            }

            var ch = c.Peek();
            if (ch == '"')
            {
                c.Advance();
                return builder.ToString();
            }

            if (ch == '\\')
            {
                c.Advance();
                if (c.IsEnd)
                {
                    throw c.Error("unterminated escape");
                }

                builder.Append(c.Peek());
                c.Advance();
                continue;
            }

            builder.Append(ch);
            c.Advance();
        }
    }

    private static PredicateNode ParsePredicate(ref Cursor c)
    {
        c.Expect('[');
        var exprs = new List<PredicateExpr>();
        while (true)
        {
            c.SkipWhitespace();
            exprs.Add(ParseExpr(ref c));
            c.SkipWhitespace();
            if (c.TryConsume(';') || c.TryConsume(','))
            {
                continue;
            }

            c.Expect(']');
            break;
        }

        if (exprs.Count == 0)
        {
            throw c.Error("empty predicate");
        }

        return new PredicateNode { Expressions = exprs };
    }

    private static PredicateExpr ParseExpr(ref Cursor c)
    {
        var key = ParseName(ref c);
        c.SkipWhitespace();

        if (c.TryConsume('='))
        {
            c.SkipWhitespace();
            var value = ParseValue(ref c);
            PredicateNode? nested = null;
            if (c.Peek() == '[')
            {
                nested = ParsePredicate(ref c);
            }

            return new TagExpr { Tag = key, Value = value, Nested = nested };
        }

        if (c.TryConsume(':'))
        {
            var qualifier = ParseName(ref c);
            return new ScopeExpr { Tag = key, Qualifier = qualifier };
        }

        throw c.Error("expected '=' or ':'");
    }

    private static ValueNode ParseValue(ref Cursor c)
    {
        if (c.TryConsume('*'))
        {
            return new WildcardValue();
        }

        if (c.Peek() == '"')
        {
            return new LiteralValue { Text = ParseQuoted(ref c) };
        }

        var first = ParseName(ref c);

        if (c.TryConsume(':'))
        {
            var name = ParseName(ref c);
            return new QualifiedValue { Qualifier = first, Name = name };
        }

        if (string.Equals(first, SelectorTokens.CurrentUser, StringComparison.Ordinal))
        {
            string? pinned = null;
            if (c.TryConsume('/'))
            {
                pinned = ParseName(ref c);
            }

            return new CurrentUserValue { PinnedId = pinned };
        }

        return new LiteralValue { Text = first };
    }

    private static bool IsNameChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';

    private static bool IsTokenChar(char c, Context context) =>
        context == Context.Path
            ? IsNameChar(c)
            : IsNameChar(c);

    private enum Context
    {
        Path,
        Predicate
    }

    private struct Cursor
    {
        private readonly string _src;
        private int _pos;

        public Cursor(string src)
        {
            _src = src;
            _pos = 0;
        }

        public int Position => _pos;

        public bool IsEnd => _pos >= _src.Length;

        public char Peek() => IsEnd ? '\0' : _src[_pos];

        public char PeekAt(int offset) =>
            _pos + offset >= _src.Length ? '\0' : _src[_pos + offset];

        public void Advance() => _pos++;

        public string Slice(int start, int end) => _src[start..end];

        public bool TryConsume(char ch)
        {
            if (Peek() == ch)
            {
                _pos++;
                return true;
            }

            return false;
        }

        public void Expect(char ch)
        {
            if (!TryConsume(ch))
            {
                throw Error($"expected '{ch}'");
            }
        }

        public void SkipWhitespace()
        {
            while (!IsEnd && char.IsWhiteSpace(Peek()))
            {
                _pos++;
            }
        }

        public SelectorParseException Error(string message) => new(message, _pos, _src);
    }
}
