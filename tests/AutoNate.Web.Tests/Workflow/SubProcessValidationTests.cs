using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Embedded subprocesses (#161) — the refusal, and what is deliberately not refused.
/// </summary>
/// <remarks>
/// <para>
/// The story's premise was that "an empty subprocess, or one with no end event, both
/// deploy today and hang". Both halves were checked against Flowable 8.0.0 and both
/// were wrong in ways that matter:
/// </para>
/// <para>
/// An **empty subprocess** deploys and then fails at start with a 500 — "No initial
/// activity found for subprocess" — rather than hanging. The remedy is unchanged,
/// because the failure lands on whoever ran the process rather than on the author who
/// published it, but the diagnosis is different and the real rule is about the *start
/// event*, which is what Flowable's own message names.
/// </para>
/// <para>
/// A subprocess with **no end event works correctly**. Flowable completes it once no
/// tokens remain inside; a run completed normally. Refusing it — which the acceptance
/// criterion asked for — would have broken diagrams that run today, so it is
/// deliberately not refused and this class pins that.
/// </para>
/// </remarks>
public sealed class SubProcessValidationTests
{
    private static string Process(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
        {body}
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void An_empty_subprocess_is_refused_and_told_why_it_matters()
    {
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="sub" name="Empty one" />
        """));

        var error = Assert.Single(result.Errors, e => e.Contains("Subprocess", StringComparison.Ordinal));
        Assert.Contains("Empty one", error, StringComparison.Ordinal);
        Assert.Contains("start event", error, StringComparison.Ordinal);
        // The reason refusing at publish is the right remedy: otherwise the failure
        // reaches the wrong person.
        Assert.Contains("whoever ran it", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subprocess_with_activities_but_no_start_event_is_refused()
    {
        // This is the same engine failure as the empty case — "No initial activity
        // found" — so a rule keyed on emptiness alone would miss it.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="sub" name="No way in">
              <bpmn:userTask id="t" name="Orphan" />
            </bpmn:subProcess>
        """));

        Assert.Contains(result.Errors, e => e.Contains("No way in", StringComparison.Ordinal));
    }

    [Fact]
    public void A_subprocess_with_no_end_event_is_NOT_refused()
    {
        // The half of the acceptance criterion that turned out to be wrong.
        // Verified against Flowable 8.0.0: completing the inner task left no tokens,
        // the subprocess completed, and the process finished normally. Refusing this
        // would break working diagrams.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="sub" name="Stops quietly">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="t" />
              <bpmn:userTask id="t" name="Inner" />
            </bpmn:subProcess>
        """));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Stops quietly", StringComparison.Ordinal));
    }

    [Fact]
    public void A_well_formed_subprocess_is_accepted()
    {
        // The complement — without it every test above passes for a validator that
        // refuses every subprocess.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="sub" name="Fine">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
              <bpmn:endEvent id="se" />
            </bpmn:subProcess>
        """));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Fine", StringComparison.Ordinal));
    }

    [Fact]
    public void Nested_subprocesses_are_each_checked()
    {
        // Two levels execute (verified against the engine), so the rule has to reach
        // the inner one — a check that only walked top-level children would pass a
        // diagram that fails at runtime one level down.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="outer" name="Outer">
              <bpmn:startEvent id="os" />
              <bpmn:sequenceFlow id="of" sourceRef="os" targetRef="inner" />
              <bpmn:subProcess id="inner" name="Inner has no way in">
                <bpmn:userTask id="t" name="Orphan" />
              </bpmn:subProcess>
            </bpmn:subProcess>
        """));

        Assert.Contains(result.Errors, e => e.Contains("Inner has no way in", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("'Outer'", StringComparison.Ordinal));
    }

    // ── Round trip: where BPMN serialisation loses data ─────────────────────

    [Fact]
    public void A_subprocess_keeps_its_children_and_their_layout_through_a_save()
    {
        // AC8. The failure this catches is specific: a collapsed subprocess whose
        // *children* were dropped still round-trips as a subprocess, so asserting
        // "the subProcess element is still there" proves nothing. The children and
        // their BPMNShape entries are what has to survive.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="P" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <bpmn:subProcess id="sub" name="Grouped">
                  <bpmn:startEvent id="ss" />
                  <bpmn:sequenceFlow id="sf0" sourceRef="ss" targetRef="inner" />
                  <bpmn:userTask id="inner" name="Inner work" />
                  <bpmn:sequenceFlow id="sf1" sourceRef="inner" targetRef="se" />
                  <bpmn:endEvent id="se" />
                </bpmn:subProcess>
                <bpmn:sequenceFlow id="f1" sourceRef="sub" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              <bpmndi:BPMNDiagram id="d">
                <bpmndi:BPMNPlane id="pl" bpmnElement="p">
                  <bpmndi:BPMNShape id="sh_sub" bpmnElement="sub">
                    <dc:Bounds x="200" y="80" width="350" height="200" />
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="sh_inner" bpmnElement="inner">
                    <dc:Bounds x="300" y="120" width="100" height="80" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        // A rename on the subprocess, which is the ordinary edit that would take
        // its children with it if the handler rebuilt the element.
        var applied = WorkflowBpmnXml.ApplyProcessMetadata(
            xml, "p", "P",
            [new WorkflowElementSnapshot("sub", "bpmn:SubProcess", "Renamed group")]);

        var document = XDocument.Parse(applied);
        var sub = document.Descendants().Single(e => e.Attribute("id")?.Value == "sub");

        Assert.Equal("Renamed group", sub.Attribute("name")?.Value);

        // The children survived, by id.
        var childIds = sub.Elements().Select(e => e.Attribute("id")?.Value).ToArray();
        Assert.Contains("ss", childIds);
        Assert.Contains("inner", childIds);
        Assert.Contains("se", childIds);
        Assert.Contains("sf0", childIds);

        // And so did the layout — including the inner element's own shape, which is
        // the part a naive DI rewrite drops.
        Assert.Contains("sh_inner", applied, StringComparison.Ordinal);
        Assert.Contains("x=\"300\"", applied, StringComparison.Ordinal);
        Assert.Contains("width=\"350\"", applied, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_instance_subprocess_keeps_its_marker_through_a_save()
    {
        // AC4 asks that a subprocess can be marked multi-instance by reusing #159's
        // marker work rather than a separate implementation. #159 has not landed, so
        // what is asserted here is that the marker SERIALISES and survives — the
        // execution assertion belongs to #159 and is not duplicated.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="P" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:subProcess id="sub" name="Per item">
                  <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                        flowable:collection="${items}"
                                                        flowable:elementVariable="item" />
                  <bpmn:startEvent id="ss" />
                  <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
                  <bpmn:endEvent id="se" />
                </bpmn:subProcess>
                <bpmn:endEvent id="e" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(
            xml, "p", "P",
            [new WorkflowElementSnapshot("sub", "bpmn:SubProcess", "Per item, renamed")]);

        Assert.Contains("multiInstanceLoopCharacteristics", applied, StringComparison.Ordinal);
        Assert.Contains("flowable:collection=\"${items}\"", applied, StringComparison.Ordinal);
        Assert.Contains("flowable:elementVariable=\"item\"", applied, StringComparison.Ordinal);

        // And the marker does not make the subprocess look start-event-less to the
        // new validation — a rule that counted only flow nodes would trip on it.
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(applied).Errors,
            error => error.Contains("Per item", StringComparison.Ordinal));
    }

    [Fact]
    public void An_event_subprocess_is_refused_by_its_own_story_not_this_one()
    {
        // This guard was written when #162 did not exist, asserting that #161's
        // subprocess rule left event subprocesses alone so the two stories would
        // not refuse the same shape in different words. #162 has since landed and
        // now owns it, so the assertion moves from "nobody refuses this" to
        // "exactly one story does, and it is the right one" — which is what the
        // guard was protecting all along.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:subProcess id="handler" name="On error" triggeredByEvent="true">
              <bpmn:userTask id="t" name="Handle" />
            </bpmn:subProcess>
        """));

        // #162's rule fires: an event subprocess is TRIGGERED, so the problem is
        // that nothing can trigger it.
        Assert.Contains(
            result.Errors,
            e => e.Contains("On error", StringComparison.Ordinal)
                 && e.Contains("nothing can ever trigger it", StringComparison.Ordinal));

        // #161's does not: it is about a subprocess the engine cannot ENTER,
        // which is a different sentence for a different shape. Two rules refusing
        // one diagram with different explanations is the confusion this guards.
        Assert.DoesNotContain(
            result.Errors,
            e => e.Contains("On error", StringComparison.Ordinal)
                 && e.Contains("cannot enter it", StringComparison.Ordinal));
    }
}
