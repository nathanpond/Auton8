using System.Text;

namespace AutoNate.Web.Services.Common;

public static class SqlPatternEscaper
{
    // Escapes the three wildcard literals (`\`, `%`, `_`) for use in a SQL
    // LIKE / ILIKE pattern. Consumers wrap the result with their own `%`
    // wildcards (e.g. `"%" + EscapeLike(userInput) + "%"`) so user input like
    // "50%" matches the literal "50%" instead of "anything-after-50". Pair
    // every consumer with `ESCAPE '\'` on the SQL side so Postgres treats the
    // emitted backslash as the escape character.
    public static string EscapeLike(string input)
    {
        var sb = new StringBuilder(input.Length + 8);
        foreach (var c in input)
        {
            if (c == '\\' || c == '%' || c == '_') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
