using System.Xml.Linq;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// A business rule task becomes a DMN service task in the deployed copy (#111).
/// </summary>
/// <remarks>
/// <para>
/// The engine facts these rest on were measured before any of this was written,
/// and they are sharper than the story assumed. A <c>bpmn:businessRuleTask</c>
/// does not fail at run time on this image — it fails to <b>deploy</b>, HTTP 500,
/// <c>NoClassDefFoundError: org/kie/api/runtime/rule/AgendaFilter</c>, because
/// <c>BusinessRuleParseHandler</c> resolves the KIE/Drools behaviour at deployment
/// time and KIE is not in the image. <c>flowable:type="dmn"</c> is never consulted
/// on that element; the DMN integration hangs off
/// <c>createDmnActivityBehavior(ServiceTask)</c>.
/// </para>
/// <para>
/// So the story's AC — <i>"this is a single edit to the BPMN support manifest"</i>
/// — was not available. <c>BusinessRuleTaskExecutionTests</c> proves the expanded
/// shape runs on the real engine; this half pins the rewrite on every merge.
/// </para>
/// </remarks>
public sealed class BusinessRuleTaskExpansionTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";
    private static readonly XNamespace AutoNate = "http://autonate.dev/workflows";

    private static string Diagram(string? decisionKey) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice"{(
                decisionKey is null ? "" : $" autonate:decisionKey=\"{decisionKey}\"")}>
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="after" />
            <bpmn:userTask id="after" name="After" />
            <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void The_deployed_copy_carries_a_dmn_service_task()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram("invoiceRouting")));

        // The element the engine can parse. A businessRuleTask left in the
        // deployed copy is a 500 at deploy, not a run-time surprise.
        Assert.Empty(expanded.Descendants(Bpmn + "businessRuleTask"));
        var task = Assert.Single(expanded.Descendants(Bpmn + "serviceTask"));

        Assert.Equal("decide", task.Attribute("id")!.Value);
        Assert.Equal("dmn", task.Attribute(Flowable + "type")!.Value);

        // The field name is the engine's, read out of DmnActivityBehavior.
        var field = Assert.Single(task.Descendants(Flowable + "field"));
        Assert.Equal("decisionTableReferenceKey", field.Attribute("name")!.Value);
        Assert.Equal("invoiceRouting", field.Element(Flowable + "string")!.Value);
    }

    [Fact]
    public void The_authoring_attribute_is_stripped_from_the_deployed_copy()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram("invoiceRouting")));

        // Leaving it behind is a schema violation of the same class that refused
        // `scriptFormat` on a complexGateway (#218) -- and Flowable validates the
        // deployed XML strictly, so it is a 500 at publish rather than a
        // degradation.
        Assert.Empty(expanded.Descendants().Attributes(AutoNate + "decisionKey"));
    }

    [Fact]
    public void The_element_keeps_everything_else_about_itself()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram("invoiceRouting")));
        var task = Assert.Single(expanded.Descendants(Bpmn + "serviceTask"));

        // Rebuilding the node from scratch would drop its incoming/outgoing
        // references and its name -- and a task with no incoming flow is
        // unreachable, which deploys perfectly well and never runs.
        Assert.Equal("Route the invoice", task.Attribute("name")!.Value);
        Assert.Equal("f0", task.Element(Bpmn + "incoming")!.Value);
        Assert.Equal("f1", task.Element(Bpmn + "outgoing")!.Value);
    }

    [Fact]
    public void Expanding_twice_produces_the_same_document()
    {
        // Publish runs the whole expansion chain every time. A second pass that
        // rewrote its own output would make each publish a different diagram --
        // and would append a second identical field extension here, which the
        // engine would read as a duplicate rather than an error.
        var once = WorkflowBpmnXml.ExpandForDeployment(Diagram("invoiceRouting"));
        var twice = WorkflowBpmnXml.ExpandForDeployment(once);

        Assert.Equal(once, twice);
        Assert.Single(XDocument.Parse(twice).Descendants(Flowable + "field"));
    }

    [Fact]
    public void The_STORED_diagram_still_holds_the_business_rule_task()
    {
        // The expansion is on the deploy path only. If it ran at prepare, the
        // studio would save what prepare returned and the author's business rule
        // task would become a service task in their own diagram -- losing both
        // the shape they drew and the autonate:decisionKey this expansion reads.
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(
            Diagram("invoiceRouting"), "p", "Routing", []);

        var stored = XDocument.Parse(prepared);
        Assert.Single(stored.Descendants(Bpmn + "businessRuleTask"));
        Assert.Empty(stored.Descendants(Bpmn + "serviceTask"));
        Assert.Equal("invoiceRouting",
            stored.Descendants(Bpmn + "businessRuleTask").Single()
                .Attribute(AutoNate + "decisionKey")!.Value);
    }

    [Fact]
    public void A_task_with_no_table_is_refused_at_prepare_naming_the_element()
    {
        var errors = WorkflowBpmnXml.ValidateProcess(Diagram(decisionKey: null)).Errors;

        var error = Assert.Single(errors, e => e.Contains("decision table", StringComparison.Ordinal));
        // Named, because "validation failed" on a forty-element diagram tells an
        // author nothing about which one to open.
        Assert.Contains("Route the invoice", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unconfigured_task_is_LEFT_as_a_business_rule_task_rather_than_half_expanded()
    {
        // Reaching the expansion unconfigured means validation was bypassed. The
        // choice then is between a service task that deploys and decides nothing,
        // and a business rule task that fails the deployment loudly. The second is
        // more diagnosable, so the expansion backs out rather than half-applying.
        var expanded = XDocument.Parse(
            WorkflowBpmnXml.ExpandForDeployment(Diagram(decisionKey: null)));

        Assert.Single(expanded.Descendants(Bpmn + "businessRuleTask"));
        Assert.Empty(expanded.Descendants(Bpmn + "serviceTask"));
        Assert.Empty(expanded.Descendants(Flowable + "field"));
    }

    [Fact]
    public void The_deployed_task_is_async_so_a_failed_evaluation_is_visible()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram("invoiceRouting")));

        // MEASURED, both ways, on the live engine. Synchronously a failing
        // decision throws out of the start call -- HTTP 500, transaction rolled
        // back, no instance and no history for anyone to open. Asynchronously it
        // becomes a job carrying the engine's message, which is the surface every
        // other failing step already lands in (#172).
        Assert.Equal(
            "true",
            expanded.Descendants(Bpmn + "serviceTask").Single()
                .Attribute(Flowable + "async")?.Value);
    }

    // ── Version binding (#111) ──────────────────────────────────────────────

    [Fact]
    public void Every_configured_task_is_reported_with_its_element_id_label_and_key()
    {
        var found = WorkflowBpmnXml.ExtractBusinessRuleTaskDecisions(Diagram("invoiceRouting"));

        var one = Assert.Single(found);
        Assert.Equal("decide", one.ElementId);
        // The label, because the publish refusal has to name something an author
        // can find on the canvas.
        Assert.Equal("Route the invoice", one.Label);
        Assert.Equal("invoiceRouting", one.DecisionKey);
    }

    [Fact]
    public void An_unconfigured_task_is_NOT_reported_as_a_reference_to_resolve()
    {
        // Its refusal is BuildBusinessRuleTaskErrors'. Reporting it here too would
        // hand an author two different sentences about one element.
        Assert.Empty(WorkflowBpmnXml.ExtractBusinessRuleTaskDecisions(Diagram(decisionKey: null)));
    }

    [Fact]
    public void Publish_pins_the_deployed_copy_to_the_version_published_now()
    {
        var pinned = WorkflowBpmnXml.PinBusinessRuleTaskDecisions(
            Diagram("invoiceRouting"),
            new Dictionary<string, string> { ["invoiceRouting"] = "invoiceRouting-v3" });

        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(pinned));

        Assert.Equal(
            "invoiceRouting-v3",
            expanded.Descendants(Flowable + "field")
                .Single(f => (string?)f.Attribute("name") == "decisionTableReferenceKey")
                .Value);

        // AND THE STORED DIAGRAM IS UNTOUCHED. Pinning applies to the deployed
        // copy; an author reopening this workflow must still see the table they
        // picked, and the next publish must resolve the key afresh rather than
        // re-pinning a pin.
        Assert.Equal(
            "invoiceRouting",
            XDocument.Parse(Diagram("invoiceRouting"))
                .Descendants(Bpmn + "businessRuleTask").Single()
                .Attribute(AutoNate + "decisionKey")!.Value);
    }

    [Fact]
    public void A_key_the_publish_did_not_resolve_is_left_exactly_as_written()
    {
        // The complement of the test above, and the one that matters: a pin map
        // that silently rewrote keys it had no entry for would bind processes to
        // tables nobody resolved. Publish refuses an unresolved key before it
        // reaches here, so anything left over must pass through unchanged.
        var pinned = WorkflowBpmnXml.PinBusinessRuleTaskDecisions(
            Diagram("invoiceRouting"),
            new Dictionary<string, string> { ["somethingElse"] = "somethingElse-v1" });

        Assert.Equal(
            "invoiceRouting",
            XDocument.Parse(pinned).Descendants(Bpmn + "businessRuleTask").Single()
                .Attribute(AutoNate + "decisionKey")!.Value);
    }

    [Fact]
    public void A_pinned_key_names_the_table_and_the_version()
    {
        Assert.Equal("invoiceRouting-v3", DecisionTableDmn.PinnedKey("invoiceRouting", 3));
    }

    [Fact]
    public void No_key_an_author_can_write_can_collide_with_a_pinned_one()
    {
        // The reason the separator is a hyphen and not `__v`. With `__v`, a table
        // genuinely called `invoiceRouting__v1` would occupy the pinned name of
        // `invoiceRouting` version 1 -- and `EnsureDecisionAsync`, finding a
        // decision already deployed under that key, would bind the process to the
        // wrong table without deploying anything or saying a word.
        //
        // Asserted through the VALIDATOR rather than against a regex copied here,
        // so tightening or loosening the key rule moves this test with it.
        var pinned = DecisionTableDmn.PinnedKey("invoiceRouting", 1);

        var collidingTable = new DecisionTableModel
        {
            DecisionKey = pinned,
            Name = "Collision",
            HitPolicy = "FIRST",
            Inputs = [new DecisionColumn("i1", "Amount", "amount", "number")],
            Outputs = [new DecisionColumn("o1", "Route", "route", "string")],
            Rules = [new DecisionRule("r1", [""], ["\"escalate\""])]
        };

        Assert.Contains(
            DecisionTableValidator.Validate(collidingTable),
            e => e.Contains("not a usable decision key", StringComparison.Ordinal));
    }

    [Fact]
    public void Rekeying_a_published_version_changes_its_identity_and_nothing_else()
    {
        var table = new DecisionTableModel
        {
            Id = Guid.NewGuid(),
            DecisionKey = "invoiceRouting",
            Name = "Invoice routing",
            HitPolicy = "FIRST",
            Inputs = [new DecisionColumn("i1", "Amount", "amount", "number")],
            Outputs = [new DecisionColumn("o1", "Route", "route", "string")],
            Rules = [new DecisionRule("r1", ["> 10000"], ["\"escalate\""])]
        };

        var rekeyed = XDocument.Parse(
            DecisionTableDmn.Rekey(DecisionTableDmn.Generate(table), "invoiceRouting-v3"));

        var dmn = rekeyed.Root!.Name.Namespace;
        var decision = rekeyed.Root.Elements(dmn + "decision").Single();

        // Flowable takes the decision KEY from this attribute, so it is the whole
        // mechanism. `defs_` and `dt_` move with it only because two deployments
        // sharing an id is the sort of thing that works until it does not.
        Assert.Equal("invoiceRouting-v3", (string?)decision.Attribute("id"));
        Assert.Equal("defs_invoiceRouting-v3", (string?)rekeyed.Root.Attribute("id"));
        Assert.Equal(
            "dt_invoiceRouting-v3",
            (string?)decision.Elements(dmn + "decisionTable").Single().Attribute("id"));

        // AND THE RULES ARE THE SAME RULES. A re-key that touched a cell would
        // bind a process to a table deciding something nobody published, which is
        // the failure version binding exists to prevent arriving by the back door.
        var entries = decision.Descendants(dmn + "text").Select(t => t.Value).ToList();
        Assert.Contains("> 10000", entries);
        Assert.Contains("\"escalate\"", entries);
        Assert.Contains("amount", entries);
    }
}
