using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

/// <summary>
/// Publish-time checking for the expressions an author writes in a diagram (#158).
/// </summary>
/// <remarks>
/// <para>
/// A mistyped variable name in a gateway condition fails silently at runtime by
/// taking the wrong branch, which is the hardest workflow bug to diagnose. It gets
/// worse with every new place a condition can be written, and conditions are about
/// to appear in several at once — conditional events here, multi-instance completion
/// conditions (#159), ad-hoc completion conditions (#163).
/// </para>
/// <para>
/// So this is deliberately a **shared** check rather than a rule inside one
/// validator. <see cref="Check"/> is the entry point every condition site calls;
/// reuse is an acceptance criterion, not an optimisation, and
/// <c>WorkflowConditionValidationTests</c> asserts the sites reach this code rather
/// than growing copies.
/// </para>
/// <para>
/// **Errors are for what cannot work; warnings for what is probably wrong.** An
/// expression that does not parse can never evaluate, so it is an error. A reference
/// to a variable the process never sets may still be legitimate — a variable can
/// arrive from outside, through a start payload the diagram does not describe or an
/// API caller — so it is a warning. That asymmetry is the story's decision, and it is
/// why the false-positive guard matters more here than the coverage: a validation
/// that cries wolf gets ignored, and then the real warning is invisible too.
/// </para>
/// </remarks>
public static class WorkflowConditionValidation
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";

    /// <summary>
    /// Identifiers that are language, not variables. Warning on these would fire on
    /// almost every correct condition, which is the fastest way to make the warning
    /// worthless.
    /// </summary>
    private static readonly HashSet<string> NotVariables = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "empty", "and", "or", "not", "div", "mod",
        "execution", "task", "variables", "authenticatedUserId", "this",
        // Flowable exposes these helper beans to expressions.
        "dateUtils", "variableContainer",
    };

    /// <summary>An expression written somewhere in the diagram, with where it came from.</summary>
    public readonly record struct Site(string Element, string Where, string Expression);

    public readonly record struct Result(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Checks every condition in the document — conditional event definitions and
    /// the <c>conditionExpression</c> that sequence flows already carry.
    /// </summary>
    public static Result CheckDocument(XDocument document)
    {
        var declared = CollectAssignedVariableNames(document);
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var site in CollectSites(document))
        {
            var result = Check(site, declared);
            errors.AddRange(result.Errors);
            warnings.AddRange(result.Warnings);
        }

        return new Result(errors, warnings);
    }

    /// <summary>
    /// The single check. Every condition site in the product calls this — sequence
    /// flows, conditional events, and (as they land) multi-instance and ad-hoc
    /// completion conditions.
    /// </summary>
    /// <param name="assigned">
    /// Names the process is known to set. Anything referenced but not in here earns a
    /// warning, never an error.
    /// </param>
    public static Result Check(Site site, IReadOnlySet<string> assigned)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var expression = site.Expression?.Trim() ?? string.Empty;

        if (expression.Length == 0)
        {
            errors.Add($"{site.Where} on '{site.Element}' has no condition. " +
                       "Write an expression such as ${amount > 100}, or remove the condition.");
            return new Result(errors, warnings);
        }

        var syntax = DescribeSyntaxProblem(expression);
        if (syntax is not null)
        {
            // Naming the expression as well as the element matters: an author with
            // forty elements needs to see which text is wrong, not just where.
            errors.Add($"{site.Where} on '{site.Element}' cannot be parsed: {syntax} " +
                       $"The expression is: {expression}");
            return new Result(errors, warnings);
        }

        foreach (var name in ReferencedVariableNames(expression))
        {
            if (assigned.Contains(name)) continue;
            warnings.Add(
                $"{site.Where} on '{site.Element}' reads '{name}', which nothing in this " +
                "process sets. If it arrives from outside — a start payload or an API " +
                "caller — this is fine; if it is a typo, the condition will quietly " +
                "evaluate the wrong way.");
        }

        return new Result(errors, warnings);
    }

    // ── Finding the conditions ──────────────────────────────────────────────

    private static IEnumerable<Site> CollectSites(XDocument document)
    {
        // AC7: the existing sequence flow conditions, so exclusive and inclusive
        // gateways get this without a story of their own.
        foreach (var flow in document.Descendants(Bpmn + "sequenceFlow"))
        {
            var condition = flow.Element(Bpmn + "conditionExpression");
            if (condition is null) continue;
            yield return new Site(LabelOf(flow), "The condition", condition.Value);
        }

        foreach (var definition in document.Descendants(Bpmn + "conditionalEventDefinition"))
        {
            var condition = definition.Element(Bpmn + "condition");
            var owner = definition.Parent;
            yield return new Site(
                owner is null ? LabelOf(definition) : LabelOf(owner),
                "The condition",
                condition?.Value ?? string.Empty);
        }
    }

    /// <summary>
    /// Every variable name the process can be seen to set.
    /// </summary>
    /// <remarks>
    /// Deliberately generous, because every name missed here becomes a false
    /// warning: start-event form fields, script bodies, service task result
    /// variables, data objects, multi-instance element variables, and call activity
    /// output targets all count. The cost of being generous is a missed real warning;
    /// the cost of being strict is a warning nobody reads. AC9 chooses generous.
    /// </remarks>
    public static IReadOnlySet<string> CollectAssignedVariableNames(XDocument document)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in document.Descendants())
        {
            // Service task / business rule task / call activity result targets.
            Add(assigned, element.Attribute(Flowable + "resultVariable")?.Value);
            Add(assigned, element.Attribute("resultVariable")?.Value);
            Add(assigned, element.Attribute(Flowable + "elementVariable")?.Value);
            Add(assigned, element.Attribute(Flowable + "variable")?.Value);
            Add(assigned, element.Attribute(Flowable + "target")?.Value);
            Add(assigned, element.Attribute("target")?.Value);

            var localName = element.Name.LocalName;

            // A data object declares a process variable (#166).
            if (localName is "dataObject" or "dataObjectReference" or "dataStoreReference"
                or "dataInput" or "dataOutput" or "property")
            {
                Add(assigned, element.Attribute("name")?.Value);
                Add(assigned, element.Attribute("id")?.Value);
            }

            // Anything a script assigns. The SPA's extractor uses the same shape;
            // this is the backend's copy because validation runs at /prepare.
            if (localName is "script" or "documentation")
            {
                foreach (Match match in ScriptAssignment.Matches(element.Value))
                {
                    Add(assigned, match.Groups["name"].Value);
                }
            }
        }

        return assigned;
    }

    private static void Add(HashSet<string> into, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (IsIdentifier(trimmed)) into.Add(trimmed);
    }

    // ── Reading the expressions ─────────────────────────────────────────────

    // `execution.setVariable("x", …)` and `variables.set("x", …)`, plus a plain
    // assignment in a script body.
    private static readonly Regex ScriptAssignment = new(
        """(?:setVariable(?:Local)?|variables\s*\.\s*set)\s*\(\s*['"](?<name>[A-Za-z_$][A-Za-z0-9_$]*)['"]""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Identifier = new(
        "[A-Za-z_$][A-Za-z0-9_$]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The names an expression reads. Only the head of a property path counts —
    /// `order.customer.name` reads `order`, and warning about `customer` would be
    /// nonsense.
    /// </summary>
    public static IReadOnlyList<string> ReferencedVariableNames(string expression)
    {
        var body = ExpressionBody(expression);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Strip string literals first, so a variable name mentioned inside a quoted
        // string is not read as a reference.
        body = StringLiteral.Replace(body, " ");

        foreach (Match match in Identifier.Matches(body))
        {
            var name = match.Value;

            // A property or method after a dot is not a variable.
            var before = match.Index - 1;
            while (before >= 0 && char.IsWhiteSpace(body[before])) before--;
            if (before >= 0 && body[before] == '.') continue;

            // A method call is not a variable either.
            var after = match.Index + match.Length;
            while (after < body.Length && char.IsWhiteSpace(body[after])) after++;
            if (after < body.Length && body[after] == '(') continue;

            if (NotVariables.Contains(name)) continue;
            if (seen.Add(name)) names.Add(name);
        }

        return names;
    }

    private static readonly Regex StringLiteral = new(
        "'[^']*'|\"[^\"]*\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The inside of `${…}` or `#{…}`, or the whole string if it is bare.</summary>
    private static string ExpressionBody(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length > 3
            && (trimmed[0] == '$' || trimmed[0] == '#')
            && trimmed[1] == '{'
            && trimmed[^1] == '}')
        {
            return trimmed[2..^1];
        }
        return trimmed;
    }

    /// <summary>
    /// What is wrong with the expression's syntax, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// Not a JUEL parser, and deliberately not: a partial parser that rejects valid
    /// expressions would block publishing work that runs, which is a worse failure
    /// than missing a malformed one. This catches the structural mistakes an author
    /// actually makes — an unclosed delimiter, unbalanced brackets, an unterminated
    /// string, a dangling operator — and defers everything subtler to the engine.
    /// </remarks>
    public static string? DescribeSyntaxProblem(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0) return "it is empty.";

        var opensDelimiter = trimmed.StartsWith("${", StringComparison.Ordinal)
            || trimmed.StartsWith("#{", StringComparison.Ordinal);
        if (opensDelimiter && !trimmed.EndsWith('}'))
        {
            return "it opens with ${ but never closes with }.";
        }
        if (!opensDelimiter && trimmed.EndsWith('}') && trimmed.Contains('{', StringComparison.Ordinal))
        {
            return "it closes with } but does not open with ${.";
        }

        var body = ExpressionBody(trimmed);
        if (body.Trim().Length == 0) return "the expression is empty.";

        var depth = 0;
        char? quote = null;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (quote is not null)
            {
                if (c == quote) quote = null;
                continue;
            }

            switch (c)
            {
                case '\'':
                case '"':
                    quote = c;
                    break;
                case '(':
                case '[':
                    depth++;
                    break;
                case ')':
                case ']':
                    depth--;
                    if (depth < 0) return $"there is a closing '{c}' with nothing to close.";
                    break;
            }
        }

        if (quote is not null) return "a quoted string is never closed.";
        if (depth > 0) return "a bracket is opened and never closed.";

        var last = body.TrimEnd();
        if (last.Length > 0 && "+-*/<>=!&|".Contains(last[^1], StringComparison.Ordinal))
        {
            return $"it ends with '{last[^1]}', so the comparison is unfinished.";
        }

        return null;
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] is '_' or '$')
        && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '$');

    private static string LabelOf(XElement element) =>
        element.Attribute("name")?.Value is { Length: > 0 } name
            ? name
            : element.Attribute("id")?.Value ?? "(unnamed)";
}
