using AutoNate.Web.Services.Query;
using AutoNate.Web.Tests.Properties.Generators;
using FsCheck;
using FsCheck.Fluent;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Asserts the AQL generator can actually produce every construct the grammar
/// supports.
/// </summary>
/// <remarks>
/// Without this the property suite could be entirely vacuous while staying
/// green: a generator that only ever emitted <c>FROM Record</c> would satisfy
/// round-trip, validity and totality perfectly and test almost nothing. Every
/// property in this namespace is only as strong as what the generator reaches,
/// so what it reaches is asserted rather than assumed.
/// </remarks>
public sealed class AqlGeneratorCoverageTests
{
    // Enough samples that a construct generated with low frequency still shows
    // up reliably; the rarest here is the parameterised FROM at 1-in-5.
    private const int Samples = 3000;

    private static List<AqlQuery> Sample()
    {
        var gen = AqlGenerators.Query();
        return gen.Sample(Samples, 20).ToList();
    }

    [Fact]
    public void The_generator_reaches_every_grammar_construct()
    {
        var queries = Sample();
        var missing = new List<string>();

        void Require(string construct, Func<AqlQuery, bool> predicate)
        {
            if (!queries.Any(predicate)) missing.Add(construct);
        }

        static IEnumerable<AqlWhere> Nodes(AqlWhere? w)
        {
            if (w is null) yield break;
            yield return w;
            if (w is AqlBinary b)
            {
                foreach (var n in Nodes(b.Left)) yield return n;
                foreach (var n in Nodes(b.Right)) yield return n;
            }
        }

        static IEnumerable<AqlValue> Values(AqlWhere? w) => Nodes(w).SelectMany(n => n switch
        {
            AqlCompare c => [c.Value],
            AqlIn i => i.Values,
            AqlBetween b => new[] { b.Lo, b.Hi },
            AqlFunctionCall f => f.Args,
            AqlFunctionCompare f => f.Args.Append(f.Value),
            _ => Array.Empty<AqlValue>(),
        });

        // Clauses
        Require("WHERE absent", q => q.Where is null);
        Require("WHERE present", q => q.Where is not null);
        Require("ORDER BY", q => q.OrderBy.Count > 0);
        Require("ORDER BY DESC", q => q.OrderBy.Any(o => o.Descending));
        Require("ORDER BY ASC", q => q.OrderBy.Any(o => !o.Descending));
        Require("COLUMNS", q => q.Columns is { Count: > 0 });
        Require("COLUMNS with alias", q => q.Columns?.Any(c => c.Alias is not null) == true);
        Require("aggregate column", q => q.Columns?.Any(c => c.IsAggregate) == true);
        Require("aggregate with no argument", q => q.Columns?.Any(c => c.IsAggregate && c.AggregateField is null) == true);
        Require("GROUP", q => q.Group is { Count: > 0 });
        Require("LIMIT", q => q.Limit is not null);
        Require("parameterised FROM", q => q.EntityArgument is not null);
        Require("bare FROM", q => q.EntityArgument is null);

        // Predicate nodes
        Require("AqlBinary", q => Nodes(q.Where).OfType<AqlBinary>().Any());
        Require("AND", q => Nodes(q.Where).OfType<AqlBinary>().Any(b => b.Op == "AND"));
        Require("OR", q => Nodes(q.Where).OfType<AqlBinary>().Any(b => b.Op == "OR"));
        Require("AqlCompare", q => Nodes(q.Where).OfType<AqlCompare>().Any());
        Require("AqlIn", q => Nodes(q.Where).OfType<AqlIn>().Any());
        Require("AqlBetween", q => Nodes(q.Where).OfType<AqlBetween>().Any());
        Require("AqlContains", q => Nodes(q.Where).OfType<AqlContains>().Any());
        Require("AqlFunctionCall", q => Nodes(q.Where).OfType<AqlFunctionCall>().Any());
        Require("AqlFunctionCompare", q => Nodes(q.Where).OfType<AqlFunctionCompare>().Any());
        Require("nested boolean", q => Nodes(q.Where).OfType<AqlBinary>()
            .Any(b => b.Left is AqlBinary || b.Right is AqlBinary));

        // Value nodes
        Require("AqlString", q => Values(q.Where).OfType<AqlString>().Any());
        Require("AqlNumber", q => Values(q.Where).OfType<AqlNumber>().Any());
        Require("AqlBool", q => Values(q.Where).OfType<AqlBool>().Any());
        Require("AqlNull", q => Values(q.Where).OfType<AqlNull>().Any());
        Require("AqlRelativeDate", q => Values(q.Where).OfType<AqlRelativeDate>().Any());
        Require("negative relative date", q => Values(q.Where).OfType<AqlRelativeDate>().Any(d => d.Magnitude < 0));

        // Every comparison operator the grammar defines.
        foreach (var op in new[] { "=", "!=", "<", "<=", ">", ">=" })
        {
            var captured = op;
            Require($"operator {captured}",
                q => Nodes(q.Where).OfType<AqlCompare>().Any(c => c.Op == captured));
        }

        Assert.True(
            missing.Count == 0,
            $"The generator never produced: {string.Join(", ", missing)}. "
            + $"Every property in this namespace is only as strong as what the generator reaches, "
            + $"so an unreachable construct is untested rather than merely uncommon. "
            + $"(Sampled {Samples} queries.)");
    }

    [Fact]
    public void Every_AqlWhere_and_AqlValue_subtype_is_covered_by_the_requirements_above()
    {
        // Guards the guard. If someone adds a node type to AqlAst, the coverage
        // test above keeps passing while silently ignoring it — so the set of
        // node types is pinned here, and adding one fails until the generator,
        // the printer and the coverage list all learn about it.
        var whereTypes = typeof(AqlWhere).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AqlWhere)) && !t.IsAbstract)
            .Select(t => t.Name).OrderBy(n => n).ToArray();

        var valueTypes = typeof(AqlValue).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AqlValue)) && !t.IsAbstract)
            .Select(t => t.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[] { "AqlBetween", "AqlBinary", "AqlCompare", "AqlContains", "AqlFunctionCall", "AqlFunctionCompare", "AqlIn" },
            whereTypes.AsEnumerable());

        Assert.Equal(
            new[] { "AqlBool", "AqlNull", "AqlNumber", "AqlRelativeDate", "AqlString" },
            valueTypes.AsEnumerable());
    }
}
