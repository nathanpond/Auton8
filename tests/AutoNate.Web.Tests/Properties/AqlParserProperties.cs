using System.Diagnostics;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Tests.Properties.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Property-based tests for the AQL parser.
/// </summary>
/// <remarks>
/// The parser turns untrusted text into queries. Example-based tests cover the
/// inputs somebody thought of; a parser's interesting inputs are the ones
/// nobody did. These state what must hold for <em>every</em> input and check it
/// against generated ones.
///
/// The seed is fixed so a CI failure is reproducible from the log rather than
/// only on the machine that saw it. FsCheck requires the gamma half of a
/// replay seed to be odd and throws <c>ArgumentException: Gamma must be odd</c>
/// at collection time otherwise — which aborts the whole test host, not just
/// the property, so it is worth knowing before changing these numbers.
/// </remarks>
public sealed class AqlParserProperties
{
    // Enough runs to explore, few enough to stay inside a CI shard that now
    // finishes in ~4 minutes. Raise MaxTest locally when hunting something.
    private const int Runs = 500;

    /// <summary>
    /// The property that matters most: <c>Parse</c> either returns an AST or
    /// throws <see cref="AqlValidationException"/>. Nothing else, ever.
    /// </summary>
    /// <remarks>
    /// An unexpected exception type reaching an endpoint is a 500 where a 400
    /// was owed. QueryEndpoints does have a catch-all, so today the user sees a
    /// generic failure rather than a crash — but a generic failure is still the
    /// wrong answer for malformed input, and it arrives with an ERROR log and a
    /// failed audit event attached.
    /// </remarks>
    [Property(MaxTest = Runs, Replay = "(1234567890,9876543211)")]
    public Property Parse_either_returns_an_ast_or_throws_AqlValidationException()
    {
        return Prop.ForAll(Arb.From(AqlGenerators.HostileText()), source =>
        {
            try
            {
                var query = AqlParser.Parse(source);
                return query is not null;
            }
            catch (AqlValidationException)
            {
                return true;
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Parse threw {ex.GetType().Name} instead of AqlValidationException.\n"
                    + $"Input ({source.Length} chars): {Truncate(source)}\n"
                    + $"Message: {ex.Message}");
            }
        });
    }

    /// <summary>Parsing terminates, so a pathological input cannot hang a request thread.</summary>
    /// <remarks>
    /// A generous ceiling on purpose. This is a guard against non-termination
    /// and catastrophic backtracking, not a performance budget — a tight bound
    /// here would fail on a loaded CI runner and teach everyone to ignore it.
    /// </remarks>
    [Property(MaxTest = 200, Replay = "(1234567890,9876543211)")]
    public Property Parsing_terminates_within_a_bounded_time()
    {
        return Prop.ForAll(Arb.From(AqlGenerators.HostileText()), source =>
        {
            var stopwatch = Stopwatch.StartNew();
            try { AqlParser.Parse(source); }
            catch (AqlValidationException) { }
            catch (Exception) { /* totality is the other property's job */ }
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds >= 2000)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Parse took {stopwatch.ElapsedMilliseconds}ms on a {source.Length}-char input: "
                    + Truncate(source));
            }

            return true;
        });
    }

    /// <summary>
    /// Every query the grammar says is valid is accepted.
    /// </summary>
    /// <remarks>
    /// A parser that rejects its own grammar is the bug random strings will
    /// never find, because a random string is essentially never a valid query.
    /// </remarks>
    [Property(MaxTest = Runs, Replay = "(1234567890,9876543211)")]
    public Property Every_generated_valid_query_parses()
    {
        return Prop.ForAll(AqlGenerators.QueryArb(), ast =>
        {
            var text = AqlGenerators.Print(ast);
            try
            {
                return AqlParser.Parse(text) is not null;
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"A query the grammar allows was rejected.\nText: {text}\n"
                    + $"{ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>Printing a parsed AST and re-parsing yields an equal AST.</summary>
    /// <remarks>
    /// The printer lives in the test project (see AqlGenerators) because none
    /// exists in the product. Written independently of the parser and
    /// deliberately dumb — every binary node is fully parenthesised — so the
    /// two cannot agree by sharing a mistake.
    /// </remarks>
    [Property(MaxTest = Runs, Replay = "(1234567890,9876543211)")]
    public Property Printing_and_reparsing_round_trips()
    {
        return Prop.ForAll(AqlGenerators.QueryArb(), ast =>
        {
            var text = AqlGenerators.Print(ast);
            AqlQuery reparsed;
            try
            {
                reparsed = AqlParser.Parse(text);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Round-trip failed at the re-parse.\nText: {text}\n{ex.GetType().Name}: {ex.Message}");
            }

            // Compared as printed text rather than as records: the AST holds
            // doubles, and comparing renderings avoids a spurious failure from
            // floating-point equality while still catching every structural
            // difference.
            var again = AqlGenerators.Print(reparsed);
            if (again != text)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Round-trip changed the query.\n  printed: {text}\n  reparsed: {again}");
            }

            return true;
        });
    }

    private static string Truncate(string s) =>
        s.Length <= 200 ? s : s[..200] + $"... (+{s.Length - 200} more)";
}
