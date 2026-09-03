using System.Globalization;
using System.Text;
using AutoNate.Web.Services.Query;
using FsCheck;
using FsCheck.Fluent;

namespace AutoNate.Web.Tests.Properties.Generators;

/// <summary>
/// Grammar-driven generation for AQL, plus the printer that turns a generated
/// AST back into query text.
/// </summary>
/// <remarks>
/// Generating ASTs and printing them gives both halves of the story for one
/// price: the printed text drives the lexer, and the AST it came from is the
/// oracle the round-trip compares against. <c>AqlAst</c> is a record hierarchy,
/// so structural equality makes <c>parse(print(ast)) == ast</c> a real
/// assertion rather than a field-by-field crawl.
///
/// The printer lives here rather than in the product because nothing in
/// production needs it, and because a printer the parser disagrees with is
/// precisely the bug the round-trip property exists to find — keeping them
/// independently written is what gives the property teeth.
/// </remarks>
internal static class AqlGenerators
{
    // Kept away from the keyword set on purpose: a generator that emitted
    // `FROM WHERE` would be testing the parser's keyword handling by accident
    // and failing for reasons that say nothing about the property under test.
    // Keyword-adjacent identifiers are exercised deliberately instead, by
    // HostileText below.
    private static readonly string[] FieldNames =
        ["name", "status", "amount", "createdAt", "ownerId", "priority", "score"];

    private static readonly string[] EntityNames =
        ["Record", "Note", "Workflow", "Execution", "Page"];

    private static readonly string[] AggregateFns = ["COUNT", "SUM", "AVG", "MIN", "MAX"];

    private static readonly string[] CompareOps = ["=", "!=", "<", "<=", ">", ">="];

    private static readonly char[] RelativeUnits = ['h', 'd', 'w', 'm', 'y'];

    private static Gen<string> Field() => Gen.Elements(FieldNames);

    /// <summary>
    /// Numbers that survive a print/parse cycle exactly.
    /// </summary>
    /// <remarks>
    /// Deliberately not arbitrary doubles. A generator emitting 1e308 or a
    /// value needing 17 significant digits would fail the round-trip on
    /// formatting alone, which says nothing about the parser and buries the
    /// failures that do. The constraint is stated here rather than discovered
    /// inside a shrink. Extreme numeric input is still covered — by the
    /// totality property, where it belongs, via HostileText.
    /// </remarks>
    private static Gen<AqlValue> Number() =>
        Gen.Choose(-100000, 100000).Select(n => (AqlValue)new AqlNumber(n));

    private static Gen<AqlValue> StringValue() =>
        Gen.Elements("open", "closed", "pending", "a", "with space", "O'Brien", "quote\"inside")
            .Select(s => (AqlValue)new AqlString(s));

    private static Gen<AqlValue> Value() =>
        Gen.Frequency(
            (4, StringValue()),
            (4, Number()),
            (2, Gen.Elements(true, false).Select(b => (AqlValue)new AqlBool(b))),
            (1, Gen.Constant((AqlValue)new AqlNull())),
            (2, Gen.Choose(-30, 30)
                .SelectMany(m => Gen.Elements(RelativeUnits)
                    .Select(u => (AqlValue)new AqlRelativeDate(m, u)))));

    private static Gen<AqlWhere> Compare() =>
        Field().SelectMany(f => Gen.Elements(CompareOps)
            .SelectMany(op => Value().Select(v => (AqlWhere)new AqlCompare(f, op, v))));

    private static Gen<AqlWhere> In() =>
        Field().SelectMany(f => Gen.NonEmptyListOf(Value())
            .Select(vs => (AqlWhere)new AqlIn(f, vs.ToList())));

    private static Gen<AqlWhere> Between() =>
        Field().SelectMany(f => Number()
            .SelectMany(lo => Number().Select(hi => (AqlWhere)new AqlBetween(f, lo, hi))));

    private static Gen<AqlWhere> Contains() =>
        Field().SelectMany(f => Gen.Elements("abc", "x", "with space")
            .Select(s => (AqlWhere)new AqlContains(f, s)));

    private static Gen<AqlWhere> FunctionCall() =>
        Gen.Elements("USESNODE", "HASVARIABLE")
            .SelectMany(n => Gen.ListOf(StringValue(), 1)
                .Select(args => (AqlWhere)new AqlFunctionCall(n, args.ToList())));

    private static Gen<AqlWhere> FunctionCompare() =>
        Gen.Elements("NUMNODES", "DURATION")
            .SelectMany(n => Gen.Elements(CompareOps)
                .SelectMany(op => Number()
                    .Select(v => (AqlWhere)new AqlFunctionCompare(n, [], op, v))));

    /// <summary>A WHERE tree, bounded by <paramref name="depth"/>.</summary>
    /// <remarks>
    /// Depth is bounded because the point of the structured properties is
    /// grammar coverage, not stack pressure. Unbounded nesting is exercised by
    /// the totality property, which is where a stack overflow would actually
    /// be a finding.
    /// </remarks>
    private static Gen<AqlWhere> Where(int depth)
    {
        var leaf = Gen.Frequency(
            (5, Compare()),
            (2, In()),
            (2, Between()),
            (2, Contains()),
            (1, FunctionCall()),
            (1, FunctionCompare()));

        if (depth <= 0) return leaf;

        return Gen.Frequency(
            (6, leaf),
            (3, Gen.Elements("AND", "OR")
                .SelectMany(op => Where(depth - 1)
                    .SelectMany(l => Where(depth - 1)
                        .Select(r => (AqlWhere)new AqlBinary(op, l, r))))));
    }

    private static Gen<AqlSelectItem> SelectItem() =>
        Gen.Frequency(
            (3, Field().Select(f => new AqlSelectItem(f, null, null))),
            (2, Gen.Elements(AggregateFns)
                .SelectMany(fn => Gen.Frequency(
                    (1, Gen.Constant<string?>(null)),
                    (3, Field().Select(f => (string?)f)))
                    .Select(arg => new AqlSelectItem(null, fn, arg)))));

    private static Gen<AqlSelectItem> AliasedSelectItem() =>
        SelectItem().SelectMany(item => Gen.Frequency(
            (2, Gen.Constant<string?>(null)),
            (1, Gen.Elements("total", "n", "label").Select(a => (string?)a)))
            .Select(alias => item with { Alias = alias }));

    /// <summary>A syntactically valid query, covering the whole grammar.</summary>
    public static Gen<AqlQuery> Query() =>
        Gen.Frequency(
            (4, Gen.Elements(EntityNames).Select(e => (e, (string?)null))),
            // The parameterized FROM form: FROM Dataset("sales").
            (1, Gen.Elements("sales", "hr", "with space")
                .Select(a => ("Dataset", (string?)a))))
        .SelectMany(entity => OptionalWhere()
            .SelectMany(where => OrderBy()
                .SelectMany(order => Columns()
                    .SelectMany(cols => Group()
                        .SelectMany(group => Limit()
                            .Select(limit => new AqlQuery(
                                entity.Item1, where, order, cols, group, limit, entity.Item2)))))));

    private static Gen<AqlWhere?> OptionalWhere() =>
        Gen.Frequency(
            (1, Gen.Constant<AqlWhere?>(null)),
            (4, Where(2).Select(w => (AqlWhere?)w)));

    private static Gen<IReadOnlyList<AqlOrderItem>> OrderBy() =>
        Gen.Frequency(
            (2, Gen.Constant((IReadOnlyList<AqlOrderItem>)[])),
            (3, Gen.NonEmptyListOf(
                    SelectItem().SelectMany(i => Gen.Elements(true, false)
                        .Select(d => new AqlOrderItem(i, d))))
                .Select(xs => (IReadOnlyList<AqlOrderItem>)xs.ToList())));

    private static Gen<IReadOnlyList<AqlSelectItem>?> Columns() =>
        Gen.Frequency(
            (2, Gen.Constant<IReadOnlyList<AqlSelectItem>?>(null)),
            (3, Gen.NonEmptyListOf(AliasedSelectItem())
                .Select(xs => (IReadOnlyList<AqlSelectItem>?)xs.ToList())));

    private static Gen<IReadOnlyList<string>?> Group() =>
        Gen.Frequency(
            (3, Gen.Constant<IReadOnlyList<string>?>(null)),
            (1, Gen.NonEmptyListOf(Field())
                .Select(xs => (IReadOnlyList<string>?)xs.Distinct().ToList())));

    private static Gen<int?> Limit() =>
        Gen.Frequency(
            (3, Gen.Constant<int?>(null)),
            (1, Gen.Choose(1, 5000).Select(n => (int?)n)));

    /// <summary>
    /// Inputs chosen to break a parser, for the totality property.
    /// </summary>
    /// <remarks>
    /// Uniform random strings essentially never produce an unterminated quote
    /// followed by an escape, or a numeric literal one digit past long.MaxValue.
    /// Those are the inputs that find lexer bugs, so they are generated on
    /// purpose and mixed with genuinely arbitrary text.
    /// </remarks>
    public static Gen<string> HostileText()
    {
        var fragments = Gen.Elements(
            // Unbalanced and escape-terminated quotes.
            "\"", "'", "\"unterminated", "'\\", "\"\\\\\"", "\"a\\",
            // Numeric extremes: one past long.MaxValue, and double overflow.
            "9223372036854775808", "-9223372036854775809", "1e400", "0.0.0", "1.", "-", "+",
            // Structural imbalance.
            "(", ")", "((((", "))))", ",", "()",
            // Keyword prefixes and near-misses — an identifier that starts with
            // a keyword is a classic lexer boundary bug.
            "FROMM", "WHER", "ORDERBY", "AN", "ANDD", "NOTNULL", "LIMITT",
            // Dangling operators and clauses.
            "=", "!=", "<=", "WHERE", "ORDER BY", "LIMIT", "GROUP", "COLUMNS", "AS",
            // Real-ish prefixes so generated text sometimes gets deep into the parser.
            "FROM Record", "FROM Record WHERE", "FROM Record WHERE name", "FROM Record WHERE name =",
            "FROM Record ORDER", "FROM Record COLUMNS(", "FROM Dataset(",
            // Whitespace and control characters.
            " ", "\t", "\n", "\0", " ");

        var assembled = Gen.ListOf(fragments)
            .Select(parts => string.Join(" ", parts));

        return Gen.Frequency(
            // Mostly assembled hostile fragments...
            (6, assembled),
            // ...some deep nesting, for stack behaviour...
            (1, Gen.Choose(50, 400).Select(n =>
                "FROM Record WHERE " + new string('(', n) + "name = 1" + new string(')', n))),
            // ...and some genuinely arbitrary text, so the property is not
            // limited to failures somebody already imagined.
            (3, ArbMap.Default.GeneratorFor<string>().Select(s => s ?? string.Empty)));
    }

    /// <summary>The query generator paired with a shrinker.</summary>
    /// <remarks>
    /// <c>Arb.From(gen)</c> alone gives no shrinking, and an unshrunk
    /// counterexample is close to useless here: the first failure this suite
    /// found was a 250-character query with six columns and two nested IN
    /// lists, when the actual bug was one escaped quote. The shrinker strips
    /// the query down to the smallest thing that still fails.
    ///
    /// Order matters. Whole clauses are dropped first because that removes the
    /// most noise per step, then the WHERE tree is simplified, then individual
    /// values. FsCheck takes the first candidate that still falsifies and
    /// recurses, so cheap, large reductions belong at the front.
    /// </remarks>
    public static Arbitrary<AqlQuery> QueryArb() =>
        Arb.From(Query(), ShrinkQuery);

    private static IEnumerable<AqlQuery> ShrinkQuery(AqlQuery q)
    {
        // Drop optional clauses outright.
        if (q.Limit is not null) yield return q with { Limit = null };
        if (q.Group is not null) yield return q with { Group = null };
        if (q.Columns is not null) yield return q with { Columns = null };
        if (q.OrderBy.Count > 0) yield return q with { OrderBy = [] };
        if (q.EntityArgument is not null) yield return new AqlQuery("Record", q.Where, q.OrderBy, q.Columns, q.Group, q.Limit);
        if (q.Where is not null) yield return q with { Where = null };

        // Halve the lists rather than removing one element at a time: a
        // six-column COLUMNS() takes three steps this way instead of five.
        if (q.Columns is { Count: > 1 })
            yield return q with { Columns = q.Columns.Take(q.Columns.Count / 2).ToList() };
        if (q.OrderBy.Count > 1)
            yield return q with { OrderBy = q.OrderBy.Take(q.OrderBy.Count / 2).ToList() };
        if (q.Group is { Count: > 1 })
            yield return q with { Group = q.Group.Take(q.Group.Count / 2).ToList() };

        // Then simplify the predicate tree.
        if (q.Where is not null)
        {
            foreach (var smaller in ShrinkWhere(q.Where))
            {
                yield return q with { Where = smaller };
            }
        }
    }

    private static IEnumerable<AqlWhere> ShrinkWhere(AqlWhere w)
    {
        switch (w)
        {
            case AqlBinary b:
                // Either side alone is a strictly smaller counterexample, and
                // usually one of them is the culprit.
                yield return b.Left;
                yield return b.Right;
                foreach (var l in ShrinkWhere(b.Left)) yield return b with { Left = l };
                foreach (var r in ShrinkWhere(b.Right)) yield return b with { Right = r };
                break;

            case AqlIn i when i.Values.Count > 1:
                // Both halves, not just the prefix. The first version kept
                // only leading elements and stalled at a six-value list whose
                // culprit was the last one — it shrank to
                // IN(status, 56448, TRUE, TRUE, "pending", "quote\"inside")
                // and stopped. Dropping each element individually finishes the
                // job.
                yield return i with { Values = i.Values.Take(i.Values.Count / 2).ToList() };
                yield return i with { Values = i.Values.Skip(i.Values.Count / 2).ToList() };
                for (var k = 0; k < i.Values.Count; k++)
                {
                    yield return i with { Values = [i.Values[k]] };
                }
                foreach (var v in i.Values.Select((_, k) => k))
                {
                    var without = i.Values.Where((_, k) => k != v).ToList();
                    if (without.Count > 0) yield return i with { Values = without };
                }
                break;

            case AqlCompare c:
                // A simpler value often still triggers the bug, and reads far
                // better in the failure message.
                foreach (var v in ShrinkValue(c.Value)) yield return c with { Value = v };
                break;

            case AqlFunctionCall f when f.Args.Count > 0:
                yield return f with { Args = [] };
                break;

            case AqlFunctionCompare f:
                foreach (var v in ShrinkValue(f.Value)) yield return f with { Value = v };
                break;
        }
    }

    private static IEnumerable<AqlValue> ShrinkValue(AqlValue v)
    {
        switch (v)
        {
            case AqlString s when s.Value.Length > 0:
                // Try the empty string, then progressively shorter prefixes.
                // A string bug usually survives truncation down to the one
                // character that causes it - typically the escape.
                yield return new AqlString(string.Empty);
                if (s.Value.Length > 1) yield return new AqlString(s.Value[..(s.Value.Length / 2)]);
                break;

            case AqlNumber n when n.Value != 0:
                yield return new AqlNumber(0);
                break;

            case AqlRelativeDate d when d.Magnitude != 0:
                yield return new AqlRelativeDate(0, d.Unit);
                break;
        }
    }

    /// <summary>Renders a query back to AQL text.</summary>
    public static string Print(AqlQuery q)
    {
        var sb = new StringBuilder("FROM ").Append(q.Entity);

        if (q.EntityArgument is not null)
        {
            sb.Append('(').Append(Quote(q.EntityArgument)).Append(')');
        }

        if (q.Where is not null) sb.Append(" WHERE ").Append(PrintWhere(q.Where));

        if (q.OrderBy.Count > 0)
        {
            sb.Append(" ORDER BY ").Append(string.Join(", ", q.OrderBy.Select(o =>
                PrintSelectItem(o.Item) + (o.Descending ? " DESC" : " ASC"))));
        }

        if (q.Columns is not null)
        {
            sb.Append(" COLUMNS(")
              .Append(string.Join(", ", q.Columns.Select(PrintSelectItemWithAlias)))
              .Append(')');
        }

        if (q.Group is not null)
        {
            sb.Append(" GROUP(").Append(string.Join(", ", q.Group)).Append(')');
        }

        if (q.Limit is not null) sb.Append(" LIMIT ").Append(q.Limit.Value);

        return sb.ToString();
    }

    private static string PrintSelectItem(AqlSelectItem item) =>
        item.IsAggregate
            ? $"{item.AggregateFn}({item.AggregateField ?? string.Empty})"
            : item.Field!;

    private static string PrintSelectItemWithAlias(AqlSelectItem item) =>
        PrintSelectItem(item) + (item.Alias is null ? string.Empty : $" AS {item.Alias}");

    // Every binary node is parenthesised. Precedence-correct printing would be
    // a second implementation of the parser's precedence rules, and a
    // round-trip that only passes because both sides share a bug proves
    // nothing. Explicit parentheses keep the printer honest and dumb.
    private static string PrintWhere(AqlWhere w) => w switch
    {
        AqlBinary b => $"({PrintWhere(b.Left)} {b.Op} {PrintWhere(b.Right)})",
        AqlCompare c => $"{c.Field} {c.Op} {PrintValue(c.Value)}",
        // Function form for all three. The parser also accepts the infix
        // `field IN (...)` spelling, but CONTAINS and BETWEEN exist only as
        // functions — the first printer here invented an infix `field CONTAINS
        // "x"` and the property caught it on the fifth generated case, which is
        // exactly the disagreement the round-trip is for. Note the field is a
        // bare identifier, not a quoted string, even though the parser stores
        // it as an AqlString internally.
        AqlIn i => $"IN({i.Field}, {string.Join(", ", i.Values.Select(PrintValue))})",
        AqlBetween b => $"BETWEEN({b.Field}, {PrintValue(b.Lo)}, {PrintValue(b.Hi)})",
        AqlContains c => $"CONTAINS({c.Field}, {Quote(c.Substr)})",
        AqlFunctionCall f => $"{f.Name}({string.Join(", ", f.Args.Select(PrintValue))})",
        AqlFunctionCompare f =>
            $"{f.FnName}({string.Join(", ", f.Args.Select(PrintValue))}) {f.Op} {PrintValue(f.Value)}",
        _ => throw new InvalidOperationException(
            $"The printer does not handle {w.GetType().Name}. A new AqlWhere node was added "
            + "without teaching the generator about it, which would silently drop it from every "
            + "property in this suite.")
    };

    private static string PrintValue(AqlValue v) => v switch
    {
        AqlString s => Quote(s.Value),
        AqlNumber n => n.Value.ToString("R", CultureInfo.InvariantCulture),
        AqlBool b => b.Value ? "TRUE" : "FALSE",
        AqlNull => "NULL",
        AqlRelativeDate d => $"{(d.Magnitude >= 0 ? "+" : "-")}{Math.Abs(d.Magnitude)}{d.Unit}",
        _ => throw new InvalidOperationException(
            $"The printer does not handle {v.GetType().Name}. See PrintWhere for why that matters.")
    };

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
