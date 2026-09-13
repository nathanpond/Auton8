using System.Text;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Source with its comments removed, for guards that scan code as text (#393).
/// </summary>
/// <remarks>
/// <para>
/// Two guards shipped in round 17 asking "does this file contain X?" over raw
/// text. Both were defeated by leaving X in a comment — which is not a contrived
/// attack, it is the normal shape of a rename:
/// </para>
/// <code>
/// // renamed from "deploy the BPMN workflow" for consistency
/// await EnsureSuccessAsync(response, "deploy the workflow");
/// </code>
/// <para>
/// That left 74/74 green while every publish refusal in production degraded to
/// the generic fallback. A guard that a comment can satisfy is asking about the
/// text, not about the program.
/// </para>
/// <para>
/// String-aware, because a comment marker inside a string literal is not a
/// comment — <c>"http://x"</c> must not truncate the line. Verbatim strings
/// (<c>@"..."</c>) and both quote styles are handled; interpolation braces need
/// no special case because this only removes comments.
/// </para>
/// </remarks>
internal static class SourceText
{
    /// <summary>Strips <c>//</c> and <c>/* */</c> comments, preserving string literals.</summary>
    public static string WithoutComments(string source)
    {
        var output = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            // A string literal: copy it whole, so a // or /* inside it survives.
            if (c is '"' or '\'')
            {
                var verbatim = c == '"' && i > 0 && source[i - 1] == '@';
                output.Append(c);
                i++;
                while (i < source.Length)
                {
                    if (verbatim)
                    {
                        if (source[i] == '"')
                        {
                            // "" inside a verbatim string is an escaped quote.
                            if (i + 1 < source.Length && source[i + 1] == '"')
                            {
                                output.Append("\"\"");
                                i += 2;
                                continue;
                            }
                            output.Append('"');
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            output.Append(source[i]).Append(source[i + 1]);
                            i += 2;
                            continue;
                        }
                        if (source[i] == c)
                        {
                            output.Append(c);
                            i++;
                            break;
                        }
                        // An unterminated literal at end of line: stop rather than
                        // swallow the rest of the file.
                        if (source[i] == '\n')
                        {
                            break;
                        }
                    }
                    output.Append(source[i]);
                    i++;
                }
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i = Math.Min(i + 2, source.Length);
                // Keep a separator so `a/*x*/b` does not become `ab`.
                output.Append(' ');
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }
}
