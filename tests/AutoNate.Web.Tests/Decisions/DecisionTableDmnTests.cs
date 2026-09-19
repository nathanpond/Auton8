using System.Xml.Linq;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// The generator produces the shape the engine was measured to run (#110).
/// </summary>
/// <remarks>
/// <para>
/// This half runs in slim and pins the structure. The other half,
/// <c>GeneratedDecisionTableTests</c>, deploys that same structure to a live engine
/// and evaluates every rule — because "this is valid XML" and "Flowable will run
/// this" are different claims, and only the engine answers the second.
/// </para>
/// <para>
/// So the assertions here are the ones the engine care about, established in #106
/// by deployment: the namespace, the <c>inputExpression</c> text being the
/// <b>variable name</b> rather than the label, one <c>inputEntry</c> per input
/// column <em>including empty ones</em>, and comparison operators surviving as
/// text rather than breaking the XML.
/// </para>
/// </remarks>
public sealed class DecisionTableDmnTests
{
    private static readonly XNamespace Dmn = "https://www.omg.org/spec/DMN/20191111/MODEL/";

    private static DecisionTableModel Routing() => new()
    {
        Id = Guid.NewGuid(),
        DecisionKey = "routing",
        Name = "Routing",
        HitPolicy = DecisionHitPolicies.First,
        Inputs =
        [
            new DecisionColumn("in_amount", "Amount", "amount", DecisionTypeRefs.Number),
            new DecisionColumn("in_region", "Region", "region", DecisionTypeRefs.String)
        ],
        Outputs = [new DecisionColumn("out_route", "Route", "route", DecisionTypeRefs.String)],
        Rules =
        [
            new DecisionRule("r1", ["> 10000", ""], ["\"escalate\""]),
            new DecisionRule("r2", ["", "\"EU\""], ["\"eu-desk\""])
        ]
    };

    [Fact]
    public void The_generated_document_carries_the_namespace_the_engine_reads()
    {
        var document = XDocument.Parse(DecisionTableDmn.Generate(Routing()));

        // Measured in #106: this exact namespace round-tripped through Flowable
        // 8.0.0. A DMN 1.1 or 1.2 namespace produces a document that parses fine
        // here and deploys into silence.
        Assert.Equal(Dmn + "definitions", document.Root!.Name);
        Assert.Equal("routing", document.Descendants(Dmn + "decision").Single().Attribute("id")!.Value);
    }

    [Fact]
    public void The_input_expression_is_the_VARIABLE_NAME_not_the_label()
    {
        var document = XDocument.Parse(DecisionTableDmn.Generate(Routing()));

        var texts = document.Descendants(Dmn + "inputExpression")
            .Select(e => e.Element(Dmn + "text")!.Value)
            .ToList();

        // This is the join between a table and the process that uses it. Emitting
        // the label instead produces a table that deploys, evaluates, and matches
        // nothing -- because "Amount" is not a variable any process sets.
        Assert.Equal(["amount", "region"], texts);
        Assert.DoesNotContain("Amount", texts);
    }

    [Fact]
    public void Every_rule_carries_one_input_entry_per_column_INCLUDING_the_empty_ones()
    {
        var document = XDocument.Parse(DecisionTableDmn.Generate(Routing()));

        foreach (var rule in document.Descendants(Dmn + "rule"))
        {
            Assert.Equal(2, rule.Elements(Dmn + "inputEntry").Count());
            Assert.Single(rule.Elements(Dmn + "outputEntry"));
        }

        // The complement, and the reason this fact exists. DMN cells are
        // POSITIONAL: omitting an empty entry shifts every later cell onto the
        // wrong column, and the document still parses. Rule 1's second cell is
        // empty and rule 2's first is -- if either were dropped, the table would
        // decide something nobody wrote.
        var first = document.Descendants(Dmn + "rule").First();
        Assert.Equal("> 10000", first.Elements(Dmn + "inputEntry").First().Value);
        Assert.Equal(string.Empty, first.Elements(Dmn + "inputEntry").Last().Value);
    }

    [Fact]
    public void Comparison_operators_survive_as_text_rather_than_breaking_the_document()
    {
        // `>` and `<` are what a numeric rule is made of, so escaping is
        // load-bearing rather than defensive: without it the very first real
        // table produces malformed XML.
        var table = Routing() with
        {
            Rules = [new DecisionRule("r1", ["< 5", "\"A & B\""], ["\"<escaped>\""])]
        };

        var document = XDocument.Parse(DecisionTableDmn.Generate(table));
        var rule = document.Descendants(Dmn + "rule").Single();

        Assert.Equal("< 5", rule.Elements(Dmn + "inputEntry").First().Value);
        Assert.Equal("\"A & B\"", rule.Elements(Dmn + "inputEntry").Last().Value);
        Assert.Equal("\"<escaped>\"", rule.Element(Dmn + "outputEntry")!.Value);
    }

    [Fact]
    public void The_hit_policy_the_author_chose_reaches_the_document()
    {
        var document = XDocument.Parse(
            DecisionTableDmn.Generate(Routing() with { HitPolicy = DecisionHitPolicies.Collect }));

        Assert.Equal("COLLECT",
            document.Descendants(Dmn + "decisionTable").Single().Attribute("hitPolicy")!.Value);
    }

    [Fact]
    public void An_output_carries_its_name_because_that_is_the_variable_the_process_reads()
    {
        var document = XDocument.Parse(DecisionTableDmn.Generate(Routing()));
        var output = document.Descendants(Dmn + "output").Single();

        Assert.Equal("route", output.Attribute("name")!.Value);
        Assert.Equal("string", output.Attribute("typeRef")!.Value);
    }
}
