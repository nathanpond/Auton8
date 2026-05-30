using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoNate.Web.Services.Query;

// Phase 3 of the Data Stores plan: substitute `:paramName` placeholders
// in a parsed AqlQuery with caller-supplied values, returning a new AST
// the validator runs over. Binding (typed AST nodes), not interpolation
// (string concatenation). Placeholders appear inside string literals so
// the v2 grammar doesn't need to learn a new token kind: the writer
// authors `Name = ":customerName"` and the binder swaps the string node
// for AqlString/AqlNumber/AqlBool based on the bound value.
//
// If a placeholder is referenced but not supplied, the binder throws
// AqlParameterBindingException; the endpoint maps that to 400. Supplied
// parameter names that aren't referenced are silently ignored (so a shared
// query URL that adds optional params next week doesn't break old callers).
public static class AqlParameterBinder
{
    private static readonly Regex PlaceholderPattern =
        new("^:(?<name>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    public static AqlQuery Bind(
        AqlQuery query,
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            // No bindings supplied — still scan and reject if the query
            // contains an unbound placeholder so the caller sees a clean
            // error rather than running a literal `:foo` value against the
            // source.
            EnsureNoUnboundPlaceholders(query);
            return query;
        }

        // Case-insensitive lookup so `:CustomerName` matches `customerName`.
        var lookup = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        var rewritten = query with
        {
            Where = query.Where is null ? null : RewriteWhere(query.Where, lookup),
        };
        EnsureNoUnboundPlaceholders(rewritten);
        return rewritten;
    }

    private static AqlWhere RewriteWhere(AqlWhere where, IReadOnlyDictionary<string, string> bindings)
    {
        return where switch
        {
            AqlBinary b => b with
            {
                Left = RewriteWhere(b.Left, bindings),
                Right = RewriteWhere(b.Right, bindings),
            },
            AqlCompare c => c with { Value = RewriteValue(c.Value, bindings) },
            AqlIn i => i with { Values = i.Values.Select(v => RewriteValue(v, bindings)).ToList() },
            AqlBetween bt => bt with
            {
                Lo = RewriteValue(bt.Lo, bindings),
                Hi = RewriteValue(bt.Hi, bindings),
            },
            AqlFunctionCompare fc => fc with
            {
                Args = fc.Args.Select(v => RewriteValue(v, bindings)).ToList(),
                Value = RewriteValue(fc.Value, bindings),
            },
            AqlFunctionCall fn => fn with
            {
                Args = fn.Args.Select(v => RewriteValue(v, bindings)).ToList(),
            },
            // AqlContains has a string Substr, not an AqlValue — rebind only
            // when the substring is a placeholder by itself.
            AqlContains ct => ct with { Substr = RewriteSubstring(ct.Substr, bindings) },
            _ => where,
        };
    }

    private static AqlValue RewriteValue(AqlValue value, IReadOnlyDictionary<string, string> bindings)
    {
        if (value is AqlString s && TryGetPlaceholderName(s.Value, out var name))
        {
            if (!bindings.TryGetValue(name, out var bound))
            {
                throw new AqlParameterBindingException(
                    $"Query parameter ':{name}' was referenced but not supplied.");
            }
            return CoerceToAqlValue(bound);
        }
        return value;
    }

    private static string RewriteSubstring(string substr, IReadOnlyDictionary<string, string> bindings)
    {
        if (!TryGetPlaceholderName(substr, out var name)) return substr;
        if (!bindings.TryGetValue(name, out var bound))
        {
            throw new AqlParameterBindingException(
                $"Query parameter ':{name}' was referenced but not supplied.");
        }
        return bound;
    }

    private static void EnsureNoUnboundPlaceholders(AqlQuery query)
    {
        if (query.Where is null) return;
        Walk(query.Where);

        static void Walk(AqlWhere w)
        {
            switch (w)
            {
                case AqlBinary b: Walk(b.Left); Walk(b.Right); break;
                case AqlCompare c: Check(c.Value); break;
                case AqlIn i:
                    foreach (var v in i.Values) { Check(v); }
                    break;
                case AqlBetween bt: Check(bt.Lo); Check(bt.Hi); break;
                case AqlFunctionCompare fc:
                    foreach (var a in fc.Args) { Check(a); }
                    Check(fc.Value); break;
                case AqlFunctionCall fn:
                    foreach (var a in fn.Args) { Check(a); }
                    break;
                case AqlContains ct:
                    if (TryGetPlaceholderName(ct.Substr, out var n))
                        throw new AqlParameterBindingException(
                            $"Query parameter ':{n}' was referenced but not supplied.");
                    break;
            }
        }

        static void Check(AqlValue v)
        {
            if (v is AqlString s && TryGetPlaceholderName(s.Value, out var n))
                throw new AqlParameterBindingException(
                    $"Query parameter ':{n}' was referenced but not supplied.");
        }
    }

    private static bool TryGetPlaceholderName(string literal, out string name)
    {
        var match = PlaceholderPattern.Match(literal ?? string.Empty);
        if (match.Success)
        {
            name = match.Groups["name"].Value;
            return true;
        }
        name = string.Empty;
        return false;
    }

    private static AqlValue CoerceToAqlValue(string raw)
    {
        // Boolean fast path so `:flag=true` binds the way the writer expects.
        if (bool.TryParse(raw, out var b)) return new AqlBool(b);
        // Numeric: integer or double, invariant culture to avoid `,` traps.
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return new AqlNumber(l);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new AqlNumber(d);
        return new AqlString(raw);
    }
}

public sealed class AqlParameterBindingException(string message) : Exception(message);
