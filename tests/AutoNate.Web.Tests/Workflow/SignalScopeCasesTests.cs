using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Every neighbouring signal-scope case, in one table (#273, #274).
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of how #270 failed verification. Its fix was correct
/// for the case it was written for and wrong for the two cases beside it: a
/// scoped catch sharing a name with an unscoped THROW had its scope silently
/// dropped (#273), and an event-subprocess start event was refused as though it
/// forced the signal global (#274).
/// </para>
/// <para>
/// The lesson was not "write a test for #273 and a test for #274" — that would
/// leave the next neighbour unexamined. It was that the code conflated two
/// distinct notions, so the cases have to be enumerated as a grid:
/// </para>
/// <list type="bullet">
/// <item>who declares a scope — nobody, one event, or two disagreeing;</item>
/// <item>what kind of event shares the name — throw, catch, boundary,
/// event-subprocess start, or process-level start.</item>
/// </list>
/// <para>
/// The rule that falls out: <b>only a process-level start event forces a signal
/// global</b>, because only it is triggered from outside any instance. Everything
/// else may share an instance-scoped signal. Declaring nothing is not a conflict;
/// declaring the opposite is.
/// </para>
/// <para>
/// <c>SignalScopeExecutionTests.Every_accepted_signal_scope_case_deploys</c> feeds
/// each accepted row here to a real engine, because "the expansion is correct" and
/// "the engine takes it" are different claims and #270 proved the gap between them.
/// </para>
/// </remarks>
public sealed class SignalScopeCasesTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";

    /// <summary>An event referencing signal `Sig_1`, optionally declaring a scope.</summary>
    private static string Event(string localName, string id, string? scope, string extra = "")
    {
        var ext = scope is null
            ? ""
            : $"""
                 <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="{scope}" />
                  </bpmn:extensionElements>
               """;
        return $"""
              <bpmn:{localName} id="{id}" name="{id}"{extra}>
                {ext}
                <bpmn:signalEventDefinition signalRef="Sig_1" />
              </bpmn:{localName}>
            """;
    }

    internal static string Diagram(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="the.signal" />
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Work" />
        {body}
          </bpmn:process>
        </bpmn:definitions>
        """;

    // ── The accepted cases, and what scope the shared root must end up with ──

    public static TheoryData<string, string, string?> Accepted() => new()
    {
        // one event declares instance, nobody disagrees
        {
            "a lone scoped catch",
            Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        // #273. An unscoped THROW is not a conflict — it raises the signal, and
        // the scope decides who hears it. This used to silently drop the scope.
        {
            "a scoped catch beside an unscoped throw",
            Event("intermediateThrowEvent", "th", null) + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        {
            "a scoped catch beside an unscoped boundary event",
            Event("boundaryEvent", "b", null, " attachedToRef=\"t\"")
                + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        {
            "a scoped catch beside a second catch that declares nothing",
            Event("intermediateCatchEvent", "c2", null) + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        // Two events agreeing is not a conflict either.
        {
            "two catches both scoped to the instance",
            Event("intermediateCatchEvent", "c1", "instance") + Event("intermediateCatchEvent", "c2", "instance"),
            "processInstance"
        },
        // #274. An event-subprocess start is an IN-INSTANCE handler.
        {
            "a scoped throw beside an event-subprocess signal start",
            Event("intermediateThrowEvent", "th", "instance")
                + $"""
                      <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                    {Event("startEvent", "hs", null, " isInterrupting=\"false\"")}
                      </bpmn:subProcess>
                   """,
            "processInstance"
        },
        // Everything global stays global, and nothing is written.
        {
            "a catch explicitly declaring global",
            Event("intermediateCatchEvent", "c", "global"),
            null
        },
        {
            "a process-level signal start with an unscoped catch",
            Event("startEvent", "ss", null) + Event("intermediateCatchEvent", "c", null),
            null
        },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void An_accepted_case_publishes_and_carries_the_scope_the_author_asked_for(
        string because, string body, string? expectedScope)
    {
        var xml = Diagram(body);

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));

        // Exactly one root. Two sharing a name is what Flowable refuses (#270).
        var root = Assert.Single(expanded.Descendants(Bpmn + "signal"));

        // #273's defect was invisible here until this assertion existed: the
        // diagram published clean and the scope was simply absent.
        Assert.Equal(expectedScope, root.Attribute(Flowable + "scope")?.Value);
        Assert.Equal("the.signal", root.Attribute("name")?.Value);
        _ = because;
    }

    // ── The refused cases ────────────────────────────────────────────────────

    public static TheoryData<string, string> Refused() => new()
    {
        {
            "a scoped catch beside a process-level signal start",
            Event("startEvent", "ss", null) + Event("intermediateCatchEvent", "c", "instance")
        },
        {
            "two events declaring opposite scopes",
            Event("intermediateThrowEvent", "th", "global") + Event("intermediateCatchEvent", "c", "instance")
        },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_contradiction_is_refused_naming_the_signal(string because, string body)
    {
        var result = WorkflowBpmnXml.ValidateProcess(Diagram(body));

        var error = Assert.Single(result.Errors, e => e.Contains("the.signal", StringComparison.Ordinal));
        Assert.Contains("one scope per signal name", error, StringComparison.Ordinal);
        _ = because;
    }

    [Fact]
    public void A_refused_diagram_still_emits_only_one_signal_root()
    {
        // Belt and braces. Validation refuses these, but if a caller ever skipped
        // validation the expansion must not hand Flowable two roots of one name —
        // that is the 500 #270 was filed for.
        foreach (var row in Refused())
        {
            var body = (string)row[1]!;
            var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram(body)));
            Assert.Single(expanded.Descendants(Bpmn + "signal"));
        }
    }
}
