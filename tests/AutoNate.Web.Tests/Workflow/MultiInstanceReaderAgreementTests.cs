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
    /// Every reader of these two facts goes through the shared helpers (#356).
    /// </summary>
    /// <remarks>
    /// The defect was not the missing spelling — it was that there were two
    /// readers and they disagreed. Fixing one and leaving the other would put the
    /// next round back here, so this pins that the file has one reader each.
    /// Scanning the source is the only way to assert "nobody re-inlined it".
    /// </remarks>
    [Fact]
    public void Nothing_reads_cardinality_or_collection_except_the_shared_helpers()
    {
        var source = File.ReadAllText(Path.Combine(
            AutoNate.Web.Tests.Infrastructure.RepoRoot.Path,
            "src", "AutoNate.Web", "Services", "Workflow", "WorkflowBpmnXml.cs"));

        // Two places legitimately name a spelling: the shared helpers, and
        // ExpandMultiInstanceCardinality, whose whole job is rewriting one into
        // the other. Scoped by METHOD rather than by line number -- adding the
        // helpers shifted every line and a numeric window would have to be
        // re-tuned on every edit, which is how a guard quietly stops guarding.
        var lines = source.Split('\n');
        string? MethodAt(int index)
        {
            for (var i = index; i >= 0; i--)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    lines[i], @"^\s+(?:private|internal|public)\s+static\s+[\w<>?\[\]]+\s+(?<name>\w+)\s*\(");
                if (m.Success) return m.Groups["name"].Value;
            }

            return null;
        }

        string[] mayNameASpelling =
        [
            "DeclaresCardinality",
            "DeclaresCollection",
            "ExpandMultiInstanceCardinality",
        ];

        var offenders = lines
            .Select((line, n) => (Line: line.Trim(), Index: n))
            .Where(l => l.Line.Contains("loopCardinality", StringComparison.Ordinal)
                        || l.Line.Contains("LoopCardinalityAttribute", StringComparison.Ordinal))
            .Where(l => !l.Line.StartsWith("//", StringComparison.Ordinal))
            .Where(l => !l.Line.StartsWith("///", StringComparison.Ordinal))
            // The constant's own declaration is not a read.
            .Where(l => !l.Line.Contains("internal const string", StringComparison.Ordinal))
            .Where(l => !mayNameASpelling.Contains(MethodAt(l.Index) ?? ""))
            .Select(l => $"line {l.Index + 1} (in {MethodAt(l.Index) ?? "?"}): {l.Line}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines read a cardinality spelling directly instead of going through "
            + "DeclaresCardinality. Two readers disagreeing is exactly what #356 was.\n  "
            + string.Join("\n  ", offenders));
    }
}
