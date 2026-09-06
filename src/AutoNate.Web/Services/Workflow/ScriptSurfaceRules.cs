using System.Text;
using System.Text.RegularExpressions;

namespace AutoNate.Web.Services.Workflow;

// The one place that says what a BPMN script task may use, and what it may not
// (#151).
//
// #147 moved script execution out of the Flowable JVM and into the executor
// sandbox. Two classes of script stopped working as a result, and both would
// otherwise fail at *runtime*, on whoever happened to run the process, with an
// error that says nothing about how to fix it:
//
//   * scripts written against the old `execution` binding, which no longer
//     exists — the sandbox binds `variables` instead;
//   * scripts reaching for the JVM, which is the vulnerability #147 closed.
//
// Rejecting them at publish turns that into a message the author can act on
// while they still have the editor open.
//
// The list lives here rather than inline in the validator so that the docs and
// the check cannot drift: docs/DEPLOYMENT.md describes the supported surface,
// and these are its complement.
public static class ScriptSurfaceRules
{
    /// <summary>
    /// The `scriptFormat` values a script task may declare (#154).
    /// </summary>
    /// <remarks>
    /// A list rather than a single value: both languages are front-ends onto
    /// the same host surface, executed by the same sandbox. Adding one should
    /// be an entry here, not a new execution path — that is the property the
    /// executor's parity suite holds us to.
    /// </remarks>
    public static readonly IReadOnlyList<string> SupportedScriptFormats = ["javascript", "python"];

    /// <summary>
    /// Translates a BPMN `scriptFormat` into the executor's runner name.
    /// </summary>
    /// <remarks>
    /// BPMN says "javascript"; the executor's wire format says "js". The two
    /// vocabularies meet here and nowhere else, so a third caller cannot invent
    /// a third spelling.
    /// </remarks>
    public static string ToExecutorLanguage(string? scriptFormat) =>
        string.Equals(scriptFormat, "python", StringComparison.OrdinalIgnoreCase) ? "python" : "js";

    public static bool IsSupportedScriptFormat(string? scriptFormat) =>
        scriptFormat is not null
        && SupportedScriptFormats.Contains(scriptFormat, StringComparer.OrdinalIgnoreCase);

    /// <param name="Identifier">
    /// The bare name a sandbox refusal reports. When the sandbox rejects a
    /// script it does so as an ordinary <c>ReferenceError</c> — "Java is not
    /// defined" — which is indistinguishable from a typo unless something
    /// knows the name was withheld deliberately. This is what knows.
    /// Null where the shape has no single identifier to key on.
    /// </param>
    public sealed record RejectedShape(Regex Pattern, string Explanation, string? Identifier = null);

    // Ordered: the first match wins for a given script, so the specific
    // `execution.setVariable` message is reached before the general
    // `execution` one.
    public static readonly IReadOnlyList<RejectedShape> Rejected =
    [
        new(new Regex(@"\bexecution\s*\.\s*setVariable\b", RegexOptions.Compiled),
            "`execution.setVariable(name, value)` is no longer available; use `variables.set(name, value)`."),
        new(new Regex(@"\bexecution\s*\.\s*getVariable(Local)?\b", RegexOptions.Compiled),
            "`execution.getVariable(name)` is no longer available; use `variables.get(name)`."),
        new(new Regex(@"\bexecution\s*\.\s*removeVariable\b", RegexOptions.Compiled),
            "`execution.removeVariable(name)` is no longer available; set the variable to null with `variables.set(name, null)`."),
        new(new Regex(@"\bexecution\b", RegexOptions.Compiled),
            "`execution` is not bound in the script sandbox; process variables are available through `variables.get(name)` and `variables.set(name, value)`.",
            Identifier: "execution"),

        // Java interop. These would fail in the sandbox as an unresolved
        // identifier, which is correct but late — and the reason matters more
        // than the symptom, because an author reading "Java is not defined"
        // may reasonably think it is a missing dependency.
        new(new Regex(@"\bJava\s*\.\s*type\b", RegexOptions.Compiled),
            "`Java.type(...)` is not available: scripts run in a sandbox with no access to the host JVM.",
            Identifier: "Java"),
        new(new Regex(@"\bJavaImporter\b", RegexOptions.Compiled),
            "`JavaImporter` is not available: scripts run in a sandbox with no access to the host JVM.",
            Identifier: "JavaImporter"),
        new(new Regex(@"\bPackages\s*\.", RegexOptions.Compiled),
            "`Packages.*` is not available: scripts run in a sandbox with no access to the host JVM.",
            Identifier: "Packages"),
        new(new Regex(@"\bjava\s*\.\s*lang\b", RegexOptions.Compiled),
            "`java.lang.*` is not available: scripts run in a sandbox with no access to the host JVM."),
    ];

    /// <summary>
    /// Finds rejected shapes in a script body, ignoring comments and string
    /// literals.
    /// </summary>
    /// <remarks>
    /// Deliberately static: validation runs at publish, and running
    /// author-supplied code at publish is precisely what must not happen.
    ///
    /// Where a shape cannot be distinguished reliably this errs toward missing
    /// it. A missed script fails at runtime with a clear sandbox error; a
    /// wrongly blocked script leaves an author stuck with no recourse.
    /// </remarks>
    public static IReadOnlyList<string> FindRejected(string? scriptBody)
    {
        if (string.IsNullOrWhiteSpace(scriptBody)) return [];

        var code = StripCommentsAndStrings(scriptBody);
        var found = new List<string>();
        foreach (var shape in Rejected)
        {
            if (shape.Pattern.IsMatch(code))
            {
                found.Add(shape.Explanation);
                // One message per script is enough to act on, and the ordering
                // above means it is the most specific one.
                break;
            }
        }
        return found;
    }

    /// <summary>
    /// Blanks out comments and string literals so a mention of a rejected name
    /// inside one does not read as a use of it.
    /// </summary>
    /// <remarks>
    /// This is what keeps a comment reading "replaces execution.setVariable"
    /// from blocking a publish — the case a plain substring check gets wrong.
    ///
    /// It is a scanner, not a parser. The one construct it cannot separate from
    /// division is a regex literal, so `/execution/.test(x)` is left as code and
    /// would be flagged. That is a deliberate trade: handling it needs the
    /// preceding-token context a real lexer carries, and the alternative
    /// mistake — treating a division as the start of a literal — would blank
    /// out real code and cause the false *negatives* this is not allowed to
    /// hide behind.
    /// </remarks>
    internal static string StripCommentsAndStrings(string source)
    {
        var output = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] is not ('\n' or '\r')) i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i = Math.Min(i + 2, source.Length);
                // Keep a separator so `a/*x*/b` does not become the identifier `ab`.
                output.Append(' ');
                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                var quote = c;
                i++;
                while (i < source.Length && source[i] != quote)
                {
                    // A backslash escapes the next character, including the
                    // quote — without this, "it\"s" would end the literal early
                    // and the rest of the line would be scanned as code.
                    if (source[i] == '\\') i++;
                    i++;
                }
                i++;
                output.Append(' ');
                continue;
            }

            output.Append(c);
            i++;
        }
        return output.ToString();
    }

    /// <summary>
    /// Decides whether a sandbox error is the boundary refusing something, as
    /// opposed to an ordinary mistake in the author's script.
    /// </summary>
    /// <remarks>
    /// The sandbox does not announce refusals. It withholds the binding, so
    /// reaching for one produces a plain <c>ReferenceError: Java is not
    /// defined</c> — the same shape a misspelled variable produces. To an
    /// author that reads like a missing dependency rather than a deliberate
    /// boundary, and the test-run panel is exactly where they should learn the
    /// difference (#152).
    ///
    /// Keyed off the same list that drives publish-time rejection, so the two
    /// cannot disagree about what is out of bounds.
    /// </remarks>
    public static string? TryExplainRefusal(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage)) return null;

        foreach (var shape in Rejected)
        {
            if (shape.Identifier is null) continue;
            // The engine's wording is "<name> is not defined". Requiring both
            // the name and that phrasing keeps a script that merely mentions
            // the name in its own thrown message from being misreported as a
            // refusal.
            if (errorMessage.Contains(shape.Identifier, StringComparison.Ordinal)
                && errorMessage.Contains("is not defined", StringComparison.OrdinalIgnoreCase))
            {
                return shape.Explanation;
            }
        }
        return null;
    }
}
