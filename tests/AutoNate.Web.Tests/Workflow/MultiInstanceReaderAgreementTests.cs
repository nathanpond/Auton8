using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// A fixed-count multi-instance publishes, in the spelling the studio writes (#356).
/// </summary>
/// <remarks>
/// <para>
/// <b>This defect was live for four rounds and no backend test could see it.</b>
/// The studio writes cardinality as <c>autonate:loopCardinality</c> — an
/// attribute — because bpmn-js has no Flowable moddle extension.
/// <c>ExpandForDeployment</c> converts it to <c>&lt;bpmn:loopCardinality&gt;</c>,
/// and validation runs on the <b>stored</b> diagram, before that conversion.
/// </para>
/// <para>
/// The publish gate added in #333 read only the child element. So a fixed-count
/// multi-instance was refused with "has nothing to repeat over. Set the collection
/// … or a fixed number of times" — telling the author to set what they had set.
/// The only test that caught it, <c>MultiInstanceExecutionTests</c>, carries
/// <c>RequiresService=Flowable</c>, which CI excludes.
/// </para>
/// <para>
/// So these rows deliberately need <b>no engine</b>. The rule they guard is a pure
/// function over XML; making its guard depend on a running Flowable is what let
/// four rounds pass with the feature broken.
/// </para>
/// </remarks>
public sealed class MultiInstanceReaderAgreementTests
{
    private static string Diagram(string loop) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="D" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve">
              {loop}
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static bool RefusedForNothingToRepeatOver(string xml) =>
        WorkflowBpmnXml.ValidateProcess(xml).Errors
            .Any(e => e.Contains("nothing to repeat over", StringComparison.Ordinal));

    /// <summary>
    /// Both spellings of a fixed count publish — the studio's is the attribute.
    /// </summary>
    [Theory]
    // What the studio actually writes. This row is the regression.
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3" />""")]
    // What the expansion produces, and what a hand-authored diagram may carry.
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false"><bpmn:loopCardinality>3</bpmn:loopCardinality></bpmn:multiInstanceLoopCharacteristics>""")]
    // An expression, which is the common real case.
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="true" autonate:loopCardinality="${headcount}" />""")]
    public void A_fixed_count_publishes_in_either_spelling(string loop)
    {
        Assert.False(
            RefusedForNothingToRepeatOver(Diagram(loop)),
            "a multi-instance with a fixed count was refused for having nothing to repeat over");
    }

    /// <summary>Both spellings of a collection publish too.</summary>
    [Theory]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false" flowable:collection="items" flowable:elementVariable="i" />""")]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false"><bpmn:loopDataInputRef>items</bpmn:loopDataInputRef></bpmn:multiInstanceLoopCharacteristics>""")]
    public void A_collection_publishes_in_either_spelling(string loop)
    {
        Assert.False(RefusedForNothingToRepeatOver(Diagram(loop)));
    }

    /// <summary>
    /// The complement: saying neither is still refused (#333's actual subject).
    /// </summary>
    /// <remarks>
    /// Without this, "accept every multi-instance" passes every row above while
    /// reopening the defect #333 was written for — measured, the engine refuses
    /// that shape with <c>flowable-multi-instance-missing-collection</c>.
    /// </remarks>
    [Theory]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false" />""")]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="true" />""")]
    // Present but empty is not a declaration.
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="" />""")]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false"><bpmn:loopCardinality>  </bpmn:loopCardinality></bpmn:multiInstanceLoopCharacteristics>""")]
    public void Saying_neither_is_still_refused(string loop)
    {
        Assert.True(
            RefusedForNothingToRepeatOver(Diagram(loop)),
            "a multi-instance with neither a collection nor a count was accepted");
    }

    /// <summary>
    /// A hand-written aggregation element is not refused (#364).
    /// </summary>
    /// <remarks>
    /// <c>ExpandMultiInstanceAggregation</c> honours a hand-written
    /// <c>&lt;flowable:variableAggregation&gt;</c> — "an author who hand-wrote the
    /// aggregation meant it" — while <c>BuildMultiInstanceErrors</c> read only the
    /// <c>autonate:aggregate*</c> attributes. An author who wrote the element was
    /// told they "did not say which variable to collect".
    /// <b>That is #356's sentence in a second rule</b>, found one round later, and
    /// it is why the shared-reader treatment now covers three facts rather than two.
    /// </remarks>
    [Theory]
    [InlineData("""autonate:aggregateTarget="results" """)]
    [InlineData("""autonate:aggregateSource="score" """)]
    [InlineData("")]
    public void A_hand_written_aggregation_element_is_accepted(string attributes)
    {
        var xml = Diagram($"""
            <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                flowable:collection="items" flowable:elementVariable="i" {attributes}>
              <bpmn:extensionElements>
                <flowable:variableAggregation target="results">
                  <flowable:variable source="score" target="score" />
                </flowable:variableAggregation>
              </bpmn:extensionElements>
            </bpmn:multiInstanceLoopCharacteristics>
            """);

        // #373: this asserted only two fragments, and the source-only branch emits
        // a THIRD -- "collects the variable 'score' from each run but does not say
        // where to put the results." -- so that row of the theory asserted nothing.
        // Reverting both guards failed only the aggregateTarget row.
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("which variable to collect", StringComparison.Ordinal)
                 || e.Contains("collects each run", StringComparison.Ordinal)
                 || e.Contains("where to put the results", StringComparison.Ordinal));
    }

    /// <summary>
    /// The complement: half an aggregation with no element IS still refused (#364).
    /// </summary>
    /// <remarks>
    /// Without this, "never refuse an aggregation" passes the rows above while
    /// reopening the defect the rule was written for.
    /// </remarks>
    [Theory]
    [InlineData("""autonate:aggregateTarget="results" """, "which variable to collect")]
    [InlineData("""autonate:aggregateSource="score" """, "collect")]
    public void Half_an_aggregation_with_no_element_is_still_refused(string attributes, string expected)
    {
        var xml = Diagram($"""
            <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                flowable:collection="items" flowable:elementVariable="i" {attributes} />
            """);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// One fact, one reader — for every multi-instance fact, in every file (#356, #364).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first version of this scan was built from #356's shape and four
    /// siblings walked past it. Confirmed by mutation: an <em>identical</em> second
    /// collection reader passed 10/10 (it greps only the cardinality spellings,
    /// despite its name); a new method reading both cardinality spellings passed
    /// (its method-attribution regex cannot match a return type containing a comma
    /// or an <c>async</c> modifier, and on a miss walked <em>backwards</em> into the
    /// allowlist); and a reader in any other file was invisible (it hard-coded one
    /// path).
    /// </para>
    /// <para>
    /// So this is written from the property — <b>one fact has one reader</b> —
    /// rather than from the example. Every spelling of every fact, every file that
    /// reads any of them, and allowlisting by explicit declaration rather than by
    /// guessing which method a line sits in.
    /// </para>
    /// <para>
    /// What it still cannot see: a reader that reaches the fact by a helper of its
    /// own (`loop.Attribute(SomeOtherConstant)`). That is a real limit, stated
    /// rather than papered over — the constants below are the spellings as
    /// written today.
    /// </para>
    /// </remarks>
    [Fact]
    public void One_fact_one_reader_across_every_file_that_reads_them()
    {
        // Every spelling of every multi-instance fact, not just the one that broke.
        // Derived from what the shared readers actually touch, not from which
        // spelling last broke. #373: `"collection"` -- the studio's PRIMARY
        // spelling of the collection fact and half of DeclaresCollection -- was
        // missing from the previous two versions of this list, in a guard named
        // for that very fact. Each rewrite had widened along the axis the last
        // bypass used.
        string[] spellings =
        [
            // cardinality
            "loopCardinality", "LoopCardinalityAttribute",
            // collection
            "\"collection\"", "loopDataInputRef",
            // aggregation
            "variableAggregation",
            "AggregateTargetAttribute", "AggregateSourceAttribute",
        ];

        // The only places a spelling may be named: the shared readers, and the
        // expansion, whose job is rewriting one spelling into another. Declared
        // by name, so a new method is an offender until someone adds it here on
        // purpose.
        // file + method, not the bare name: a method called DeclaresCardinality in
        // any other class was allowlisted by the previous version (#373).
        var mayNameASpelling = new HashSet<string>(StringComparer.Ordinal)
        {
            "WorkflowBpmnXml.cs:DeclaresCardinality",
            "WorkflowBpmnXml.cs:DeclaresCollection",
            "WorkflowBpmnXml.cs:CollectionName",
            "WorkflowBpmnXml.cs:DeclaresAggregationElement",
            "WorkflowBpmnXml.cs:AggregationTarget",
            "WorkflowBpmnXml.cs:AggregationSource",
            "WorkflowBpmnXml.cs:ExpandMultiInstanceCardinality",
            "WorkflowBpmnXml.cs:ExpandMultiInstanceAggregation",
        };

        var roots = new[]
        {
            // All of src/, not one project: a reader in Plugin.Abstractions or
            // shared/ was invisible to the previous version (#373).
            Path.Combine(AutoNate.Web.Tests.Infrastructure.RepoRoot.Path, "src"),
        };

        var offenders = new List<string>();

        foreach (var file in roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var relative = Path.GetRelativePath(AutoNate.Web.Tests.Infrastructure.RepoRoot.Path, file);
            var lines = File.ReadAllLines(file);

            // Brace-scoped: walk the file once, tracking which declared method we
            // are inside. No backwards regex walk, so a signature this cannot
            // parse leaves us OUTSIDE a method rather than inside the previous one
            // -- failing loud instead of silently allowlisting.
            var current = (string?)null;
            var depthAtEntry = 0;
            var depth = 0;

            foreach (var (raw, index) in lines.Select((l, i) => (l, i)))
            {
                var line = raw.Trim();

                // The LAST identifier-then-paren on a declaration line. A tuple
                // return type -- "(bool, bool) Name(" -- has no word character
                // before its own "(", so it cannot be mistaken for the name. The
                // first version's lazy prefix matched "static" here (#364).
                if (current is null
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        raw, @"^\s+(?:private|internal|public|protected)\b")
                    && !raw.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                {
                    var names = System.Text.RegularExpressions.Regex
                        .Matches(raw, @"(?<name>\w+)\s*\(")
                        .Select(m => m.Groups["name"].Value)
                        .ToList();
                    if (names.Count > 0)
                    {
                        current = names[^1];
                        depthAtEntry = depth;
                    }
                }

                depth += raw.Count(c => c == '{') - raw.Count(c => c == '}');

                // Check BEFORE resetting, so a one-line expression body is still
                // attributed to its own method.
                var skip = line.StartsWith("//", StringComparison.Ordinal)
                           || line.StartsWith("///", StringComparison.Ordinal)
                           || line.Contains("internal const string", StringComparison.Ordinal);

                var key = current is null ? null : $"{Path.GetFileName(file)}:{current}";

                if (!skip
                    && spellings.Any(sp => line.Contains(sp, StringComparison.Ordinal))
                    && !(key is not null && mayNameASpelling.Contains(key)))
                {
                    offenders.Add($"{relative}:{index + 1} (in {current ?? "<file scope>"}): {line}");
                }

                // A member ends at a closing brace OR -- for an expression-bodied
                // member, which has none -- at the semicolon that finishes it.
                //
                // The first version reset only on '}'. Every expression-bodied
                // member therefore left `current` stuck on its own name forever,
                // so everything below the first one inherited an ALLOWLISTED name
                // and two of the three bypasses walked straight through. That is
                // the "walks backwards into the allowlist" defect this scan was
                // rewritten to remove, reintroduced in the rewrite.
                if (current is not null && depth <= depthAtEntry
                    && (raw.Contains('}') || raw.TrimEnd().EndsWith(";", StringComparison.Ordinal)))
                {
                    current = null;
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These lines read a multi-instance fact directly instead of going through a "
            + "shared reader. Two readers of one fact disagreeing is #356 and #364, and it "
            + "has now shipped twice.\n  "
            + string.Join("\n  ", offenders)
            + "\n\nEither route it through DeclaresCardinality / DeclaresCollection / "
            + "DeclaresAggregationElement, or add the method to `mayNameASpelling` "
            + "deliberately.");
    }

    /// <summary>The scan can see a reader it is supposed to catch (#364).</summary>
    /// <remarks>
    /// The first version passed while three different bypasses walked through it.
    /// These assert the scan's own mechanics on synthetic sources, so a regex that
    /// stopped matching cannot report a clean tree.
    /// </remarks>
    [Fact]
    public void The_scan_can_see_a_reader_outside_the_allowlist()
    {
        // A method whose return type contains a comma -- the shape that defeated
        // the first version's signature regex.
        const string Sneaky = """
                private static (bool, bool) CardinalityProbe(XElement loop)
                {
                    return (loop.Attribute(AutoNateNamespace + LoopCardinalityAttribute) is not null, false);
                }
            """;

        var names = System.Text.RegularExpressions.Regex
            .Matches(Sneaky.Split('\n')[0], @"(?<name>\w+)\s*\(")
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Assert.True(names.Count > 0, "the signature matcher cannot see a tuple return type");
        Assert.Equal("CardinalityProbe", names[^1]);
        Assert.DoesNotContain("CardinalityProbe", new[]
        {
            "DeclaresCardinality", "DeclaresCollection", "DeclaresAggregationElement",
            "AggregationTarget", "AggregationSource",
            "ExpandMultiInstanceCardinality", "ExpandMultiInstanceAggregation",
        });
    }
}
