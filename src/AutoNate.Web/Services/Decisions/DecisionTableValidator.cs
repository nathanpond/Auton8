using System.Globalization;
using System.Text.RegularExpressions;
using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Decisions;

/// <summary>
/// Refuses a decision table at SAVE, naming the cell (#110).
/// </summary>
/// <remarks>
/// <para>
/// The AC's words: <i>"a rule whose cells do not satisfy them is rejected at save
/// with a message naming the cell — not at execution time, where the failure lands
/// on whoever ran the process"</i>.
/// </para>
/// <para>
/// This is only possible because the rules are stored as structured data. A table
/// stored as hand-edited DMN could not be checked cell by cell, and the first
/// anyone would hear of a bad cell is a process taking a branch nobody wrote.
/// </para>
/// <para>
/// <b>What it does NOT check, deliberately:</b> whether a FEEL expression is
/// semantically sensible. <c>&gt; 10</c> on a number column is accepted;
/// <c>&gt; "banana"</c> on a number column is refused because the literal cannot be
/// a number, but an expression that parses and means something silly is the
/// author's business. The line is "the engine could not use this", not "I would
/// have written it differently".
/// </para>
/// </remarks>
public static class DecisionTableValidator
{
    /// <summary>A DMN identifier: what the engine will accept as a decision key.</summary>
    private static readonly Regex KeyPattern = new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(DecisionTableModel table)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(table.Name))
        {
            errors.Add("The decision table needs a name.");
        }

        if (string.IsNullOrWhiteSpace(table.DecisionKey))
        {
            errors.Add("The decision table needs a key. A business rule task references it by this.");
        }
        else if (!KeyPattern.IsMatch(table.DecisionKey))
        {
            errors.Add(
                $"'{table.DecisionKey}' is not a usable decision key. Use a letter followed by "
                + "letters, digits or underscores — the engine reads it as a DMN identifier.");
        }

        if (!DecisionHitPolicies.All.Contains(table.HitPolicy, StringComparer.Ordinal))
        {
            errors.Add(
                $"'{table.HitPolicy}' is not a hit policy this editor supports. Choose one of: "
                + string.Join(", ", DecisionHitPolicies.All) + ".");
        }

        if (table.Inputs.Count == 0)
        {
            errors.Add("A decision table needs at least one input — otherwise every rule matches.");
        }

        if (table.Outputs.Count == 0)
        {
            errors.Add("A decision table needs at least one output — otherwise it decides nothing.");
        }

        errors.AddRange(ColumnErrors(table.Inputs, "Input"));
        errors.AddRange(ColumnErrors(table.Outputs, "Output"));

        // Names are the join to process variables, so a duplicate is not cosmetic:
        // one column would silently win.
        errors.AddRange(DuplicateNameErrors(table.Inputs, "Input"));
        errors.AddRange(DuplicateNameErrors(table.Outputs, "Output"));

        for (var r = 0; r < table.Rules.Count; r++)
        {
            errors.AddRange(RuleErrors(table, table.Rules[r], r + 1));
        }

        return errors;
    }

    private static IEnumerable<string> ColumnErrors(IReadOnlyList<DecisionColumn> columns, string kind)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var where = string.IsNullOrWhiteSpace(column.Label)
                ? $"{kind} {i + 1}"
                : $"{kind} '{column.Label}'";

            if (string.IsNullOrWhiteSpace(column.Name))
            {
                yield return $"{where} has no variable name. That is how a process passes a value in "
                    + "or reads one back, so it cannot be left blank.";
            }
            else if (!KeyPattern.IsMatch(column.Name))
            {
                yield return $"{where}'s variable name '{column.Name}' is not usable. Use a letter "
                    + "followed by letters, digits or underscores.";
            }

            if (!DecisionTypeRefs.All.Contains(column.TypeRef, StringComparer.Ordinal))
            {
                yield return $"{where} has type '{column.TypeRef}', which the engine does not read. "
                    + "Use one of: " + string.Join(", ", DecisionTypeRefs.All) + ".";
            }
        }
    }

    private static IEnumerable<string> DuplicateNameErrors(
        IReadOnlyList<DecisionColumn> columns, string kind)
    {
        foreach (var group in columns
                     .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .GroupBy(c => c.Name, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            yield return $"Two {kind.ToLowerInvariant()} columns both use the variable name "
                + $"'{group.Key}'. One would silently win; rename one.";
        }
    }

    private static IEnumerable<string> RuleErrors(DecisionTableModel table, DecisionRule rule, int position)
    {
        // Ragged rules first: every cell check below indexes into these, and a
        // count mismatch would otherwise surface as a confusing per-cell error.
        if (rule.InputEntries.Count != table.Inputs.Count)
        {
            yield return $"Rule {position} has {rule.InputEntries.Count} input cells but the table has "
                + $"{table.Inputs.Count} input columns.";
            yield break;
        }

        if (rule.OutputEntries.Count != table.Outputs.Count)
        {
            yield return $"Rule {position} has {rule.OutputEntries.Count} output cells but the table has "
                + $"{table.Outputs.Count} output columns.";
            yield break;
        }

        for (var i = 0; i < rule.InputEntries.Count; i++)
        {
            var problem = InputCellProblem(rule.InputEntries[i], table.Inputs[i].TypeRef);
            if (problem is not null)
            {
                yield return $"Rule {position}, input '{table.Inputs[i].Label}': {problem}";
            }
        }

        for (var i = 0; i < rule.OutputEntries.Count; i++)
        {
            var problem = OutputCellProblem(rule.OutputEntries[i], table.Outputs[i].TypeRef);
            if (problem is not null)
            {
                yield return $"Rule {position}, output '{table.Outputs[i].Label}': {problem}";
            }
        }
    }

    /// <summary>
    /// An input cell is a FEEL <i>unary test</i>, not a value.
    /// </summary>
    /// <remarks>
    /// Empty means "any", which is legitimate and common — a rule that cares about
    /// one column and not another. So blank is never an error here, which is the
    /// opposite of the output rule below.
    /// </remarks>
    private static string? InputCellProblem(string cell, string typeRef)
    {
        var trimmed = cell.Trim();
        if (trimmed.Length == 0 || trimmed == "-") return null;

        // A comparison or a range is a test, not a literal, so the literal check
        // below would reject it. Strip the operator and check what is compared.
        var comparison = Regex.Match(trimmed, @"^(<=|>=|<|>|!=|=)\s*(.+)$");
        if (comparison.Success)
        {
            var operand = comparison.Groups[2].Value.Trim();
            return LiteralProblem(operand, typeRef, "a comparison is compared against");
        }

        // A range: [1..10] or (1..10]. Both ends must fit the column's type.
        var range = Regex.Match(trimmed, @"^[\[\(]\s*(.+?)\s*\.\.\s*(.+?)\s*[\]\)]$");
        if (range.Success)
        {
            return LiteralProblem(range.Groups[1].Value, typeRef, "a range starts at")
                ?? LiteralProblem(range.Groups[2].Value, typeRef, "a range ends at");
        }

        // A comma-separated list means "any of these".
        if (trimmed.Contains(',', StringComparison.Ordinal))
        {
            foreach (var part in trimmed.Split(','))
            {
                var problem = LiteralProblem(part.Trim(), typeRef, "a list contains");
                if (problem is not null) return problem;
            }

            return null;
        }

        return LiteralProblem(trimmed, typeRef, "the cell holds");
    }

    /// <summary>
    /// An output cell is a value, and blank is NOT legitimate.
    /// </summary>
    /// <remarks>
    /// A rule with a blank output matches and then decides nothing — a process
    /// reading that variable gets null and usually takes the branch nobody meant.
    /// That is precisely the silent failure this validator exists for.
    /// </remarks>
    private static string? OutputCellProblem(string cell, string typeRef)
    {
        var trimmed = cell.Trim();
        if (trimmed.Length == 0)
        {
            return "no value. A rule that matches and outputs nothing sets the variable to null, "
                + "which a following gateway will read as a decision nobody wrote.";
        }

        return LiteralProblem(trimmed, typeRef, "the cell holds");
    }

    private static string? LiteralProblem(string literal, string typeRef, string lead)
    {
        if (literal.Length == 0) return null;

        // A FEEL expression -- a function call, a variable reference, arithmetic --
        // is the author's business and cannot be type-checked here. Recognised by
        // what it is not: a bare literal.
        if (literal.Contains('(', StringComparison.Ordinal)
            || literal.Contains('+', StringComparison.Ordinal)
            || literal.Contains('*', StringComparison.Ordinal))
        {
            return null;
        }

        switch (typeRef)
        {
            case DecisionTypeRefs.Number:
                if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    return $"{lead} {Describe(literal)}, but the column is a number.";
                }

                return null;

            case DecisionTypeRefs.Boolean:
                if (!string.Equals(literal, "true", StringComparison.Ordinal)
                    && !string.Equals(literal, "false", StringComparison.Ordinal))
                {
                    return $"{lead} {Describe(literal)}, but the column is true/false.";
                }

                return null;

            case DecisionTypeRefs.String:
                // FEEL string literals are quoted. An unquoted word is a variable
                // reference, which resolves to null at run time and matches nothing
                // -- the classic silent no-match.
                if (!(literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"'))
                {
                    return $"{lead} {Describe(literal)} without quotes, so the engine reads it as a "
                        + $"variable name rather than the text {literal}. Write \"{literal}\".";
                }

                return null;

            case DecisionTypeRefs.Date:
                if (!literal.StartsWith("date(", StringComparison.Ordinal)
                    && !DateTime.TryParse(literal, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out _))
                {
                    return $"{lead} {Describe(literal)}, but the column is a date. "
                        + "Use an ISO date, or date(\"2026-01-31\").";
                }

                return null;

            default:
                return null;
        }
    }

    private static string Describe(string literal) => $"'{literal}'";
}
