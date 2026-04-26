using System.Text;

namespace AutoNate.Web.Authorization.Selectors;

public static class SelectorPrinter
{
    public static string ToCanonicalString(SelectorAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var sb = new StringBuilder();
        WritePath(sb, ast.Path);
        if (ast.Predicate is not null)
        {
            WritePredicate(sb, ast.Predicate);
        }

        return sb.ToString();
    }

    private static void WritePath(StringBuilder sb, PathNode path)
    {
        sb.Append('/');
        WriteSetOrSingle(sb, path.Kinds);
        if (path.Ids is not null)
        {
            sb.Append('/');
            WriteSetOrSingle(sb, path.Ids);
        }
    }

    private static void WriteSetOrSingle(StringBuilder sb, IReadOnlyList<string> items)
    {
        if (items.Count == 1)
        {
            WriteToken(sb, items[0]);
            return;
        }

        sb.Append('{');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            WriteToken(sb, items[i]);
        }

        sb.Append('}');
    }

    private static void WriteToken(StringBuilder sb, string token)
    {
        if (token == SelectorTokens.Wildcard)
        {
            sb.Append('*');
            return;
        }

        if (NeedsQuoting(token))
        {
            sb.Append('"');
            foreach (var ch in token)
            {
                if (ch == '"' || ch == '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(ch);
            }

            sb.Append('"');
            return;
        }

        sb.Append(token);
    }

    private static void WritePredicate(StringBuilder sb, PredicateNode predicate)
    {
        sb.Append('[');
        for (var i = 0; i < predicate.Expressions.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            WriteExpr(sb, predicate.Expressions[i]);
        }

        sb.Append(']');
    }

    private static void WriteExpr(StringBuilder sb, PredicateExpr expr)
    {
        switch (expr)
        {
            case TagExpr tag:
                sb.Append(tag.Tag);
                sb.Append('=');
                WriteValue(sb, tag.Value);
                if (tag.Nested is not null)
                {
                    WritePredicate(sb, tag.Nested);
                }

                break;
            case ScopeExpr scope:
                sb.Append(scope.Tag);
                sb.Append(':');
                sb.Append(scope.Qualifier);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported predicate expression: {expr.GetType().Name}");
        }
    }

    private static void WriteValue(StringBuilder sb, ValueNode value)
    {
        switch (value)
        {
            case WildcardValue:
                sb.Append('*');
                break;
            case CurrentUserValue user:
                sb.Append(SelectorTokens.CurrentUser);
                if (user.PinnedId is not null)
                {
                    sb.Append('/');
                    sb.Append(user.PinnedId);
                }

                break;
            case QualifiedValue qual:
                sb.Append(qual.Qualifier);
                sb.Append(':');
                sb.Append(qual.Name);
                break;
            case LiteralValue lit:
                WriteToken(sb, lit.Text);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported value node: {value.GetType().Name}");
        }
    }

    private static bool NeedsQuoting(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        foreach (var ch in token)
        {
            var ok = (ch is >= 'A' and <= 'Z')
                  || (ch is >= 'a' and <= 'z')
                  || (ch is >= '0' and <= '9')
                  || ch is '_' or '-';
            if (!ok)
            {
                return true;
            }
        }

        return false;
    }
}
