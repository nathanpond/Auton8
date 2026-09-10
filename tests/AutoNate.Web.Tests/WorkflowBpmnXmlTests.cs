using AutoNate.Web.Services.Workflow;
using System.Xml.Linq;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WorkflowBpmnXmlTests
{
    // ── #244: a scoped catch must not narrow a signal start sharing its signal ──

    [Fact]
    public void ValidateProcess_RefusesOneSignalNameAskedToCarryTwoScopes()
    {
        // #270. This replaces a test that asserted the XML TREE and never
        // deployed it — and the tree it pinned does not deploy.
        //
        // #244 cloned the <bpmn:signal> root for the scoped catch so a signal
        // start event sharing the name kept its global subscription. The clone
        // carried the same NAME, and Flowable 8.0.0 refuses that outright:
        //   [Problem: 'flowable-signal-duplicate-name'] : Duplicate signal name
        //   found  -> HTTP 500
        // So the diagram #244 existed to support became unpublishable, and the
        // shape test stayed green because it compared elements instead of asking
        // an engine.
        //
        // Scope lives on the root, so one name means one scope. A start event
        // must be global to start instances from outside; an instance-scoped
        // catch on the same name is a contradiction no engine can honour, and
        // the author is the only one who can resolve it.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="record.created" flowable:topic="record.events" />
              <bpmn:process id="both" name="Both" isExecutable="true">
                <bpmn:startEvent id="s" name="On record created"><bpmn:signalEventDefinition signalRef="Sig_1" /></bpmn:startEvent>
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="wait" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait for record">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="instance" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("record.created", StringComparison.Ordinal));

        // Names BOTH events, because the author has to choose between them.
        Assert.Contains("Wait for record", error, StringComparison.Ordinal);
        Assert.Contains("On record created", error, StringComparison.Ordinal);
        Assert.Contains("one scope per signal name", error, StringComparison.Ordinal);

        // And the expansion emits nothing the engine would reject: exactly one
        // <bpmn:signal> root survives, so even if validation were bypassed the
        // deployment would not fail with a duplicate-name 500.
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        var roots = expanded.Descendants(Bpmn218 + "signal").ToList();
        Assert.Single(roots);
        Assert.Equal("record.created", roots[0].Attribute("name")?.Value);
    }

    [Fact]
    public void ValidateProcess_AcceptsOneSignalNameEveryEventScopesTheSameWay()
    {
        // The complement. Without it a rule that refused every scoped signal
        // would pass the test above while breaking #156 entirely.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="record.created" />
              <bpmn:process id="scoped" name="Scoped" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="wait" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait for record">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="instance" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        // Still scoped, and still one root.
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        XNamespace flowable = "http://flowable.org/bpmn";
        var root = Assert.Single(expanded.Descendants(Bpmn218 + "signal"));
        Assert.Equal("processInstance", root.Attribute(flowable + "scope")?.Value);
    }

    // ── #242: errors match by CODE, not by the id of their <bpmn:error> root ──

    private static string TwoErrorRoots(string boundaryRef) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_A" errorCode="E_SAME" name="Same" />
          <bpmn:error id="Err_B" errorCode="E_SAME" name="Same" />
          <bpmn:error id="Err_C" errorCode="E_OTHER" name="Other" />
          <bpmn:process id="dup" name="Dup" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
            <bpmn:subProcess id="sub" name="Inner">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="boom" />
              <bpmn:endEvent id="boom" name="Give up">
                <bpmn:errorEventDefinition errorRef="Err_A" />
              </bpmn:endEvent>
            </bpmn:subProcess>
            <bpmn:boundaryEvent id="catch" attachedToRef="sub">
              <bpmn:errorEventDefinition errorRef="{boundaryRef}" />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="catch" targetRef="handled" />
            <bpmn:userTask id="handled" name="Handled" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ValidateProcess_MatchesErrorsByCodeAcrossDifferentErrorRoots()
    {
        // Err_A and Err_B are different roots carrying the SAME errorCode.
        // BPMN and Flowable match on the code, so this diagram runs correctly and
        // must publish. Comparing ref ids refused it.
        Assert.DoesNotContain(WorkflowBpmnXml.ValidateProcess(TwoErrorRoots("Err_B")).Errors, e =>
            e.Contains("Give up", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_StillRefusesAThrownCodeNothingCatches()
    {
        // The complement: resolving to codes must not make everything match.
        // Err_C is a genuinely different code and must still be refused.
        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(TwoErrorRoots("Err_C")).Errors,
            e => e.Contains("Give up", StringComparison.Ordinal));

        // And the message quotes the CODE the author typed, not the ref id.
        Assert.Contains("E_SAME", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Err_A", error, StringComparison.Ordinal);
    }

    // ── #239: a route flow's own condition can defeat the route contract ─────

    private static string GatewayWithRouteCondition(string? conditionOnFa) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="router" name="Router" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="cg" />
            <bpmn:complexGateway id="cg" name="Choose" default="fd"
                                 autonate:routeScript="return 'fa';" />
            <bpmn:sequenceFlow id="fa" name="Approve" sourceRef="cg" targetRef="ta">
              {conditionOnFa}
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="fd" sourceRef="cg" targetRef="td" />
            <bpmn:userTask id="ta" name="Route A" />
            <bpmn:userTask id="td" name="Default" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ValidateProcess_RefusesAnAuthorConditionOnAComplexGatewaysRoute()
    {
        // Without this the contract accepts 'fa', the author's own condition is
        // false, and the engine quietly takes the default. Nothing fails, nothing
        // is logged, and the process went somewhere the script did not choose.
        var result = WorkflowBpmnXml.ValidateProcess(GatewayWithRouteCondition(
            "<bpmn:conditionExpression xsi:type=\"bpmn:tFormalExpression\">${1 == 2}</bpmn:conditionExpression>"));

        var error = Assert.Single(result.Errors, e => e.Contains("Approve", StringComparison.Ordinal));
        Assert.Contains("Choose", error, StringComparison.Ordinal);
        // The message has to name the way out, or an author is stuck with a
        // diagram and a prohibition.
        Assert.Contains("exclusive gateway", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProcess_AcceptsAComplexGatewayWhoseRoutesCarryNoConditions()
    {
        // The complement: the ordinary shape must still publish. A rule that
        // refused every complex gateway would satisfy the test above and remove
        // the feature.
        Assert.DoesNotContain(WorkflowBpmnXml.ValidateProcess(GatewayWithRouteCondition("")).Errors, e =>
            e.Contains("has its own condition", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_LeavesTheDefaultFlowFreeToCarryNoCondition()
    {
        // The default flow is excluded from the routes offered to the script, so
        // it is not part of this reconciliation — and BPMN forbids a condition on
        // it anyway. Pinned so a later tightening does not start refusing it.
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(
            GatewayWithRouteCondition("")));

        Assert.Equal("fa", document.Descendants(Bpmn218 + "scriptTask").Single()
            .Attribute(Flowable218 + "autonateAllowedRoutes")?.Value);
    }

    // ── #240: an association is not necessarily a compensation association ────

    [Fact]
    public void ValidateProcess_DoesNotTreatAnAnnotatedUserTaskAsACompensationHandler()
    {
        // The rule collected every association's target, and bpmn-js uses an
        // association to attach a TEXT ANNOTATION. So annotating an ordinary user
        // task made the whole diagram unpublishable — and, since prepare's errors
        // also block save, unsaveable.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="annotated" name="Annotated" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
                <bpmn:userTask id="t" name="Approve the invoice" />
                <bpmn:textAnnotation id="note"><bpmn:text>Check the totals</bpmn:text></bpmn:textAnnotation>
                <bpmn:association id="a1" sourceRef="note" targetRef="t" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.DoesNotContain(WorkflowBpmnXml.ValidateProcess(xml).Errors, e =>
            e.Contains("compensation handler", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_StillRefusesAWaitingHandlerOnARealCompensationAssociation()
    {
        // The complement of the fix: narrowing to compensation boundary events
        // must not disarm the rule. Flowable fails its own transaction on a
        // waiting handler, so this refusal has to survive.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="undo" name="Undo" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
                <bpmn:userTask id="t1" name="Take payment" />
                <bpmn:boundaryEvent id="b1" attachedToRef="t1">
                  <bpmn:compensateEventDefinition />
                </bpmn:boundaryEvent>
                <bpmn:userTask id="h1" name="Refund" isForCompensation="true" />
                <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Contains(WorkflowBpmnXml.ValidateProcess(xml).Errors, e =>
            e.Contains("Refund", StringComparison.Ordinal)
            && e.Contains("waits", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("subProcess")]
    [InlineData("callActivity")]
    public void ValidateProcess_RefusesAContainerHandlerBecauseItCanWaitToo(string localName)
    {
        // The pair userTask/receiveTask was too narrow the other way: a
        // subprocess or call activity handler can contain a user task, so it
        // waits and hits the same engine crash.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="undo" name="Undo" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
                <bpmn:userTask id="t1" name="Take payment" />
                <bpmn:boundaryEvent id="b1" attachedToRef="t1">
                  <bpmn:compensateEventDefinition />
                </bpmn:boundaryEvent>
                <bpmn:{localName} id="h1" name="Undo it" isForCompensation="true" />
                <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Contains(WorkflowBpmnXml.ValidateProcess(xml).Errors, e =>
            e.Contains("Undo it", StringComparison.Ordinal));
    }

    // ── #166: what a child declares, for a parent's mapping UI ───────────────

    private const string DeclaringChild = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="child" name="Child" isExecutable="true">
            <bpmn:dataObject id="amountObj" name="amount" autonate:dataType="xsd:double" />
            <bpmn:dataObjectReference id="amountRef" name="amount" dataObjectRef="amountObj" />
            <bpmn:dataStoreReference id="ledger" name="ledger" />
            <bpmn:ioSpecification id="io">
              <bpmn:dataInput id="in1" name="orderId" />
              <bpmn:dataOutput id="out1" name="receiptId" />
            </bpmn:ioSpecification>
            <bpmn:startEvent id="s" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ExtractDataDeclarations_ReportsWhatTheChildDeclares_WithoutDuplicates()
    {
        var declarations = WorkflowBpmnXml.ExtractDataDeclarations(DeclaringChild);

        // A data object and the reference pointing at it are ONE declaration to an
        // author — offering `amount` twice in a mapping list is a bug, not detail.
        var amount = Assert.Single(declarations, d => d.Name == "amount");
        Assert.Equal("xsd:double", amount.Type);
        Assert.Equal("variable", amount.Kind);

        // Inputs and outputs are distinguishable, so a parent can offer the
        // child's inputs as mapping TARGETS and its outputs as SOURCES rather
        // than one undifferentiated list.
        Assert.Equal("input", Assert.Single(declarations, d => d.Name == "orderId").Kind);
        Assert.Equal("output", Assert.Single(declarations, d => d.Name == "receiptId").Kind);
        Assert.Equal("variable", Assert.Single(declarations, d => d.Name == "ledger").Kind);
    }

    [Fact]
    public void ExtractDataDeclarations_ReadsTheDeployedSpellingToo()
    {
        // A child imported from another modeller carries itemSubjectRef rather
        // than the studio's attribute, and its declarations are just as real.
        var xml = DeclaringChild.Replace(
            "autonate:dataType=\"xsd:double\"", "itemSubjectRef=\"xsd:double\"", StringComparison.Ordinal);

        Assert.Equal("xsd:double",
            Assert.Single(WorkflowBpmnXml.ExtractDataDeclarations(xml), d => d.Name == "amount").Type);
    }

    [Fact]
    public void ExtractDataDeclarations_IsEmptyForAProcessThatDeclaresNothing()
    {
        // The complement: a child with no declarations must return nothing rather
        // than inventing entries, because the mapping UI falls back to free text
        // and an empty list is what tells it to.
        const string bare = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="D" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="bare" isExecutable="true"><bpmn:startEvent id="s" /></bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Empty(WorkflowBpmnXml.ExtractDataDeclarations(bare));
    }

    [Fact]
    public void ApplyProcessMetadata_KeepsAutoNateAttributesOnDataAndMarkers()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="P" isExecutable="true">
                <bpmn:dataObject id="d" name="amount" />
                <bpmn:dataObjectReference id="dr" name="amount" dataObjectRef="d"
                                          autonate:dataType="xsd:double" />
                <bpmn:userTask id="t" name="Handle">
                  <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                      flowable:collection="${orders}" flowable:elementVariable="item"
                      autonate:completionCondition="${done}" />
                </bpmn:userTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        // WITH snapshots, which is how the studio actually calls it — the studio
        // sends one per element, and a handler that clears what a snapshot omits
        // is exactly how an attribute survives export and still vanishes.
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(xml, "p", "P",
        [
            new WorkflowElementSnapshot("dr", "bpmn:DataObjectReference", "amount"),
            new WorkflowElementSnapshot("t", "bpmn:UserTask", "Handle")
        ]);

        Assert.Contains("dataType", prepared, StringComparison.Ordinal);
        Assert.Contains("collection", prepared, StringComparison.Ordinal);
        Assert.Contains("completionCondition", prepared, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandForDeployment_TurnsAnAuthoredCompletionConditionIntoTheChildElement()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="casework" isExecutable="true">
                <bpmn:adHocSubProcess id="adhoc" name="Case work"
                                      autonate:completionCondition="${done == true}">
                  <bpmn:userTask id="a1" name="Call" />
                </bpmn:adHocSubProcess>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        var adhoc = document.Descendants(Bpmn218 + "adHocSubProcess").Single();

        var condition = Assert.Single(adhoc.Elements(Bpmn218 + "completionCondition"));
        Assert.Equal("${done == true}", condition.Value);
        Assert.Null(adhoc.Attribute(Autonate218 + "completionCondition"));

        // The child must come AFTER every flow element. The strict schema puts it
        // last, and a deployment with it first is refused outright:
        //   cvc-complex-type.2.4.d: Invalid content was found starting with
        //   element 'completionCondition'
        Assert.Equal("completionCondition", adhoc.Elements().Last().Name.LocalName);
    }

    [Fact]
    public void ExpandForDeployment_LeavesAHandWrittenCompletionConditionAlone()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="casework" isExecutable="true">
                <bpmn:adHocSubProcess id="adhoc" autonate:completionCondition="${ignored}">
                  <bpmn:userTask id="a1" name="Call" />
                  <bpmn:completionCondition xsi:type="bpmn:tFormalExpression">${mine == true}</bpmn:completionCondition>
                </bpmn:adHocSubProcess>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        var adhoc = document.Descendants(Bpmn218 + "adHocSubProcess").Single();

        // An imported diagram that already spells it the engine's way meant it —
        // and must not end up with two conditions.
        var condition = Assert.Single(adhoc.Elements(Bpmn218 + "completionCondition"));
        Assert.Equal("${mine == true}", condition.Value);
    }

    [Fact]
    public void ValidateProcess_AcceptsAnAdhocCompletionConditionWrittenAsAnAttribute()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="casework" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="adhoc" />
                <bpmn:adHocSubProcess id="adhoc" name="Case work"
                                      autonate:completionCondition="${done == true}">
                  <bpmn:userTask id="a1" name="Call" />
                </bpmn:adHocSubProcess>
              </bpmn:process>
            </bpmn:definitions>
            """;

        // The studio's spelling must satisfy the "needs a completion condition"
        // rule, or an author who sets one in the panel is refused for not having
        // set one.
        Assert.DoesNotContain(WorkflowBpmnXml.ValidateProcess(xml).Errors, e =>
            e.Contains("never finish", StringComparison.Ordinal));
    }

    // ── #159: multi-instance and the loop marker ─────────────────────────────

    private static string WithMarker(string marker) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="each" name="Each" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve">
              {marker}
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ValidateProcess_RefusesTheLoopMarker_BecauseTheEngineIgnoresIt()
    {
        // Not a hang, which is what the story assumed — a SILENT NO-OP. Measured
        // against a control on the same task: every standardLoopCharacteristics
        // spelling ran the activity once, while multi-instance cardinality 3 ran
        // it three times. The author marks a loop and gets one iteration.
        var result = WorkflowBpmnXml.ValidateProcess(
            WithMarker("<bpmn:standardLoopCharacteristics loopMaximum=\"3\" />"));

        // Refused through #107's manifest mechanism rather than a rule of its own.
        // The first version of this added a second check and produced TWO errors
        // for one problem — the manifest already refuses anything the engine
        // cannot run, and the row's reason is the message.
        var error = Assert.Single(result.Errors, e => e.Contains("Approve", StringComparison.Ordinal));
        Assert.Contains("Loop Marker", error, StringComparison.Ordinal);

        // Refusing without saying what to use instead leaves an author stuck with
        // a diagram and no way forward, so the row's reason names the remedy.
        Assert.Contains("multi-instance", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateProcess_AcceptsAMultiInstanceMarker()
    {
        // The complement, and the one that matters most here: the remedy the
        // refusal above points at must actually publish. A rule that caught both
        // markers would pass the test above and leave an author nowhere to go.
        var result = WorkflowBpmnXml.ValidateProcess(WithMarker(
            "<bpmn:multiInstanceLoopCharacteristics isSequential=\"true\" " +
            "flowable:collection=\"${items}\" flowable:elementVariable=\"item\" />"));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Approve", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_ChecksAMultiInstanceCompletionConditionWithTheSharedRule()
    {
        var result = WorkflowBpmnXml.ValidateProcess(WithMarker(
            "<bpmn:multiInstanceLoopCharacteristics isSequential=\"true\">" +
            "<bpmn:completionCondition xsi:type=\"bpmn:tFormalExpression\">${nrOfCompletedInstances >=</bpmn:completionCondition>" +
            "</bpmn:multiInstanceLoopCharacteristics>"));

        // Through WorkflowConditionValidation's own site list, not a second
        // expression parser — two implementations of "is this condition valid"
        // drift apart, and the one nobody maintains is the one that lets a hang
        // through.
        Assert.Contains(result.Errors.Concat(result.Warnings), m =>
            m.Contains("Approve", StringComparison.Ordinal)
            && m.Contains("completion condition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_WarnsWhenAMultiInstanceCollectionIsNeverSet()
    {
        // Reuses the existing unset-variable rule rather than adding a second
        // one: a collection nothing sets produces no instances, silently.
        var result = WorkflowBpmnXml.ValidateProcess(WithMarker(
            "<bpmn:multiInstanceLoopCharacteristics isSequential=\"true\" " +
            "flowable:collection=\"${nobodySetsThis}\" flowable:elementVariable=\"item\" />"));

        Assert.Contains(result.Warnings, w =>
            w.Contains("nobodySetsThis", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandForDeployment_RewritesADataObjectsTypeIntoTheFormTheEngineReads()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="amounts" name="Amounts" isExecutable="true">
                <bpmn:dataObject id="amountObj" name="amount" autonate:dataType="xsd:double" />
                <bpmn:startEvent id="s" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        var dataObject = document.Descendants(Bpmn218 + "dataObject").Single();

        // The bare QName is the ONLY form the engine types the variable from —
        // an itemDefinition indirection deploys and silently produces `string`.
        Assert.Equal("xsd:double", dataObject.Attribute("itemSubjectRef")?.Value);

        // And the authoring attribute is gone from the deployed copy, having
        // done its job.
        Assert.Null(dataObject.Attribute(Autonate218 + "dataType"));

        // And the xsd prefix is DECLARED. itemSubjectRef holds a QName, so
        // without this the deployment is refused outright —
        //   UndeclaredPrefix: Cannot resolve 'xsd:double' as a QName
        // — and a studio-authored diagram never carries xmlns:xsd of its own.
        Assert.Equal("http://www.w3.org/2001/XMLSchema",
            document.Root!.Attribute(XNamespace.Xmlns + "xsd")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_DoesNotOverwriteAHandWrittenItemSubjectRef()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="amounts" isExecutable="true">
                <bpmn:dataObject id="a" name="amount"
                                 itemSubjectRef="xsd:string" autonate:dataType="xsd:double" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));

        // An imported diagram that already carries the engine's own spelling
        // meant it. Overwriting would change what someone else's diagram does.
        Assert.Equal("xsd:string",
            document.Descendants(Bpmn218 + "dataObject").Single().Attribute("itemSubjectRef")?.Value);
    }

    // ── #166: data objects declare variables ─────────────────────────────────
    //
    // Verified against Flowable 8.0.0 before these were written: a <dataObject>
    // creates a REAL process variable with its declared type (`amount = 42.5,
    // type=double`), and a condition reads it. So these are declarations, not
    // decoration — which is what the story turns on.

    // Held apart from the interpolated literal: an EL expression is all braces,
    // and escaping them inside a raw interpolated string is how this file first
    // failed to compile.
    private const string Condition166 = "${amount > 100}";

    private static string WithDataObject(bool declared) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="amounts" name="Amounts" isExecutable="true">
            {(declared
              ? "<bpmn:dataObject id=\"amountObj\" name=\"amount\" itemSubjectRef=\"xsd:double\" />"
              : "")}
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="g" />
            <bpmn:exclusiveGateway id="g" default="fno" />
            <bpmn:sequenceFlow id="fyes" sourceRef="g" targetRef="tyes">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">{Condition166}</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="fno" sourceRef="g" targetRef="tno" />
            <bpmn:userTask id="tyes" name="Big" />
            <bpmn:userTask id="tno" name="Small" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void A_condition_reading_a_declared_data_object_is_not_warned_about()
    {
        // This is the link that makes a declaration worth making. Without it the
        // data object is a drawable box, which is what it was.
        var result = WorkflowBpmnXml.ValidateProcess(WithDataObject(declared: true));

        Assert.DoesNotContain(result.Warnings, w =>
            w.Contains("amount", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_condition_is_warned_about_when_the_declaration_is_removed()
    {
        // The complement, and the pair is what makes either half meaningful: a
        // validator that never warns would satisfy the test above. Synthesising
        // the failure is removing the declaration and nothing else.
        var result = WorkflowBpmnXml.ValidateProcess(WithDataObject(declared: false));

        Assert.Contains(result.Warnings, w =>
            w.Contains("amount", StringComparison.Ordinal));
    }

    // ── #163: ad-hoc subprocess ──────────────────────────────────────────────

    private static string Adhoc(string? completionCondition) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="casework" name="Case work" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="adhoc" />
            <bpmn:adHocSubProcess id="adhoc" name="Case work">
              <bpmn:userTask id="a1" name="Call the customer" />
              {(completionCondition is null
                ? ""
                : $"<bpmn:completionCondition xsi:type=\"bpmn:tFormalExpression\">{completionCondition}</bpmn:completionCondition>")}
            </bpmn:adHocSubProcess>
            <bpmn:sequenceFlow id="f1" sourceRef="adhoc" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ValidateProcess_RefusesAnAdhocSubProcessWithNoCompletionCondition()
    {
        // Flowable deploys this happily and the instance then sits in the
        // subprocess forever with the parent unable to continue. A hang, not a
        // feature — which is the line epic #40 draws.
        var result = WorkflowBpmnXml.ValidateProcess(Adhoc(null));

        var error = Assert.Single(result.Errors, e => e.Contains("Case work", StringComparison.Ordinal));
        Assert.Contains("never finish", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProcess_AcceptsAnAdhocSubProcessWithACompletionCondition()
    {
        // The complement: a rule that refused every ad-hoc subprocess would
        // satisfy the test above and make the element unusable.
        var result = WorkflowBpmnXml.ValidateProcess(Adhoc("${done == true}"));

        Assert.DoesNotContain(result.Errors, e =>
            e.Contains("ad-hoc subprocess", StringComparison.OrdinalIgnoreCase));
    }

    // #115/#225. The guard for a regression that already happened once and that
    // nothing caught: #225 pointed publish at ValidateProcess, which did not
    // contain the promoted structure rules, so three of them silently stopped
    // running — among them #114's uncaught error code, whose runtime consequence
    // is Flowable destroying the instance with a 500 and no history.
    //
    // Asserting the two sets agree, rather than listing the rules, is what makes
    // this survive the next rule being added to either one.
    [Fact]
    public void ValidateProcess_IncludesEveryRulePromotedToPublish()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:error id="Err_1" errorCode="NOBODY_CATCHES_THIS" name="Orphan" />
              <bpmn:process id="orphan" name="Orphan" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="boom" />
                <bpmn:endEvent id="boom" name="Give up">
                  <bpmn:errorEventDefinition errorRef="Err_1" />
                </bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var promoted = WorkflowBpmnXml.ValidateStructureForPublish(xml);
        var full = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        // The fixture has to actually trip a promoted rule, or the assertion
        // below is comparing two empty sets and proves nothing.
        Assert.NotEmpty(promoted);
        Assert.All(promoted, e => Assert.Contains(e, full));
    }

    // ── #115: compensation ───────────────────────────────────────────────────

    private const string CompensationXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="undo" name="Undo" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
            <bpmn:task id="t1" name="Take payment" />
            <bpmn:sequenceFlow id="f1" sourceRef="t1" targetRef="done" />
            <bpmn:endEvent id="done" name="Undo everything">
              <bpmn:compensateEventDefinition />
            </bpmn:endEvent>
            <bpmn:boundaryEvent id="b1" attachedToRef="t1">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:task id="h1" name="Refund" isForCompensation="true" />
            <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ExpandForDeployment_TurnsACompensationEndEvent_IntoAThrowFollowedByAnEnd()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(CompensationXml));

        // Verified against Flowable 8.0.0: the END form ends the process and runs
        // NO handler — the recorded trail came back empty. The intermediate throw
        // form works, waits, and compensates in reverse order. So the deployed
        // copy carries the form the engine actually runs.
        Assert.Empty(document.Descendants(Bpmn218 + "endEvent")
            .Where(e => e.Elements(Bpmn218 + "compensateEventDefinition").Any()));

        var thrown = Assert.Single(document.Descendants(Bpmn218 + "intermediateThrowEvent"));
        // The ORIGINAL id survives, which is what keeps every inbound flow and
        // diagram shape pointing at it valid.
        Assert.Equal("done", thrown.Attribute("id")?.Value);
        Assert.NotNull(thrown.Element(Bpmn218 + "compensateEventDefinition"));

        // And the process still terminates, through a generated none end event.
        var terminal = Assert.Single(document.Descendants(Bpmn218 + "endEvent"));
        Assert.Equal("done_end", terminal.Attribute("id")?.Value);
        Assert.Empty(terminal.Elements());

        var flow = document.Descendants(Bpmn218 + "sequenceFlow")
            .Single(f => f.Attribute("id")?.Value == "done_end_flow");
        Assert.Equal("done", flow.Attribute("sourceRef")?.Value);
        Assert.Equal("done_end", flow.Attribute("targetRef")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_PutsGeneratedNodesBeforeTheDiagramsArtifacts()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(CompensationXml));
        var process = document.Descendants(Bpmn218 + "process").Single();

        var children = process.Elements().Select(e => e.Name.LocalName).ToList();
        var lastFlowElement = children.FindLastIndex(n => n != "association");
        var firstArtifact = children.IndexOf("association");

        // The strict BPMN schema puts artifacts after every flow element, and
        // Flowable validates against it — appending the generated end event after
        // the associations is refused outright:
        //   cvc-complex-type.2.4.a: Invalid content was found starting with
        //   element 'endEvent'. One of '{artifact, ...}' is expected.
        // A 500 at publish for every diagram with a compensation association.
        Assert.True(lastFlowElement < firstArtifact,
            $"Generated nodes must precede artifacts; order was: {string.Join(", ", children)}");
    }

    [Fact]
    public void ExpandForDeployment_IsIdempotent_ForCompensationEndEvents()
    {
        var twice = WorkflowBpmnXml.ExpandForDeployment(
            WorkflowBpmnXml.ExpandForDeployment(CompensationXml));
        var document = XDocument.Parse(twice);

        // Publishing twice must not chain a second terminal event onto the first.
        Assert.Single(document.Descendants(Bpmn218 + "endEvent"));
        Assert.Single(document.Descendants(Bpmn218 + "intermediateThrowEvent"));
    }

    [Fact]
    public void ExpandForDeployment_LeavesTheHandlerAndItsAssociationAlone()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(CompensationXml));

        // The association IS the link from boundary event to handler. An
        // expansion that dropped or rewired it would leave a handler nothing can
        // reach, and the process would compensate silently nothing at all —
        // which is the exact failure being fixed.
        var association = Assert.Single(document.Descendants(Bpmn218 + "association"));
        Assert.Equal("b1", association.Attribute("sourceRef")?.Value);
        Assert.Equal("h1", association.Attribute("targetRef")?.Value);

        var handler = document.Descendants(Bpmn218 + "task")
            .Single(t => t.Attribute("id")?.Value == "h1");
        Assert.Equal("true", handler.Attribute("isForCompensation")?.Value);
    }

    [Fact]
    public void ValidateProcess_RefusesACompensationHandlerThatWaits()
    {
        // Refused, not warned. Flowable cannot run a waiting compensation
        // handler: triggered during a user task's completion it fails the
        // engine's own transaction with an act_fk_exe_parent violation, and the
        // task can then never be completed. Reproduced against a bare Flowable
        // and isolated — only making the handlers automatic fixes it.
        var xml = CompensationXml.Replace(
            "<bpmn:task id=\"h1\" name=\"Refund\" isForCompensation=\"true\" />",
            "<bpmn:userTask id=\"h1\" name=\"Refund\" isForCompensation=\"true\" />",
            StringComparison.Ordinal);
        Assert.Contains("userTask", xml, StringComparison.Ordinal);

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        var error = Assert.Single(result.Errors, e => e.Contains("Refund", StringComparison.Ordinal));
        // The message has to say what to do instead, because "not supported"
        // leaves an author with a diagram and no way forward.
        Assert.Contains("automatic step", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProcess_AcceptsAnAutomaticCompensationHandler()
    {
        // The complement, and the one that matters: a rule that refused every
        // handler would satisfy the test above and make compensation unusable.
        // Verified against the engine — automatic handlers run correctly, in
        // reverse order, and the throw waits for them.
        var result = WorkflowBpmnXml.ValidateProcess(CompensationXml);

        Assert.DoesNotContain(result.Errors, e =>
            e.Contains("compensation handler", StringComparison.OrdinalIgnoreCase));
    }

    // ── #218: the complex gateway expansion ──────────────────────────────────
    //
    // CI excludes engine-backed specs, so these carry the expansion's guarantees
    // without Flowable. What they cannot check is that the engine routes on the
    // conditions written here; that is verified separately and recorded on #218.

    private const string ComplexGatewayXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="router" name="Router" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="cg" />
            <bpmn:complexGateway id="cg" name="Choose"
                                 autonate:scriptFormat="javascript"
                                 autonate:routeScript="return 'fa';"
                                 autonate:runAs="system" />
            <bpmn:sequenceFlow id="fa" sourceRef="cg" targetRef="ta" />
            <bpmn:sequenceFlow id="fb" sourceRef="cg" targetRef="tb" />
            <bpmn:userTask id="ta" name="Route A" />
            <bpmn:userTask id="tb" name="Route B" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static readonly XNamespace Bpmn218 = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable218 = "http://flowable.org/bpmn";
    private static readonly XNamespace Autonate218 = "http://autonate.dev/workflows";

    [Fact]
    public void ExpandForDeployment_PutsAScriptTaskInFrontOfAComplexGateway_AndKeepsTheGateway()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml));

        // The gateway SURVIVES. Verified against Flowable 8.0.0: complexGateway
        // runs as an exclusive gateway, so there is nothing to replace it with,
        // and keeping it means the engine reports an id the author's diagram has.
        var gateway = Assert.Single(document.Descendants(Bpmn218 + "complexGateway"));
        Assert.Equal("cg", gateway.Attribute("id")?.Value);

        var scriptTask = Assert.Single(document.Descendants(Bpmn218 + "scriptTask"));
        Assert.Equal("cg__autonateRoute", scriptTask.Attribute("id")?.Value);

        // Both the gateway and its routing task appear in history, and both map
        // onto the same shape, so an operator has only the name to tell them
        // apart when one of them is the one that failed.
        Assert.Equal("Choose (routing script)", scriptTask.Attribute("name")?.Value);
        Assert.Equal("return 'fa';", scriptTask.Element(Bpmn218 + "script")?.Value);
        // flowable:, not bare. Flowable refuses a bare resultVariable on
        // bpmn:scriptTask against the strict schema, so this spelling is the
        // difference between deploying and a 500 at publish.
        Assert.Equal("__autonateRoute_cg",
            scriptTask.Attribute(Flowable218 + "resultVariable")?.Value);
        Assert.Null(scriptTask.Attribute("resultVariable"));

        // The generated task runs on the job executor. ForceAsyncScriptTasks runs
        // on the prepare path, which this element never passed through, so
        // forgetting this here would run the sandbox call inline.
        Assert.Equal("true", scriptTask.Attribute(Flowable218 + "async")?.Value);

        // The author's declared identity follows onto the node that actually runs.
        Assert.Equal("system", scriptTask.Attribute(Autonate218 + "runAs")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_StripsTheGatewaysAuthoringPropertiesFromTheDeployedCopy()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml));
        var gateway = document.Descendants(Bpmn218 + "complexGateway").Single();

        // Flowable validates the deployed XML against the STRICT BPMN schema,
        // and bpmn:complexGateway has no scriptFormat and no script child. Left
        // in place they do not degrade anything — they refuse the entire
        // deployment with
        //   cvc-complex-type.3.2.2: Attribute 'scriptFormat' is not allowed to
        //   appear in element 'bpmn:complexGateway'
        // which is a 500 at publish for every workflow using the element.
        Assert.Null(gateway.Attribute("scriptFormat"));
        Assert.Null(gateway.Element(Bpmn218 + "script"));
        Assert.Null(gateway.Attribute(Autonate218 + "runAs"));
        Assert.Null(gateway.Attribute(Autonate218 + "routeScript"));
        Assert.Null(gateway.Attribute(Autonate218 + "scriptFormat"));

        // The authoring data is not lost — it moved to the node that runs it.
        var scriptTask = document.Descendants(Bpmn218 + "scriptTask").Single();
        Assert.Equal("javascript", scriptTask.Attribute("scriptFormat")?.Value);
        Assert.Equal("return 'fa';", scriptTask.Element(Bpmn218 + "script")?.Value);
        Assert.Equal("system", scriptTask.Attribute(Autonate218 + "runAs")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_RewiresTheGatewaysInboundFlowToTheScriptTask()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml));

        var flows = document.Descendants(Bpmn218 + "sequenceFlow")
            .ToDictionary(f => f.Attribute("id")!.Value,
                f => (Source: f.Attribute("sourceRef")?.Value, Target: f.Attribute("targetRef")?.Value));

        // The token must reach the script BEFORE the gateway, or the gateway
        // evaluates conditions against a variable nothing has set yet and takes
        // the default — the silent-wrong-branch failure this story exists to end.
        Assert.Equal("cg__autonateRoute", flows["f0"].Target);
        Assert.Equal(("cg__autonateRoute", "cg"), flows["cg__autonateRoute__flow"]);
    }

    [Fact]
    public void ExpandForDeployment_ConditionsEachRouteOnTheScriptsResult()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml));

        string? ConditionOf(string flowId) => document.Descendants(Bpmn218 + "sequenceFlow")
            .Single(f => f.Attribute("id")?.Value == flowId)
            .Element(Bpmn218 + "conditionExpression")?.Value;

        Assert.Equal("${__autonateRoute_cg == 'fa'}", ConditionOf("fa"));
        Assert.Equal("${__autonateRoute_cg == 'fb'}", ConditionOf("fb"));

        // Both routes are conditioned, not just the first. A condition on only
        // one would still route correctly for that one and silently take it for
        // everything else.
        Assert.Equal(2, document.Descendants(Bpmn218 + "conditionExpression").Count());
    }

    [Fact]
    public void ExpandForDeployment_TellsTheScriptTaskWhichRoutesAreAllowed()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml));
        var scriptTask = document.Descendants(Bpmn218 + "scriptTask").Single();

        // Carried on the deployed element rather than injected into the script:
        // the engine-side behaviour reads it both to hand the routes to the
        // sandbox and to enforce the contract on the way back.
        Assert.Equal("fa,fb", scriptTask.Attribute(Flowable218 + "autonateAllowedRoutes")?.Value);

        // And the mapping back to the author's element, recoverable from the
        // deployed XML alone — the execution view has nothing else to go on.
        Assert.Equal("cg", scriptTask.Attribute(Flowable218 + "autonateExpandedFrom")?.Value);

        // The generated FLOW carries it too. Flowable records a traversed
        // sequence flow as an activity, so leaving this off puts an id no
        // author diagram contains into the execution view's highlight set.
        var generatedFlow = document.Descendants(Bpmn218 + "sequenceFlow")
            .Single(f => f.Attribute("id")?.Value == "cg__autonateRoute__flow");
        Assert.Equal("cg", generatedFlow.Attribute(Flowable218 + "autonateExpandedFrom")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_LeavesTheAuthorsDefaultFlowUnconditioned()
    {
        var xml = ComplexGatewayXml.Replace(
            "id=\"cg\" name=\"Choose\"",
            "id=\"cg\" name=\"Choose\" default=\"fb\"",
            StringComparison.Ordinal);
        Assert.Contains("default=\"fb\"", xml);

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));

        XElement Flow(string id) => document.Descendants(Bpmn218 + "sequenceFlow")
            .Single(f => f.Attribute("id")?.Value == id);

        // BPMN forbids a condition on the default flow, and Flowable honours
        // `default` on this element (verified). Conditioning it would deploy a
        // diagram the engine rejects.
        Assert.Null(Flow("fb").Element(Bpmn218 + "conditionExpression"));
        Assert.NotNull(Flow("fa").Element(Bpmn218 + "conditionExpression"));

        // And it is not offered to the script as a route, because the engine
        // reaches it only when no route matched.
        Assert.Equal("fa", document.Descendants(Bpmn218 + "scriptTask").Single()
            .Attribute(Flowable218 + "autonateAllowedRoutes")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_GivesAGatewayWithNoScriptAWorkingStarterBody()
    {
        var xml = ComplexGatewayXml.Replace("autonate:routeScript=\"return 'fa';\"", "");

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        var script = document.Descendants(Bpmn218 + "scriptTask").Single()
            .Element(Bpmn218 + "script")?.Value;

        // A freshly dropped gateway must publish. Taking the first route is
        // visibly wrong; an empty script is invisibly broken.
        Assert.Contains("return 'fa';", script);
    }

    [Fact]
    public void ExpandForDeployment_AlsoAcceptsAHandAuthoredScriptChild()
    {
        // The studio writes an autonate: attribute because bpmn-js drops a
        // <bpmn:script> child on this element. An imported diagram written the
        // obvious way must still run — the constraint is the modeller's, and it
        // is not the importer's problem.
        // Its own literal rather than surgery on the shared fixture. The first
        // version of this test edited ComplexGatewayXml and silently failed to
        // match, so the attribute stayed and the test proved nothing about the
        // child at all.
        const string handAuthored = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="router" name="Router" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="cg" />
                <bpmn:complexGateway id="cg" name="Choose">
                  <bpmn:script>return 'fb';</bpmn:script>
                </bpmn:complexGateway>
                <bpmn:sequenceFlow id="fa" sourceRef="cg" targetRef="ta" />
                <bpmn:sequenceFlow id="fb" sourceRef="cg" targetRef="tb" />
                <bpmn:userTask id="ta" name="Route A" />
                <bpmn:userTask id="tb" name="Route B" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(handAuthored));

        Assert.Equal("return 'fb';",
            document.Descendants(Bpmn218 + "scriptTask").Single()
                .Element(Bpmn218 + "script")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_IsIdempotent_ForComplexGateways()
    {
        var once = WorkflowBpmnXml.ExpandForDeployment(ComplexGatewayXml);
        var twice = WorkflowBpmnXml.ExpandForDeployment(once);

        var document = XDocument.Parse(twice);

        // Publishing twice must not stack a second script task in front of the
        // first, which would leave the gateway reading a variable the wrong node
        // wrote.
        Assert.Single(document.Descendants(Bpmn218 + "scriptTask"));
        Assert.Equal(2, document.Descendants(Bpmn218 + "conditionExpression").Count());
    }

    [Fact]
    public void ExpandForDeployment_DoesNotTouchTheStoredModel()
    {
        var before = ComplexGatewayXml;
        _ = WorkflowBpmnXml.ExpandForDeployment(before);

        // The expansion returns a new string; the input it was given is the
        // author's stored diagram and must be unchanged. This is the assertion
        // that catches an expansion mutating a shared XDocument.
        Assert.Equal(ComplexGatewayXml, before);
        Assert.Contains("complexGateway", ComplexGatewayXml);
        Assert.DoesNotContain("scriptTask", ComplexGatewayXml);
        Assert.Contains("routeScript", ComplexGatewayXml);
    }

    // Rewritten for #107. This asserted that a business rule task, an event
    // subprocess and a participant all produced *warnings* and no errors.
    //
    // Two of those three were wrong, which #103 established by deploying them:
    // Flowable executes event subprocesses and participants perfectly well. They
    // were warned about because the old deny-lists keyed on BPMN localName alone,
    // so `subProcess` denied every subprocess and `participant` denied every pool.
    // The business rule task genuinely cannot run — the rules engine is absent
    // from the image — and is now a deployment *error*, because deploying
    // something that will throw NoClassDefFoundError at runtime is the silence
    // this epic exists to end.
    [Fact]
    public void ValidateProcess_RefusesAnElementTheEngineCannotRun()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="warning_flow" name="Warning Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:businessRuleTask id="BusinessRuleTask_1" name="Decide" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:subProcess id="SubProcess_1" triggeredByEvent="true" />
                               <bpmn:participant id="Participant_1" processRef="warning_flow" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        // The business rule task is refused, and the message says why rather
        // than merely rejecting.
        Assert.Contains(result.Errors, e => e.Contains("Business Rule Task", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("rules engine", StringComparison.OrdinalIgnoreCase));

        // The complement, which is the half that would have caught the old bug:
        // elements the engine DOES run must not be refused. An assertion that
        // only checked the refusal above would pass for a validator that refuses
        // everything.
        Assert.DoesNotContain(result.Errors, e => e.Contains("Event Sub-Process", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Pool / Participant", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Exclusive Gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenProcessIsMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenScriptTaskUsesNonJavaScriptFormat()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="groovy">
                                 <bpmn:script>execution.setVariable("value", 1);</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, error => error.Contains("scriptFormat=\"javascript\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsJavaScriptScriptTask()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="javascript">
                                 <bpmn:script>variables.set("value", 1);</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        // The body here changed with #151. It used to be
        // `execution.setVariable("value", 1)`, which is no longer a valid
        // script: #147 moved execution into the sandbox, where `execution` is
        // not bound. Keeping the old body would have made this test assert
        // that an unpublishable script publishes.
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("script tasks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenScriptTaskBodyIsMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="javascript" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, error => error.Contains("inline script body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesScriptTaskFieldsFromElementSnapshots()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:userTask id="Task_1" name="Compute" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "script_flow",
            "Script Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_1",
                    "bpmn:ScriptTask",
                    "Compute",
                    "javascript",
                    "execution.setVariable(\"total\", 42);",
                    "total")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var task = document.Descendants(bpmn + "scriptTask").Single();

        Assert.Equal("javascript", task.Attribute("scriptFormat")?.Value);
        Assert.Equal("total", task.Attribute("resultVariable")?.Value);
        Assert.Equal("execution.setVariable(\"total\", 42);", task.Element(bpmn + "script")?.Value);
    }

    // Script tasks must serialize as flowable:async="true" so a thrown error
    // in the script becomes a job.execution.failed event instead of synchronously
    // 500ing the start-process API call.
    [Fact]
    public void ApplyProcessMetadata_ForcesScriptTasksAsync()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="async_script_flow" name="Async Script Flow" isExecutable="true">
                               <bpmn:scriptTask id="ScriptTask_1" name="Boom" scriptFormat="javascript">
                                 <bpmn:script>throw new Error("boom");</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(xml, "async_script_flow", "Async Script Flow");

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";
        var task = document.Descendants(bpmn + "scriptTask").Single();

        Assert.Equal("true", task.Attribute(flowable + "async")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_EmitsFlowableUserTaskAssignmentAttributes()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="assign_flow" name="Assign Flow" isExecutable="true">
                               <bpmn:userTask id="Task_Literal" name="Review" />
                               <bpmn:userTask id="Task_Expr" name="Approve" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "assign_flow",
            "Assign Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_Literal",
                    "bpmn:UserTask",
                    "Review",
                    Assignee: "11111111-1111-1111-1111-111111111111",
                    CandidateUsers: ["aaaa", "bbbb"],
                    CandidateGroups: ["reviewers", "approvers"]),
                new WorkflowElementSnapshot(
                    "Task_Expr",
                    "bpmn:UserTask",
                    "Approve",
                    Assignee: "${initiator}",
                    CandidateUsers: ["${currentRecord.assignees}"],
                    CandidateGroups: [])
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Equal("http://flowable.org/bpmn", document.Root!.GetNamespaceOfPrefix("flowable")?.NamespaceName);

        var literalTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Literal");
        Assert.Equal("11111111-1111-1111-1111-111111111111", literalTask.Attribute(flowable + "assignee")?.Value);
        Assert.Equal("aaaa,bbbb", literalTask.Attribute(flowable + "candidateUsers")?.Value);
        Assert.Equal("reviewers,approvers", literalTask.Attribute(flowable + "candidateGroups")?.Value);

        var expressionTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Expr");
        Assert.Equal("${initiator}", expressionTask.Attribute(flowable + "assignee")?.Value);
        Assert.Equal("${currentRecord.assignees}", expressionTask.Attribute(flowable + "candidateUsers")?.Value);
        Assert.Null(expressionTask.Attribute(flowable + "candidateGroups"));
    }

    [Fact]
    public void ApplyProcessMetadata_RoundTripsFlowableUserTaskDueDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="due_flow" name="Due Flow" isExecutable="true">
                               <bpmn:userTask id="Task_Activation" name="Review" />
                               <bpmn:userTask id="Task_FromStart" name="Approve" />
                               <bpmn:userTask id="Task_Cleared" name="Cleanup" flowable:dueDate="P7D" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "due_flow",
            "Due Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_Activation",
                    "bpmn:UserTask",
                    "Review",
                    DueDate: "P3D"),
                new WorkflowElementSnapshot(
                    "Task_FromStart",
                    "bpmn:UserTask",
                    "Approve",
                    DueDate: "${dueDateHelper.fromProcessStart(execution, 5)}"),
                new WorkflowElementSnapshot(
                    "Task_Cleared",
                    "bpmn:UserTask",
                    "Cleanup",
                    DueDate: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var activationTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Activation");
        Assert.Equal("P3D", activationTask.Attribute(flowable + "dueDate")?.Value);

        var fromStartTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_FromStart");
        Assert.Equal("${dueDateHelper.fromProcessStart(execution, 5)}", fromStartTask.Attribute(flowable + "dueDate")?.Value);

        var clearedTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Cleared");
        Assert.Null(clearedTask.Attribute(flowable + "dueDate"));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesSequenceFlowConditionExpressionFromElementSnapshots()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="decision_flow" name="Decision Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="EndEvent_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Gateway_1" targetRef="EndEvent_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "decision_flow",
            "Decision Flow",
            [
                new WorkflowElementSnapshot(
                    "Flow_1",
                    "bpmn:SequenceFlow",
                    "Approved path",
                    ConditionExpression: "${needsApproval}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var flow = document.Descendants(bpmn + "sequenceFlow").Single();
        var expression = flow.Element(bpmn + "conditionExpression");

        Assert.Equal("Approved path", flow.Attribute("name")?.Value);
        Assert.NotNull(expression);
        Assert.Equal("bpmn:tFormalExpression", expression!.Attribute(xsi + "type")?.Value);
        Assert.Equal("${needsApproval}", expression.Value);
    }

    [Fact]
    public void CreateStarterDiagram_CreatesBlankProcessWithoutSeedTasks()
    {
        var xml = WorkflowBpmnXml.CreateStarterDiagram("test_process", "Test Workflow");
        var document = XDocument.Parse(xml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace bpmndi = "http://www.omg.org/spec/BPMN/20100524/DI";

        var process = document.Descendants(bpmn + "process").Single();
        var plane = document.Descendants(bpmndi + "BPMNPlane").Single();

        Assert.Equal("test_process", process.Attribute("id")?.Value);
        Assert.Equal("Test Workflow", process.Attribute("name")?.Value);
        Assert.Empty(process.Elements());
        Assert.Empty(plane.Elements());
    }

    // --- Signal start events --------------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_MaterializesSignalRoot_FromStartEventSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: "OrderPlaced",
                    SignalTopic: "orders.events")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = Assert.Single(document.Root!.Elements(bpmn + "signal"));
        Assert.Equal("OrderPlaced", signal.Attribute("name")?.Value);
        Assert.Equal("orders.events", signal.Attribute(flowable + "topic")?.Value);

        var startEvent = document.Descendants(bpmn + "startEvent").Single();
        var signalEventDefinition = startEvent.Element(bpmn + "signalEventDefinition")!;
        Assert.Equal(signal.Attribute("id")?.Value, signalEventDefinition.Attribute("signalRef")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_DefaultsTopicToWorkflowSignals_WhenSnapshotTopicMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: "OrderPlaced")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = Assert.Single(document.Root!.Elements(bpmn + "signal"));
        Assert.Equal(WorkflowBpmnXml.DefaultSignalTopic, signal.Attribute(flowable + "topic")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_PrunesOrphanSignalRoots_WhenSnapshotClearsName()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        Assert.Empty(document.Root!.Elements(bpmn + "signal"));
        var signalEventDefinition = document.Descendants(bpmn + "signalEventDefinition").Single();
        Assert.Null(signalEventDefinition.Attribute("signalRef"));
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_WritesAndReadsRecordTypeShortCodes()
    {
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="Signal_Record" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="Signal_Record" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: "record.created",
            SignalTopic: "record.events",
            RecordTypeShortCodes: new[] { "asset", "vehicle" });

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.Contains("flowable:recordTypeShortCodes=\"asset,vehicle\"", updated);

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(updated);
        var registration = Assert.Single(registrations);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "asset", "vehicle" },
            registration.RecordTypeShortCodes);
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_OmitsAttribute_WhenFilterEmpty()
    {
        // Initial XML already carries the attribute — applying an empty filter
        // must clear it so the workflow reverts to "match all records".
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="Signal_Record" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="Signal_Record" flowable:recordTypeShortCodes="asset" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: "record.created",
            SignalTopic: "record.events",
            RecordTypeShortCodes: Array.Empty<string>());

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.DoesNotContain("flowable:recordTypeShortCodes", updated);
    }

    [Fact]
    public void ExtractSignalRegistrations_ReturnsTuplesForEverySignalStartEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:signal id="Signal_Stock" name="StockChanged" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                               <bpmn:startEvent id="StartEvent_2">
                                 <bpmn:signalEventDefinition signalRef="Signal_Stock" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        Assert.Equal(2, registrations.Count);
        Assert.Contains(registrations, r => r.SignalName == "OrderPlaced" && r.Topic == "orders.events");
        Assert.Contains(registrations, r =>
            r.SignalName == "StockChanged" && r.Topic == WorkflowBpmnXml.DefaultSignalTopic);
    }

    [Fact]
    public void ExtractSignalRegistrations_IgnoresSignalEventDefinitionsOnNonStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:intermediateCatchEvent id="Intermediate_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        Assert.Empty(registrations);
    }

    [Fact]
    public void ExtractSignalRegistrations_Throws_OnMalformedXml()
    {
        Assert.Throws<System.Xml.XmlException>(
            () => WorkflowBpmnXml.ExtractSignalRegistrations("not really xml"));
    }

    [Fact]
    public void ExtractSignalRegistrations_ReturnsEmpty_OnBlankXml()
    {
        Assert.Empty(WorkflowBpmnXml.ExtractSignalRegistrations(string.Empty));
    }

    [Fact]
    public void ExtractSignalRegistrations_PopulatesProcessDefinitionKey()
    {
        var xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:flowable="http://flowable.org/bpmn">
              <signal id="Signal_1" name="record.created" flowable:topic="record.events"/>
              <process id="OrderFlow">
                <startEvent id="StartEvent_1">
                  <signalEventDefinition signalRef="Signal_1"/>
                </startEvent>
              </process>
            </definitions>
            """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        var registration = Assert.Single(registrations);
        Assert.Equal("record.created", registration.SignalName);
        Assert.Equal("record.events", registration.Topic);
        Assert.Equal("OrderFlow", registration.ProcessDefinitionKey);
        Assert.Empty(registration.RecordTypeShortCodes);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutSignalEventDefinition_OnStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("signal events", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("signalEventDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsSignalStartEventWithoutEventType()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("Event Type", StringComparison.Ordinal));
    }

    // --- Timer start events ---------------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WritesCronTimeCycle_FromTimerStartSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 ? * MON-FRI")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        var timeCycle = timerEventDefinition.Element(bpmn + "timeCycle");
        Assert.NotNull(timeCycle);
        Assert.Equal("0 0 9 ? * MON-FRI", timeCycle!.Value);
        Assert.Equal("cron", timeCycle.Attribute(flowable + "type")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDuration"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesEndDateAttribute_WhenTimerSnapshotProvidesIt()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: "2026-12-31")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-12-31", timerEventDefinition.Attribute(flowable + "endDate")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_RemovesEndDate_WhenTimerSnapshotClearsIt()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="2025-12-31">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Null(timerEventDefinition.Attribute(flowable + "endDate"));
        Assert.Empty(timerEventDefinition.Elements(flowable + "endDate"));
    }

    [Fact]
    public void ApplyProcessMetadata_NormalizesEndDateChildElement_ToAttributeForRoundTrip()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <flowable:endDate>2026-06-01</flowable:endDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: "2026-06-01")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-06-01", timerEventDefinition.Attribute(flowable + "endDate")?.Value);
        Assert.Empty(timerEventDefinition.Elements(flowable + "endDate"));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithoutSchedule()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("recurrence schedule", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithMalformedCron()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">not a cron expression!!</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid cron expression", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithMalformedEndDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="not-a-date">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid end date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerStartEventWithValidCronAndEndDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="2026-12-31">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 ? * MON-FRI</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutTimerEventDefinition_OnStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("timer events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_RejectsStartEventWithBothTimerAndSignalDefinitions()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="OrderPlaced" />
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("signalEventDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyProcessMetadata_HandlesMultipleTimerStartEventsIndependently()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                               <bpmn:startEvent id="StartEvent_2">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?"),
                new WorkflowElementSnapshot(
                    "StartEvent_2",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 17 ? * FRI",
                    TimerEndDate: "2026-12-31")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var startEvents = document.Descendants(bpmn + "startEvent").ToArray();
        Assert.Equal(2, startEvents.Length);

        var first = startEvents.Single(e => e.Attribute("id")?.Value == "StartEvent_1");
        Assert.Equal("0 0 9 * * ?", first.Element(bpmn + "timerEventDefinition")?.Element(bpmn + "timeCycle")?.Value);
        Assert.Null(first.Element(bpmn + "timerEventDefinition")?.Attribute(flowable + "endDate"));

        var second = startEvents.Single(e => e.Attribute("id")?.Value == "StartEvent_2");
        Assert.Equal("0 0 17 ? * FRI", second.Element(bpmn + "timerEventDefinition")?.Element(bpmn + "timeCycle")?.Value);
        Assert.Equal("2026-12-31", second.Element(bpmn + "timerEventDefinition")?.Attribute(flowable + "endDate")?.Value);
    }

    [Fact]
    public void ValidateProcess_AllowsSignalStartEventListeningOnTelemetryTopic()
    {
        // Listening on the BusWatcher's own topic is now allowed: a workflow can
        // legitimately want to react to events Flowable itself publishes (e.g.
        // run a janitor workflow whenever any process completes). Loop avoidance
        // is the user's responsibility — pick a signal name that doesn't collide
        // with one of your own published events.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="process.completed" flowable:topic="workflow.execution.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    // --- Timer intermediate catch events --------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WritesTimeDuration_FromTimerIntermediateSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "PT15M")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("PT15M", timerEventDefinition.Element(bpmn + "timeDuration")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeCycle"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesTimeDate_FromTimerIntermediateSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDate: "2026-12-31T09:00:00")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-12-31T09:00:00", timerEventDefinition.Element(bpmn + "timeDate")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDuration"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesExpression_FromTimerIntermediateDurationSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "${execution.getVariable('waitDuration')}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var duration = document.Descendants(bpmn + "timerEventDefinition").Single()
            .Element(bpmn + "timeDuration")?.Value;
        Assert.Equal("${execution.getVariable('waitDuration')}", duration);
    }

    [Fact]
    public void ApplyProcessMetadata_SwitchingMode_DropsPreviousTimerKindOnIntermediateCatch()
    {
        // Previous shape: timeDate. New snapshot: timeDuration. Flowable
        // rejects multiple kinds, so the writer must clear the date child
        // when only duration is present.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>2026-01-01T09:00:00</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "PT30M")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("PT30M", timerEventDefinition.Element(bpmn + "timeDuration")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithoutSchedule()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("duration or date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithMalformedDuration()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>15 minutes</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid duration", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithMalformedDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>tomorrow at noon</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerIntermediateCatchEventWithLiteralDuration()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>P1DT12H</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerIntermediateCatchEventWithExpression()
    {
        // Expressions are evaluated at runtime by Flowable; the validator
        // can't sanity-check them, so we accept any ${...} body.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>${execution.getVariable('reminderDate')}</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutTimerIntermediateCatchEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>PT15M</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("intermediate catch events", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("timer events", StringComparison.OrdinalIgnoreCase));
    }

    // --- Service tasks (behaviors) -------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WiresAutonateBehaviorDelegate_ForServiceTaskSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1" name="Unlock account" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Unlock account",
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
        Assert.Equal("true", serviceTask.Attribute(flowable + "exclusive")?.Value);
        Assert.Equal("behavior", serviceTask.Attribute(flowable + "autonateServiceKind")?.Value);
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
    }

    // #168. The retry point. Every case is asserted in BOTH directions, because
    // the whole risk with a boolean that serialises to an attribute is that it
    // writes unconditionally or never — and asserting only the "on" case passes
    // against both bugs.
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, null)]
    public void ApplyProcessMetadata_WritesRetryPoint_AsFlowableAsync(bool retryPoint, string? expected)
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="retry_flow" name="Retry Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1" name="Charge card" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "retry_flow",
            "Retry Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Charge card",
                    BehaviorKey: "autonate.charge-card",
                    RetryPoint: retryPoint)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal(expected, serviceTask.Attribute(flowable + "async")?.Value);
    }

    // Turning it off has to REMOVE the attribute, not leave the previous value
    // standing. Without this, a retry point could be set but never unset — and
    // the happy-path test above starts from XML with no attribute, so it cannot
    // catch that.
    [Fact]
    public void ApplyProcessMetadata_ClearsRetryPoint_WhenTurnedOff()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="retry_flow" name="Retry Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1" name="Charge card" flowable:async="true" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "retry_flow",
            "Retry Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Charge card",
                    BehaviorKey: "autonate.charge-card",
                    RetryPoint: false)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Null(document.Descendants(bpmn + "serviceTask").Single()
            .Attribute(flowable + "async"));
    }

    // A snapshot from an SPA build that predates the setting sends null, which
    // must leave an existing retry point alone. Writing "false" for null would
    // silently clear every retry point in the estate on the next publish from a
    // stale tab.
    [Fact]
    public void ApplyProcessMetadata_LeavesRetryPointAlone_WhenTheSnapshotDoesNotMentionIt()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="retry_flow" name="Retry Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1" name="Charge card" flowable:async="true" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "retry_flow",
            "Retry Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Charge card",
                    BehaviorKey: "autonate.charge-card")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Equal("true", document.Descendants(bpmn + "serviceTask").Single()
            .Attribute(flowable + "async")?.Value);
    }

    // #168 AC: script tasks stay forced on, and the retry-point setting must not
    // have introduced a path that can turn one off. RetryPoint: false is the
    // adversarial input — it is what the studio would send if the fixed switch
    // ever became editable.
    [Fact]
    public void ApplyProcessMetadata_KeepsScriptTasksForcedAsync_EvenWhenTheSnapshotSaysOtherwise()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute">
                                 <bpmn:script>x = 1;</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "script_flow",
            "Script Flow",
            [
                new WorkflowElementSnapshot(
                    "ScriptTask_1",
                    "bpmn:ScriptTask",
                    "Compute",
                    ScriptFormat: "javascript",
                    Script: "x = 1;",
                    RetryPoint: false)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Equal("true", document.Descendants(bpmn + "scriptTask").Single()
            .Attribute(flowable + "async")?.Value);
    }

    // #112. Flowable rejects an intermediate throw (Message) at deploy and
    // silently ignores a message end event, so publish rewrites both onto the
    // behaviour bridge. Verified against Flowable 8.0.0 first — see the issue.
    [Fact]
    public void ExpandForDeployment_ExpandsAnIntermediateMessageThrow_IntoABehaviorServiceTask()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:message id="Msg_1" name="orderShipped" />
                             <bpmn:process id="sender" name="Sender" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="announce" />
                               <bpmn:intermediateThrowEvent id="announce"
                                                            flowable:autonateTargetProcessKey="receiver">
                                 <bpmn:messageEventDefinition messageRef="Msg_1" />
                               </bpmn:intermediateThrowEvent>
                               <bpmn:sequenceFlow id="f1" sourceRef="announce" targetRef="e" />
                               <bpmn:endEvent id="e" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updated = WorkflowBpmnXml.ExpandForDeployment(xml);
        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        // The element the validator rejects is gone...
        Assert.Empty(document.Descendants(bpmn + "intermediateThrowEvent"));
        Assert.Empty(document.Descendants(bpmn + "messageEventDefinition"));

        // ...replaced by a service task that KEEPS THE ORIGINAL ID, which is what
        // leaves every sequence flow and diagram shape pointing at it still valid.
        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("announce", serviceTask.Attribute("id")?.Value);
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
        Assert.Equal("autonate.send-message", serviceTask.Attribute(flowable + "behaviorKey")?.Value);

        // The flows were never rewritten, so they must still name it.
        Assert.Equal("announce",
            document.Descendants(bpmn + "sequenceFlow")
                .Single(f => f.Attribute("id")?.Value == "f0").Attribute("targetRef")?.Value);
        Assert.Equal("announce",
            document.Descendants(bpmn + "sequenceFlow")
                .Single(f => f.Attribute("id")?.Value == "f1").Attribute("sourceRef")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_ExpandsAMessageEndEvent_AndStillEndsTheProcess()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:message id="Msg_1" name="orderShipped" />
                             <bpmn:process id="sender" name="Sender" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="done" />
                               <bpmn:endEvent id="done" flowable:autonateTargetProcessKey="receiver">
                                 <bpmn:messageEventDefinition messageRef="Msg_1" />
                               </bpmn:endEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updated = WorkflowBpmnXml.ExpandForDeployment(xml);
        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("done", serviceTask.Attribute("id")?.Value);
        Assert.Equal("autonate.send-message", serviceTask.Attribute(flowable + "behaviorKey")?.Value);

        // The half a naive expansion drops: the process must still END. Turning
        // the end event into a service task and stopping there would leave the
        // instance running forever after its last step.
        var endEvent = document.Descendants(bpmn + "endEvent").Single();
        Assert.Equal("done_end", endEvent.Attribute("id")?.Value);
        Assert.Equal(
            "done",
            document.Descendants(bpmn + "sequenceFlow")
                .Single(f => f.Attribute("targetRef")?.Value == "done_end")
                .Attribute("sourceRef")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_Twice_DoesNotAppendASecondEndEvent()
    {
        // Publish is not once-only. Re-running the expansion over its own output
        // must be a no-op, or every republish grows the diagram another end event.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:message id="Msg_1" name="orderShipped" />
                             <bpmn:process id="sender" name="Sender" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="done" />
                               <bpmn:endEvent id="done" flowable:autonateTargetProcessKey="receiver">
                                 <bpmn:messageEventDefinition messageRef="Msg_1" />
                               </bpmn:endEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var once = WorkflowBpmnXml.ExpandForDeployment(xml);
        var twice = WorkflowBpmnXml.ExpandForDeployment(once);

        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        Assert.Single(XDocument.Parse(twice).Descendants(bpmn + "endEvent"));
        Assert.Single(XDocument.Parse(twice).Descendants(bpmn + "serviceTask"));
    }

    // #112, completed after a test finally exercised the element. Flowable refuses
    // a sendTask carrying a delegateExpression — "one of the attributes 'type' or
    // 'operation' is mandatory on sendTask" — so the behaviour bridge reaches it
    // by the same publish-time route the throw events take.
    [Fact]
    public void ExpandForDeployment_ExpandsASendTaskOnTheBehaviourBridge()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="sender" name="Sender" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="send" />
                               <bpmn:sendTask id="send" name="Tell them"
                                              flowable:behaviorKey="autonate.send-message"
                                              flowable:autonateTargetProcessKey="receiver" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Empty(document.Descendants(bpmn + "sendTask"));

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("send", serviceTask.Attribute("id")?.Value);
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
        Assert.Equal("autonate.send-message", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_LeavesASendTaskItDoesNotOwnAlone()
    {
        // A send task wired to something else is not ours to rewrite — an author
        // may legitimately use Flowable's own `type`/`operationRef` route, which
        // is what the manifest's Send Task departure already records.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="sender" name="Sender" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="send" />
                               <bpmn:sendTask id="send" name="Mail" flowable:type="mail" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        Assert.Single(document.Descendants(bpmn + "sendTask"));
        Assert.Empty(document.Descendants(bpmn + "serviceTask"));
    }

    // #156. Scope is authored on the EVENT and becomes Flowable's own attribute on
    // the SIGNAL at publish, so the engine enforces it rather than Auton8
    // filtering a broadcast afterwards.
    [Fact]
    public void ExpandForDeployment_WritesInstanceScopeOntoTheSignal()
    {
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(SignalDiagram("instance", "instance")));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = document.Root!.Elements(bpmn + "signal").Single();
        Assert.Equal("processInstance", signal.Attribute(flowable + "scope")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_LeavesAHandAuthoredScopeAlone()
    {
        // An event that says NOTHING is not the same as one that says "global".
        // A diagram may already carry Flowable's own flowable:scope — written by
        // hand or by another modeller — and publish must not widen it.
        //
        // This is a regression test: the first version treated absent as global
        // and stripped the attribute, which turned an instance-scoped signal into
        // a broadcast. It surfaced only under load, because in isolation the
        // assertion ran before the other instance had reacted.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Sig_1" name="approved" flowable:scope="processInstance" />
                             <bpmn:process id="p" name="P" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:intermediateCatchEvent id="catch">
                                 <bpmn:signalEventDefinition signalRef="Sig_1" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Equal(
            "processInstance",
            document.Root!.Elements(bpmn + "signal").Single().Attribute(flowable + "scope")?.Value);
    }

    [Fact]
    public void ExpandForDeployment_LeavesAGlobalSignalUnscoped()
    {
        // The complement, and the one that protects deployed behaviour: an event
        // with no scope attribute means global, which is Flowable's default and
        // what every diagram authored before this story carries. Defaulting to
        // instance here would silently narrow them on the next publish.
        var document = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(SignalDiagram(null, null)));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = document.Root!.Elements(bpmn + "signal").Single();
        Assert.Null(signal.Attribute(flowable + "scope"));
    }

    [Fact]
    public void ValidateProcess_RefusesTwoEventsScopingOneSignalNameDifferently()
    {
        // #270. This asserted a SPLIT: two <bpmn:signal> roots sharing a name,
        // one scoped and one not. Flowable refuses that outright —
        //   [Problem: 'flowable-signal-duplicate-name'] -> HTTP 500
        // measured against 8.0.0 — so the split was never deployable and the
        // test could not see it, because it compared elements instead of asking
        // an engine.
        //
        // Two events agreeing on a name but not on who hears it ARE genuinely
        // different subscriptions, which is exactly why they need different
        // NAMES. The engine gives one scope per name; the author picks.
        var result = WorkflowBpmnXml.ValidateProcess(SignalDiagram("instance", "global"));

        var error = Assert.Single(result.Errors, e => e.Contains("approved", StringComparison.Ordinal));
        Assert.Contains("one scope per signal name", error, StringComparison.Ordinal);

        // Nothing undeployable is emitted on the way to that refusal.
        var expanded = XDocument.Parse(
            WorkflowBpmnXml.ExpandForDeployment(SignalDiagram("instance", "global")));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        Assert.Single(expanded.Root!.Elements(bpmn + "signal"));
    }

    private static string SignalDiagram(string? throwScope, string? catchScope)
    {
        string Ext(string? scope) =>
            scope is null
                ? string.Empty
                : $"""
                    <bpmn:extensionElements>
                      <flowable:autonateSignalScope value="{scope}" />
                    </bpmn:extensionElements>
                  """;

        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="approved" />
              <bpmn:process id="p" name="P" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="throw" />
                <bpmn:intermediateThrowEvent id="throw">
            {{Ext(throwScope)}}
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateThrowEvent>
                <bpmn:intermediateCatchEvent id="catch">
            {{Ext(catchScope)}}
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    [Fact]
    public void ExpandForDeployment_LeavesAPlainEndEventAlone()
    {
        // The complement. Without it the expansion could rewrite EVERY end event
        // onto the behaviour bridge and all three tests above would still pass.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="plain" name="Plain" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="done" />
                               <bpmn:endEvent id="done" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updated = WorkflowBpmnXml.ExpandForDeployment(xml);
        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        Assert.Empty(document.Descendants(bpmn + "serviceTask"));
        Assert.Equal("done", document.Descendants(bpmn + "endEvent").Single().Attribute("id")?.Value);
    }

    // #114. An error nobody catches destroys the whole instance at run time —
    // Flowable answers the start call with 500 and no instance exists afterwards.
    // Verified against the engine before this was written. These pin the publish
    // refusal in BOTH directions, because a validation that rejects everything and
    // one that rejects nothing both pass a single-case test.
    [Fact]
    public void Validate_RefusesAnErrorEndEventNoBoundaryCatches()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(ErrorDiagram(boundaryCode: "Err_Other"));

        Assert.Contains(errors, e => e.Contains("raises 'E_KNOWN'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("nothing in", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAnErrorEndEventAMatchingBoundaryCatches()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(ErrorDiagram(boundaryCode: "Err_Known"));

        Assert.DoesNotContain(errors, e => e.Contains("raises 'Err_Known'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAnErrorCaughtByAnEventSubprocessStartEvent()
    {
        // The other way BPMN catches an error. Without this the validation would
        // reject a correct diagram, which is worse than the defect it fixes:
        // a false refusal blocks work an author has every right to publish.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:error id="Err_Known" errorCode="E_KNOWN" />
                             <bpmn:process id="p" name="P" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                               <bpmn:subProcess id="sub" name="Risky">
                                 <bpmn:startEvent id="is" />
                                 <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="ie" />
                                 <bpmn:endEvent id="ie">
                                   <bpmn:errorEventDefinition errorRef="Err_Known" />
                                 </bpmn:endEvent>
                               </bpmn:subProcess>
                               <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                                 <bpmn:startEvent id="hs">
                                   <bpmn:errorEventDefinition errorRef="Err_Known" />
                                 </bpmn:startEvent>
                                 <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                                 <bpmn:userTask id="ht" name="Handle" />
                               </bpmn:subProcess>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateExecutableProcess(xml),
            e => e.Contains("raises 'Err_Known'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LeavesAnUncaughtEscalationAlone()
    {
        // Escalation is not an error. An uncaught one is a notification nobody
        // subscribed to; the engine carries on, and refusing it would block a
        // legitimate diagram. Asserted so the two are not quietly unified.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:escalation id="Esc_1" escalationCode="ESC_1" />
                             <bpmn:process id="p" name="P" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                               <bpmn:subProcess id="sub" name="Escalating">
                                 <bpmn:startEvent id="is" />
                                 <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="ie" />
                                 <bpmn:endEvent id="ie">
                                   <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                                 </bpmn:endEvent>
                               </bpmn:subProcess>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        Assert.Empty(WorkflowBpmnXml.ValidateExecutableProcess(xml).Where(
            e => e.Contains("Esc_1", StringComparison.Ordinal)));
    }

    private static string ErrorDiagram(string boundaryCode) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_Known" errorCode="E_KNOWN" />
          <bpmn:error id="Err_Other" errorCode="E_OTHER" />
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
            <bpmn:subProcess id="sub" name="Risky">
              <bpmn:startEvent id="is" />
              <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="ie" />
              <bpmn:endEvent id="ie" name="Boom">
                <bpmn:errorEventDefinition errorRef="Err_Known" />
              </bpmn:endEvent>
            </bpmn:subProcess>
            <bpmn:boundaryEvent id="bnd" attachedToRef="sub">
              <bpmn:errorEventDefinition errorRef="{{boundaryCode}}" />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="fb" sourceRef="bnd" targetRef="caught" />
            <bpmn:userTask id="caught" name="Caught" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    // #164. Two rules with different justifications, so both directions are
    // asserted for each: a validation that refuses everything and one that
    // refuses nothing both pass a single-case test.
    [Fact]
    public void Validate_RefusesAnEventGatewayWithOnlyOnePath()
    {
        // Verified against Flowable 8.0.0: this DEPLOYS cleanly, so the engine
        // will not catch it for us. A choice between one thing waits forever on a
        // single event while the diagram suggests alternatives.
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(EventGatewayDiagram(secondPath: null));

        Assert.Contains(errors, e => e.Contains("nothing for it to choose between", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAnEventGatewayWithTwoCatchEvents()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(
            EventGatewayDiagram(secondPath: "intermediateCatchEvent"));

        Assert.DoesNotContain(errors, e => e.Contains("event-based gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RefusesAnEventGatewayPointingAtSomethingThatIsNotAnEvent()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(
            EventGatewayDiagram(secondPath: "userTask"));

        Assert.Contains(errors, e => e.Contains("which is not an event", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RefusesAReceiveTaskAfterAnEventGateway_AndSaysWhy()
    {
        // BPMN allows this; Flowable does not — verified, it refuses the whole
        // deployment with a parse error naming a line and column. Refusing it here
        // with an explanation is the point, so the message is asserted rather than
        // just the refusal.
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(
            EventGatewayDiagram(secondPath: "receiveTask"));

        Assert.Contains(errors, e => e.Contains("this engine does not", StringComparison.Ordinal));
    }

    private static string EventGatewayDiagram(string? secondPath)
    {
        var second = secondPath switch
        {
            "intermediateCatchEvent" => """
                <bpmn:sequenceFlow id="fb" sourceRef="gw" targetRef="onTimer" />
                <bpmn:intermediateCatchEvent id="onTimer" name="Timeout">
                  <bpmn:timerEventDefinition />
                </bpmn:intermediateCatchEvent>
              """,
            "userTask" => """
                <bpmn:sequenceFlow id="fb" sourceRef="gw" targetRef="plain" />
                <bpmn:userTask id="plain" name="Just a task" />
              """,
            "receiveTask" => """
                <bpmn:sequenceFlow id="fb" sourceRef="gw" targetRef="rt" />
                <bpmn:receiveTask id="rt" name="Await something" />
              """,
            _ => string.Empty
        };

        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="confirm" />
              <bpmn:process id="p" name="P" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="gw" />
                <bpmn:eventBasedGateway id="gw" name="First one wins" />
                <bpmn:sequenceFlow id="fa" sourceRef="gw" targetRef="onMsg" />
                <bpmn:intermediateCatchEvent id="onMsg" name="Confirmed">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:intermediateCatchEvent>
            {{second}}
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    // #162. Both shapes deploy cleanly and can never trigger, so nothing but this
    // stands between the author and a handler that silently never runs.
    [Fact]
    public void Validate_RefusesAnEventSubProcessWithNoStartEvent()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(EventSubProcessDiagram(startEvent: null));

        Assert.Contains(errors, e => e.Contains("has no start event", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RefusesAnEventSubProcessStartingOnNothing()
    {
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(
            EventSubProcessDiagram(startEvent: "<bpmn:startEvent id=\"hs\" />"));

        Assert.Contains(errors, e => e.Contains("starts on nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAnEventSubProcessWithATypedStartEvent()
    {
        // The complement. Without it both tests above pass against a rule that
        // refuses every event subprocess.
        var errors = WorkflowBpmnXml.ValidateExecutableProcess(EventSubProcessDiagram(
            startEvent: "<bpmn:startEvent id=\"hs\"><bpmn:errorEventDefinition errorRef=\"Err_1\" /></bpmn:startEvent>"));

        Assert.DoesNotContain(errors, e => e.Contains("event subprocess", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LeavesAnOrdinarySubProcessAlone()
    {
        // An ordinary subprocess starts with a plain start event and MUST — the
        // rule above would be exactly wrong applied to one, and #161 already
        // requires it to have one.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="p" name="P" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                               <bpmn:subProcess id="sub" name="Ordinary">
                                 <bpmn:startEvent id="is" />
                                 <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="it" />
                                 <bpmn:userTask id="it" name="Work" />
                               </bpmn:subProcess>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateExecutableProcess(xml),
            e => e.Contains("starts on nothing", StringComparison.Ordinal));
    }

    private static string EventSubProcessDiagram(string? startEvent) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" />
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
            <bpmn:userTask id="work" name="Work" />
            <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
              {{startEvent ?? string.Empty}}
              <bpmn:userTask id="ht" name="Handle" />
            </bpmn:subProcess>
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ApplyProcessMetadata_StripsLegacyClassAttribute_OnServiceTaskSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 name="Whatever"
                                                 flowable:class="com.example.LegacyDelegate"
                                                 flowable:expression="${ignored}" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Whatever",
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Null(serviceTask.Attribute(flowable + "class"));
        Assert.Null(serviceTask.Attribute(flowable + "expression"));
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_OverwritesPreviousBehaviorKey_OnServiceTaskSnapshot()
    {
        // Previously wired to an older behavior via the attribute shape; re-
        // applying should leave a single behaviorKey attribute pointing at
        // the new value.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior"
                                                 flowable:behaviorKey="old.behavior" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    null,
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_StripsLegacyFieldInjectionChildren_OnServiceTaskSnapshot()
    {
        // Earlier studio builds wrote behaviorKey/autonateServiceKind as
        // <flowable:field> child elements. Re-saving an older model should
        // promote them to attributes and leave no stale field children.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}">
                                 <bpmn:extensionElements>
                                   <flowable:field name="autonateServiceKind">
                                     <flowable:string>behavior</flowable:string>
                                   </flowable:field>
                                   <flowable:field name="behaviorKey">
                                     <flowable:string>old.behavior</flowable:string>
                                   </flowable:field>
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    null,
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
        // No leftover <flowable:field> children for the swept names.
        Assert.Empty(serviceTask
            .Element(bpmn + "extensionElements")
            ?.Elements(flowable + "field")
            .Where(f =>
            {
                var n = f.Attribute("name")?.Value;
                return n == "autonateServiceKind" || n == "behaviorKey";
            })
            ?? Enumerable.Empty<XElement>());
    }

    [Fact]
    public void ValidateProcess_RejectsServiceTask_WhenBehaviorKeyMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("behavior selected", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsServiceTask_WhenBehaviorConfigured()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="behavior_flow"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior"
                                                 flowable:behaviorKey="autonate.unlock-account" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("service tasks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_AcceptsServiceTask_WrittenInLegacyFieldInjectionShape()
    {
        // Back-compat: workflows saved by the older studio build still
        // validate without the user having to re-save them first.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="behavior_flow"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}">
                                 <bpmn:extensionElements>
                                   <flowable:field name="autonateServiceKind">
                                     <flowable:string>behavior</flowable:string>
                                   </flowable:field>
                                   <flowable:field name="behaviorKey">
                                     <flowable:string>autonate.unlock-account</flowable:string>
                                   </flowable:field>
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_RejectsServiceTask_WithUnsupportedKind()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="http-call" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("unsupported autonateServiceKind", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_IgnoresServiceTaskBoundToCustomDelegate()
    {
        // A service task wired to a non-AutoNate delegate (legitimate v2
        // shape: plugin ships its own JavaDelegate class) is left alone —
        // we don't validate behavior keys for it.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:class="com.acme.MyDelegate" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_AcceptsASignalIntermediateCatchEvent()
    {
        // Rewritten for #107. This asserted a signal intermediate catch event
        // still *warned*, with the comment "make sure we didn't accidentally
        // whitelist the entire element type".
        //
        // That concern was right and the mechanism was wrong. The old deny-list
        // keyed on `intermediateCatchEvent`, so it denied every variant, and a
        // hand-written carve-out re-permitted the timer one. #103 deployed the
        // rest: message, signal and conditional catches all execute.
        //
        // The manifest keys on (localName, eventDefinition), so "don't whitelist
        // the whole element type" is now structural rather than a carve-out —
        // which is why this test can assert acceptance without weakening a gate.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="OrderPlaced" />
                             <bpmn:process id="catch_flow" name="Catch Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        // It executes, so it is neither refused nor warned about.
        Assert.DoesNotContain(
            result.Errors,
            e => e.Contains("Intermediate Catch (Signal)", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("intermediate catch events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForInclusiveGatewayWithConditions()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${pathA}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${pathB}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("inclusive gateways", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Inclusive gateway 'Branches'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForParallelGateway()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="parallel_flow" name="Parallel Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:parallelGateway id="Gateway_1" name="Fork" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("parallel gateways", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Parallel gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_Warns_ForInclusiveGatewayWithoutConditionsOrDefault()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("Inclusive gateway 'Branches'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("no conditions on its outgoing flows and no default flow", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForInclusiveGatewayWithDefaultFlow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" default="Flow_A" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Inclusive gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_Warns_ForParallelGatewayWithConditionedOutflow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="parallel_flow" name="Parallel Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:parallelGateway id="Gateway_1" name="Fork" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${shouldGo}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("Parallel gateway 'Fork'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("ignores conditions on parallel-gateway outflows", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesConditionExpression_OnInclusiveGatewayOutflow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="EndEvent_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Gateway_1" targetRef="EndEvent_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "inclusive_flow",
            "Inclusive Flow",
            [
                new WorkflowElementSnapshot(
                    "Flow_1",
                    "bpmn:SequenceFlow",
                    "High risk",
                    ConditionExpression: "${riskLevel == 'high'}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var flow = document.Descendants(bpmn + "sequenceFlow").Single();
        var expression = flow.Element(bpmn + "conditionExpression");

        Assert.Equal("High risk", flow.Attribute("name")?.Value);
        Assert.NotNull(expression);
        Assert.Equal("bpmn:tFormalExpression", expression!.Attribute(xsi + "type")?.Value);
        Assert.Equal("${riskLevel == 'high'}", expression.Value);
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenRecordTypeFilterAppearsOnIntermediateCatch()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="S" name="record.created" flowable:topic="record.events" />
                             <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                               <bpmn:startEvent id="Start" />
                               <bpmn:intermediateCatchEvent id="Catch">
                                 <bpmn:signalEventDefinition signalRef="S" flowable:recordTypeShortCodes="asset" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors,
            e => e.Contains("recordTypeShortCodes", StringComparison.OrdinalIgnoreCase)
              && e.Contains("startEvent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_ClearsRecordTypeShortCodes_WhenSignalNameCleared()
    {
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="S" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="S" flowable:recordTypeShortCodes="asset" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: null,                              // user cleared the signal name
            SignalTopic: "record.events",
            RecordTypeShortCodes: new[] { "asset" });      // stale; should be cleared with the name

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.DoesNotContain("flowable:recordTypeShortCodes", updated);
        Assert.DoesNotContain("signalRef", updated); // pre-existing behavior — sanity check
    }

    // ----- Default-behavior user task → exclusive gateway auto-rewrite -----

    private const string GatewayChoiceFlowVariable = WorkflowBpmnXml.GatewayChoiceVariableName;

    private static string DefaultModeUserTaskBeforeGatewayXml(
        string? userTaskMode = null,
        bool firstFlowConditioned = false) =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                            xmlns:flowable="http://flowable.org/bpmn"
                            id="Definitions_1"
                            targetNamespace="http://autonate.dev/workflows">
            <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
              <bpmn:startEvent id="StartEvent_1" />
              <bpmn:userTask id="Task_Approve" name="Approve"{{(userTaskMode is null ? string.Empty : $" flowable:userFormMode=\"{userTaskMode}\"")}}>
                <bpmn:documentation>Please review and pick a path.</bpmn:documentation>
              </bpmn:userTask>
              <bpmn:exclusiveGateway id="Gateway_1" />
              <bpmn:endEvent id="End_Approved" />
              <bpmn:endEvent id="End_Rejected" />
              <bpmn:sequenceFlow id="Flow_StartToTask" sourceRef="StartEvent_1" targetRef="Task_Approve" />
              <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
              <bpmn:sequenceFlow id="Flow_Approve" name="Approve" sourceRef="Gateway_1" targetRef="End_Approved">
                {{(firstFlowConditioned ? "<bpmn:conditionExpression xsi:type=\"bpmn:tFormalExpression\">${author == 'wrote-this'}</bpmn:conditionExpression>" : string.Empty)}}
              </bpmn:sequenceFlow>
              <bpmn:sequenceFlow id="Flow_Reject" name="Reject" sourceRef="Gateway_1" targetRef="End_Rejected" />
            </bpmn:process>
          </bpmn:definitions>
          """;

    [Fact]
    public void ApplyProcessMetadata_InjectsGatewayChoiceConditions_ForDefaultUserTaskBeforeExclusiveGateway()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var approveFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Approve");
        var rejectFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Reject");

        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Approve'}}",
            approveFlow.Element(bpmn + "conditionExpression")?.Value);
        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Reject'}}",
            rejectFlow.Element(bpmn + "conditionExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesAuthorAuthoredConditions_OnGatewayFlows()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(firstFlowConditioned: true),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var approveFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Approve");
        var rejectFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Reject");

        Assert.Equal(
            "${author == 'wrote-this'}",
            approveFlow.Element(bpmn + "conditionExpression")?.Value);
        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Reject'}}",
            rejectFlow.Element(bpmn + "conditionExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_IsIdempotent_ForGatewayChoiceConditions()
    {
        var first = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");
        var second = WorkflowBpmnXml.ApplyProcessMetadata(first, "approval_flow", "Approval");

        var document = XDocument.Parse(second);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_Approve", "Flow_Reject" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Single(flow.Elements(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void ApplyProcessMetadata_DoesNotInjectConditions_ForFormModeUserTask()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(userTaskMode: "modal"),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_Approve", "Flow_Reject" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Null(flow.Element(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void ApplyProcessMetadata_DoesNotInjectConditions_WhenGatewayIsInclusiveOrParallel()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
                               <bpmn:userTask id="Task_Approve" name="Approve" />
                               <bpmn:inclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="End_A" />
                               <bpmn:endEvent id="End_B" />
                               <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" name="A" sourceRef="Gateway_1" targetRef="End_A" />
                               <bpmn:sequenceFlow id="Flow_B" name="B" sourceRef="Gateway_1" targetRef="End_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(xml, "approval_flow", "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_A", "Flow_B" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Null(flow.Element(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsChoicesAndDescription_ForDefaultModeUserTaskBeforeGateway()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(prepared, "Task_Approve");

        Assert.NotNull(description);
        Assert.Equal("Please review and pick a path.", description!.Description);
        Assert.Equal(2, description.Choices.Count);
        Assert.Equal("Flow_Approve", description.Choices[0].FlowId);
        Assert.Equal("Approve", description.Choices[0].Label);
        Assert.Equal("Flow_Reject", description.Choices[1].FlowId);
        Assert.Equal("Reject", description.Choices[1].Label);
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsEmptyChoices_WhenTaskIsFormMode()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(userTaskMode: "modal"),
            "approval_flow",
            "Approval");

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(prepared, "Task_Approve");

        Assert.NotNull(description);
        Assert.Empty(description!.Choices);
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsEmptyChoices_WhenTaskIsNotBeforeAnExclusiveGateway()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="simple_flow" name="Simple" isExecutable="true">
                               <bpmn:userTask id="Task_1" name="Step" />
                               <bpmn:endEvent id="End_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Task_1" targetRef="End_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(xml, "Task_1");

        Assert.NotNull(description);
        Assert.Empty(description!.Choices);
    }

    [Fact]
    public void ValidateProcess_WarnsAboutUnnamedGatewayFlows_UnderDefaultUserTask()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
                               <bpmn:userTask id="Task_Approve" name="Approve" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="End_A" />
                               <bpmn:endEvent id="End_B" />
                               <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" name="Yes" sourceRef="Gateway_1" targetRef="End_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="End_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Warnings, w => w.Contains("Flow_B", StringComparison.Ordinal));
    }
}
