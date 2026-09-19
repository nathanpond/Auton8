using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// A bad cell is refused where the author is, not where the process runs (#110).
/// </summary>
/// <remarks>
/// <para>
/// The AC's phrasing is the whole point: <i>"rejected at save with a message naming
/// the cell — not at execution time, where the failure lands on whoever ran the
/// process"</i>. So every assertion here checks the <b>message</b>, not just that
/// something was refused: "validation failed" on a forty-rule table tells an author
/// nothing.
/// </para>
/// <para>
/// The negative cases carry as much weight as the positive ones. A validator that
/// refused everything would satisfy every "is rejected" test and be useless, and the
/// expressions it must <em>not</em> refuse — an empty input cell, a range, a
/// comparison, a FEEL function call — are exactly what a real table is made of.
/// </para>
/// </remarks>
public sealed class DecisionTableValidatorTests
{
    private static DecisionTableModel Table(
        IReadOnlyList<DecisionRule>? rules = null,
        IReadOnlyList<DecisionColumn>? inputs = null,
        IReadOnlyList<DecisionColumn>? outputs = null,
        string hitPolicy = DecisionHitPolicies.First,
        string key = "routing") => new()
    {
        Id = Guid.NewGuid(),
        DecisionKey = key,
        Name = "Routing",
        HitPolicy = hitPolicy,
        Inputs = inputs ??
        [
            new DecisionColumn("in_amount", "Amount", "amount", DecisionTypeRefs.Number),
            new DecisionColumn("in_region", "Region", "region", DecisionTypeRefs.String)
        ],
        Outputs = outputs ??
        [
            new DecisionColumn("out_route", "Route", "route", DecisionTypeRefs.String)
        ],
        Rules = rules ?? [new DecisionRule("r1", ["> 10000", ""], ["\"escalate\""])]
    };

    // ── the table that must pass ────────────────────────────────────────────

    [Fact]
    public void A_real_table_is_accepted_including_every_cell_shape_an_author_writes()
    {
        var table = Table(rules:
        [
            // A comparison, and an EMPTY cell meaning "any".
            new DecisionRule("r1", ["> 10000", ""], ["\"escalate\""]),
            // A range, and a quoted string.
            new DecisionRule("r2", ["[100..500]", "\"EU\""], ["\"eu-desk\""]),
            // A dash, DMN's other spelling of "any".
            new DecisionRule("r3", ["-", "\"US\""], ["\"auto-approve\""]),
            // A comma list.
            new DecisionRule("r4", ["1, 2, 3", "\"CA\""], ["\"small\""]),
            // A FEEL expression, which cannot be type-checked and must not be refused.
            new DecisionRule("r5", ["< floor(99.9)", ""], ["\"computed\""])
        ]);

        Assert.Empty(DecisionTableValidator.Validate(table));
    }

    // ── cells that are refused, each naming itself ──────────────────────────

    [Fact]
    public void A_word_in_a_number_column_is_refused_and_the_message_names_the_cell()
    {
        var errors = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["banana", ""], ["\"x\""])]));

        var error = Assert.Single(errors);
        Assert.Contains("Rule 1", error, StringComparison.Ordinal);
        Assert.Contains("Amount", error, StringComparison.Ordinal);
        Assert.Contains("number", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unquoted_word_in_a_string_column_is_refused_because_the_engine_reads_it_as_a_variable()
    {
        // The subtlest cell error there is. `EU` parses, deploys, and matches
        // nothing at run time -- the engine resolves it as a variable reference,
        // which is null. A validator that only checked "is it a string" would
        // accept it and the table would silently never fire that rule.
        var errors = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["", "EU"], ["\"x\""])]));

        var error = Assert.Single(errors);
        Assert.Contains("Region", error, StringComparison.Ordinal);
        Assert.Contains("variable name", error, StringComparison.Ordinal);
        // And it says what to write instead, because "this is wrong" is half a message.
        Assert.Contains("\"EU\"", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_OUTPUT_cell_is_refused_although_a_blank_input_cell_is_not()
    {
        // The asymmetry is the feature. Blank input = "any", which is how most
        // real rules are written. Blank output = a rule that matches and decides
        // nothing, setting the variable to null -- and a following gateway then
        // takes the branch nobody wrote.
        var blankInput = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["", ""], ["\"x\""])]));
        Assert.Empty(blankInput);

        var blankOutput = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["> 1", ""], ["   "])]));
        var error = Assert.Single(blankOutput);
        Assert.Contains("Route", error, StringComparison.Ordinal);
        Assert.Contains("null", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comparison_against_the_wrong_type_is_refused_not_just_a_bare_literal()
    {
        // `> banana` is a test, not a literal, so a checker that only looked at
        // bare literals would wave it through. That is the common shortcut.
        var errors = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["> banana", ""], ["\"x\""])]));

        Assert.Contains(errors, e => e.Contains("number", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_end_against_the_wrong_type_is_refused()
    {
        var errors = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["[1..lots]", ""], ["\"x\""])]));

        Assert.Contains(errors, e => e.Contains("range ends at", StringComparison.Ordinal));
    }

    // ── shape errors ────────────────────────────────────────────────────────

    [Fact]
    public void A_ragged_rule_is_refused_before_its_cells_are_blamed()
    {
        // Two inputs declared, one supplied. Reporting per-cell errors here would
        // blame the author's cells for a structural mistake, so the count error
        // short-circuits.
        var errors = DecisionTableValidator.Validate(
            Table(rules: [new DecisionRule("r1", ["> 1"], ["\"x\""])]));

        var error = Assert.Single(errors);
        Assert.Contains("1 input cells", error, StringComparison.Ordinal);
        Assert.Contains("2 input columns", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_columns_sharing_a_variable_name_are_refused()
    {
        var errors = DecisionTableValidator.Validate(Table(inputs:
        [
            new DecisionColumn("a", "First", "amount", DecisionTypeRefs.Number),
            new DecisionColumn("b", "Second", "amount", DecisionTypeRefs.Number)
        ], rules: [new DecisionRule("r1", ["> 1", "> 2"], ["\"x\""])]));

        Assert.Contains(errors, e => e.Contains("silently win", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_with_no_output_is_refused_because_it_decides_nothing()
    {
        var errors = DecisionTableValidator.Validate(Table(outputs: [], rules: []));

        Assert.Contains(errors, e => e.Contains("decides nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_with_no_input_is_refused_because_every_rule_matches()
    {
        var errors = DecisionTableValidator.Validate(Table(inputs: [], rules: []));

        Assert.Contains(errors, e => e.Contains("every rule matches", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("FIRST")]
    [InlineData("UNIQUE")]
    [InlineData("ANY")]
    [InlineData("COLLECT")]
    public void Every_offered_hit_policy_is_accepted(string policy)
    {
        Assert.Empty(DecisionTableValidator.Validate(Table(hitPolicy: policy)));
    }

    [Fact]
    public void A_hit_policy_the_editor_does_not_offer_is_refused_by_name()
    {
        // PRIORITY is real DMN and deliberately not offered: it needs an output
        // ordering the editor does not express, and offering a policy an author
        // cannot control is worse than not offering it.
        var errors = DecisionTableValidator.Validate(Table(hitPolicy: "PRIORITY"));

        var error = Assert.Single(errors);
        Assert.Contains("PRIORITY", error, StringComparison.Ordinal);
        Assert.Contains("FIRST", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("9lives")]
    [InlineData("has space")]
    [InlineData("has-hyphen")]
    public void A_key_the_engine_could_not_read_is_refused(string key)
    {
        var errors = DecisionTableValidator.Validate(Table(key: key));

        Assert.Contains(errors, e => e.Contains("decision key", StringComparison.Ordinal));
    }
}
