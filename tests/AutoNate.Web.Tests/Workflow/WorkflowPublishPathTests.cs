using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// What publish does to a diagram on its way to the engine (#253).
/// </summary>
/// <remarks>
/// <para>
/// Four guarantees this milestone rests on had no engine-free coverage at all,
/// and CI excludes <c>RequiresService=Flowable</c> — so the whole of the callback
/// stamping, the stored-versus-deployed split, call-activity pinning and the
/// mapped output ran only on a developer machine, if at all.
/// </para>
/// <para>
/// The functions here are pure. There was never a reason for the only tests of
/// them to need a running Flowable; what there was, was a set of E2E tests that
/// happened to cover the happy path and got mistaken for coverage.
/// </para>
/// </remarks>
public sealed class WorkflowPublishPathTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";

    // ── #223: the callback base URL is stamped only when overridden ─────────

    private const string BehaviorDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:serviceTask id="behavior" flowable:delegateExpression="${autonateBehaviorDelegate}" />
            <bpmn:serviceTask id="other" flowable:delegateExpression="${somethingElse}" />
            <bpmn:scriptTask id="script" scriptFormat="javascript">
              <bpmn:script>return 1;</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_stamped_when_no_callback_override_is_configured(string? override_)
    {
        var stamped = WorkflowBpmnXml.StampCallbackBaseUrl(BehaviorDiagram, override_);

        // This is every production deployment, and it had no test. A regression
        // defaulting the argument, or dropping the IsNullOrWhiteSpace guard,
        // would put an E2E-only attribute into every published diagram with
        // nothing failing -- and the engine would then call back to a host that
        // exists only on a test machine.
        Assert.DoesNotContain("autonateCallbackBaseUrl", stamped, StringComparison.Ordinal);

        // Byte-identical, not merely free of the attribute: a reserialisation
        // that reorders or reformats is still a change to what gets deployed.
        Assert.Equal(BehaviorDiagram, stamped);
    }

    [Fact]
    public void The_override_is_stamped_on_behavior_and_script_tasks_only()
    {
        var stamped = WorkflowBpmnXml.StampCallbackBaseUrl(BehaviorDiagram, "http://host.test:5099");
        var document = XDocument.Parse(stamped);

        string? Attribute(string id) => document
            .Descendants()
            .Single(element => element.Attribute("id")?.Value == id)
            .Attribute(Flowable + "autonateCallbackBaseUrl")?.Value;

        Assert.Equal("http://host.test:5099", Attribute("behavior"));
        Assert.Equal("http://host.test:5099", Attribute("script"));

        // A service task wired to something else has no callback to redirect, so
        // stamping it would be writing an attribute onto a third party's task.
        Assert.Null(Attribute("other"));
    }

    // ── #112: what is stored is not what is deployed ────────────────────────

    private const string MessageThrowDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="throw" />
            <bpmn:intermediateThrowEvent id="throw" name="Tell the shipper">
              <bpmn:messageEventDefinition id="med" messageRef="m" />
            </bpmn:intermediateThrowEvent>
            <bpmn:sequenceFlow id="f2" sourceRef="throw" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          <bpmn:message id="m" name="ShipmentReady" />
        </bpmn:definitions>
        """;

    [Fact]
    public void Expanding_a_message_throw_rewrites_the_deployed_copy_and_not_the_stored_one()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(MessageThrowDiagram);

        // The expansion happened at all. Without this the assertions below are
        // satisfied by a function that returns its input.
        Assert.Contains("serviceTask", deployed, StringComparison.Ordinal);
        Assert.DoesNotContain("intermediateThrowEvent", deployed, StringComparison.Ordinal);

        // And the author's diagram is untouched -- the property the whole
        // rewrite-at-publish design rests on. The test that claimed to cover this
        // used the complex gateway fixture and asserted an input string variable
        // was unchanged, which is trivially true of any string -> string function.
        // Changing the publish endpoint to persist the DEPLOYABLE copy would have
        // left it green.
        var storedAgain = XDocument.Parse(MessageThrowDiagram);
        Assert.NotNull(storedAgain.Descendants(Bpmn + "intermediateThrowEvent").SingleOrDefault());
        Assert.NotNull(storedAgain.Descendants(Bpmn + "messageEventDefinition").SingleOrDefault());
        Assert.Empty(storedAgain.Descendants(Bpmn + "serviceTask"));
    }

    [Fact]
    public void Expanding_twice_gives_the_same_deployed_copy()
    {
        // Publishing the same model twice must not compound. A rewrite that
        // appended rather than replaced would pass every single-publish test.
        var once = WorkflowBpmnXml.ExpandForDeployment(MessageThrowDiagram);
        var twice = WorkflowBpmnXml.ExpandForDeployment(MessageThrowDiagram);

        Assert.Equal(once, twice);
    }

    // ── #113: call activities are pinned to a version, and only unpinned ones ──

    private static string CallDiagram(string calledElement, string? calledElementType = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="parent" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:callActivity id="call" calledElement="{calledElement}"{
                (calledElementType is null ? "" : $" flowable:calledElementType=\"{calledElementType}\"")} />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static XElement CallElement(string xml) =>
        XDocument.Parse(xml).Descendants(Bpmn + "callActivity").Single();

    [Fact]
    public void A_call_activity_is_pinned_to_the_definition_id_of_the_child_it_names()
    {
        var pinned = WorkflowBpmnXml.PinCallActivityTargets(
            CallDiagram("child"),
            new Dictionary<string, string> { ["child"] = "child:3:9001" });

        var call = CallElement(pinned);
        Assert.Equal("child:3:9001", call.Attribute("calledElement")?.Value);
        Assert.Equal("id", call.Attribute(Flowable + "calledElementType")?.Value);
    }

    [Fact]
    public void A_freshly_published_parent_picks_up_the_child_version_current_at_that_moment()
    {
        // The direction the story's test plan named and no test covered. A
        // PinCallActivityTargets that always resolved to version 1 -- or that
        // cached -- would pass the test above and fail here, which is the only
        // way to tell "pins" from "pins correctly".
        var first = WorkflowBpmnXml.PinCallActivityTargets(
            CallDiagram("child"),
            new Dictionary<string, string> { ["child"] = "child:1:5001" });
        var second = WorkflowBpmnXml.PinCallActivityTargets(
            CallDiagram("child"),
            new Dictionary<string, string> { ["child"] = "child:2:7002" });

        Assert.Equal("child:1:5001", CallElement(first).Attribute("calledElement")?.Value);
        Assert.Equal("child:2:7002", CallElement(second).Attribute("calledElement")?.Value);
    }

    [Fact]
    public void An_already_pinned_call_activity_is_left_alone()
    {
        // A running parent's deployed copy carries calledElementType="id". Were
        // that re-resolved as though it were a key, the pin would drift to
        // whatever is current -- which is exactly the behaviour pinning exists to
        // prevent.
        var xml = CallDiagram("child:1:5001", calledElementType: "id");
        var pinned = WorkflowBpmnXml.PinCallActivityTargets(
            xml, new Dictionary<string, string> { ["child:1:5001"] = "child:9:9999" });

        Assert.Equal("child:1:5001", CallElement(pinned).Attribute("calledElement")?.Value);
    }

    [Fact]
    public void A_call_activity_naming_an_unpublished_child_is_left_for_validation_to_refuse()
    {
        // Silently pinning to nothing, or dropping the attribute, would turn a
        // publish-time refusal into a runtime failure in someone's process.
        var pinned = WorkflowBpmnXml.PinCallActivityTargets(
            CallDiagram("child"),
            new Dictionary<string, string> { ["a-different-child"] = "x:1:1" });

        var call = CallElement(pinned);
        Assert.Equal("child", call.Attribute("calledElement")?.Value);
        Assert.Null(call.Attribute(Flowable + "calledElementType"));
    }

    [Fact]
    public void Extracting_targets_reports_the_keys_still_needing_resolution()
    {
        var targets = WorkflowBpmnXml.ExtractCallActivityTargets(CallDiagram("child"));
        Assert.Equal(("call", "child"), Assert.Single(targets));

        // An already-pinned call needs no lookup, and reporting it would make the
        // publish path ask the engine to resolve a definition id as a key.
        Assert.Empty(WorkflowBpmnXml.ExtractCallActivityTargets(
            CallDiagram("child:1:5001", calledElementType: "id")));
    }
}
