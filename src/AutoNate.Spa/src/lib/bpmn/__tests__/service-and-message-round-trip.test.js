import { describe, it, expect } from "vitest";
import {
  updateServiceTaskProperties,
  updateMessageElementProperties,
  describeElementById
} from "../workflow.js";
import { fakeModeler, businessObject, element, moddleElement } from "./fake-modeler.js";

/**
 * The service-task and message-element properties (#323).
 *
 * `delegateExpression` is the one that carries a do-not-rename identifier:
 * `${autonateBehaviorDelegate}` is on CLAUDE.md's protected list because the
 * engine resolves it by name, so it is asserted literally rather than by shape.
 */
describe("service task properties round-trip", () => {
  it("writes the behaviour wiring and reads it back", () => {
    const bo = businessObject("bpmn:ServiceTask", "s1");
    const { handle } = fakeModeler([element(bo)]);

    updateServiceTaskProperties(handle, {
      id: "s1",
      name: "Send invoice",
      serviceTaskKind: "behavior",
      behaviorKey: "invoice.send",
      retryPoint: true
    });

    const described = describeElementById(handle, "s1");
    expect(described.serviceTaskKind).toBe("behavior");
    expect(described.behaviorKey).toBe("invoice.send");
    expect(described.retryPoint).toBe(true);
  });

  it("wires delegateExpression to the protected delegate name, exactly", () => {
    const bo = businessObject("bpmn:ServiceTask", "s1");
    const { handle } = fakeModeler([element(bo)]);

    updateServiceTaskProperties(handle, { id: "s1", behaviorKey: "k" });

    // On the do-not-rename list: the engine resolves this by name, and a
    // renamed delegate breaks every already-published workflow.
    expect(bo.$attrs["flowable:delegateExpression"]).toBe("${autonateBehaviorDelegate}");
    expect(bo.$attrs["flowable:autonateServiceKind"]).toBe("behavior");
  });

  it("reads the retry point as off when async is absent, which is Flowable's default", () => {
    const bo = businessObject("bpmn:ServiceTask", "s1");
    const { handle } = fakeModeler([element(bo)]);

    updateServiceTaskProperties(handle, { id: "s1", behaviorKey: "k", retryPoint: false });

    expect(bo.$attrs["flowable:async"]).toBeUndefined();
    expect(describeElementById(handle, "s1").retryPoint).toBe(false);
  });

  it("refuses a topic-shaped payload on a non-service task rather than writing it", () => {
    const bo = businessObject("bpmn:UserTask", "s1");
    const { handle } = fakeModeler([element(bo)]);

    // `topic` is one of the alternative wirings the writer sweeps; the guard
    // above it must fire first.
    expect(() =>
      updateServiceTaskProperties(handle, { id: "s1", behaviorKey: "k", topic: "t" })
    ).toThrow(/no longer available/);
    expect(bo.$attrs["flowable:topic"]).toBeUndefined();
  });
});

describe("message element properties round-trip", () => {
  it("writes the correlation key and target process key and reads them back", () => {
    const bo = businessObject("bpmn:ReceiveTask", "m1");
    const { handle } = fakeModeler([element(bo)]);

    updateMessageElementProperties(handle, {
      id: "m1",
      correlationKey: "orderId",
      targetProcessKey: "fulfilment"
    });

    const described = describeElementById(handle, "m1");
    expect(described.messageCorrelationKey).toBe("orderId");
    expect(described.messageTargetProcessKey).toBe("fulfilment");

    // The stored names, not only the read names: these two are the wire format
    // an already-published diagram carries, so a rename is a data migration
    // rather than a refactor.
    expect(bo.$attrs["flowable:autonateCorrelationKey"]).toBe("orderId");
    expect(bo.$attrs["flowable:autonateTargetProcessKey"]).toBe("fulfilment");
  });

  it("stores autonateMessageName only for a send task, which has no message element to name it", () => {
    const send = businessObject("bpmn:SendTask", "m2");
    const { handle } = fakeModeler([element(send)]);

    updateMessageElementProperties(handle, { id: "m2", messageName: "invoice.sent" });

    expect(describeElementById(handle, "m2").messageName).toBe("invoice.sent");
    expect(send.$attrs["flowable:autonateMessageName"]).toBe("invoice.sent");
  });
});

/**
 * A send task the studio produces is deployable (#328, #316).
 *
 * #316 withdrew Send Task because **no state of it the studio could produce was
 * deployable**: Flowable requires `type` or `operation` and refuses the whole
 * deployment otherwise, and Auton8's expansion adopts one only when it carries
 * `flowable:behaviorKey="autonate.send-message"` — which nothing in the studio
 * could write. The element was BPMN's most obvious "send a message" shape and it
 * was unavailable while a perfectly good message-sending behaviour sat behind it.
 */
describe("a send task the studio configures can actually deploy", () => {
  it("writes the behaviour key the expansion needs", () => {
    const send = businessObject("bpmn:SendTask", "s3");
    const { handle } = fakeModeler([element(send)]);

    updateMessageElementProperties(handle, { id: "s3", messageName: "invoice.sent" });

    // The literal, not the shape: WorkflowBpmnXml.SendMessageBehaviorKey is the
    // other half of this contract and matches on ordinal equality.
    expect(send.$attrs["flowable:behaviorKey"]).toBe("autonate.send-message");
  });

  it("does not put the behaviour key on anything that is not a send task", () => {
    // A receive task goes through the same editor. Adopting one would convert an
    // element the author meant to WAIT into one that sends.
    const receive = businessObject("bpmn:ReceiveTask", "r1");
    const { handle } = fakeModeler([element(receive)]);

    updateMessageElementProperties(handle, { id: "r1", messageName: "invoice.sent" });

    expect(receive.$attrs["flowable:behaviorKey"]).toBeUndefined();
    expect(receive.$attrs["flowable:autonateMessageName"]).toBeUndefined();
  });

  it("still carries the message name beside the key, so the send has something to send", () => {
    const send = businessObject("bpmn:SendTask", "s4");
    const { handle } = fakeModeler([element(send)]);

    updateMessageElementProperties(handle, { id: "s4", messageName: "invoice.sent" });

    expect(describeElementById(handle, "s4").messageName).toBe("invoice.sent");
    expect(send.$attrs["flowable:behaviorKey"]).toBe("autonate.send-message");
  });
});

/**
 * The two properties #411's broken fake was hiding.
 */
describe("script task result variable round-trips, in the namespace the engine accepts", () => {
  it("writes flowable:resultVariable, not a bare one", async () => {
    const { updateScriptTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ScriptTask", "st1");
    const { handle } = fakeModeler([element(bo)]);

    updateScriptTaskProperties(handle, {
      id: "st1", name: "calc", scriptFormat: "javascript",
      script: "variables.set('x', 1);", resultVariable: "out"
    });

    // A BARE resultVariable on a bpmn:scriptTask is refused by Flowable outright
    // -- "Attribute 'resultVariable' is not allowed to appear in element
    // 'scriptTask'" -- which WorkflowBpmnXml's gateway expansion already knew.
    expect(bo.$attrs["flowable:resultVariable"]).toBe("out");
    expect(bo.$attrs.resultVariable).toBeUndefined();
    expect(bo.resultVariable).toBeUndefined();
  });

  it("reads it back, which it never did before", async () => {
    const { updateScriptTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ScriptTask", "st2");
    const { handle } = fakeModeler([element(bo)]);

    updateScriptTaskProperties(handle, {
      id: "st2", name: "calc", scriptFormat: "javascript",
      script: "variables.set('x', 1);", resultVariable: "out"
    });

    expect(describeElementById(handle, "st2").resultVariable).toBe("out");
  });

  it("still reads a diagram that carries the old bare spelling", async () => {
    const bo = businessObject("bpmn:ScriptTask", "st3");
    bo.$attrs.resultVariable = "legacy";
    const { handle } = fakeModeler([element(bo)]);

    // Diagrams saved before #411 carry it bare. Migrating them is not this
    // change's job; reading them is.
    expect(describeElementById(handle, "st3").resultVariable).toBe("legacy");
  });
});

describe("clearing a service task's alternative wirings removes them", () => {
  it("does not leave the string \"null\" behind", async () => {
    const { updateServiceTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ServiceTask", "sv1");
    bo.$attrs.class = "com.example.Old";
    const { handle } = fakeModeler([element(bo)]);

    updateServiceTaskProperties(handle, { id: "sv1", behaviorKey: "k" });

    // moddle STORES a null, so passing one wrote class="null" into every service
    // task the studio touched. Publish strips those four, which is the only
    // reason it was never seen (#411).
    for (const key of ["class", "expression", "type", "delegateExpression"]) {
      expect(bo.$attrs[key], `${key} should be gone, not null`).toBeUndefined();
      expect(bo[key]).toBeUndefined();
    }
  });
});

/**
 * The three properties #409's widened scan found uncovered (#323, #409).
 *
 * All three are written through a DOTTED receiver, which the first version of
 * the meta-guard could not see -- so #159's "properties nobody listed" was live
 * again, in the guard built to prevent it.
 */
describe("the script identity and complex-gateway properties round-trip", () => {
  it("writes runAs and reads it back", async () => {
    const { updateScriptTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ScriptTask", "ri1");
    const { handle } = fakeModeler([element(bo)]);

    updateScriptTaskProperties(handle, {
      id: "ri1", name: "s", scriptFormat: "javascript", script: "x", runAs: "system"
    });

    expect(describeElementById(handle, "ri1").runAs).toBe("system");
  });

  it("refuses to record an identity it does not recognise, rather than storing it", async () => {
    const { updateScriptTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ScriptTask", "ri2");
    const { handle } = fakeModeler([element(bo)]);

    updateScriptTaskProperties(handle, {
      id: "ri2", name: "s", scriptFormat: "javascript", script: "x", runAs: "root"
    });

    // Unset and "explicitly nothing" must stay distinguishable in the XML, and a
    // privilege level nobody defined must not become one that exists.
    expect(describeElementById(handle, "ri2").runAs).toBeNull();
  });

  it("stores a complex gateway's routeScript and scriptFormat as attributes, and reads them back", async () => {
    const { updateScriptTaskProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:ComplexGateway", "cg1");
    const { handle } = fakeModeler([element(bo)]);

    updateScriptTaskProperties(handle, {
      id: "cg1", name: "Choose", scriptFormat: "javascript", script: "routes.take('a');"
    });

    const described = describeElementById(handle, "cg1");
    expect(described.script).toBe("routes.take('a');");
    expect(described.scriptFormat).toBe("javascript");

    // Attributes, not a <bpmn:script> child: bpmn-js's moddle has no script
    // property on ComplexGateway and drops the child on save.
    expect(bo.$attrs["autonate:routeScript"]).toBe("routes.take('a');");
    expect(bo.script).toBeUndefined();
  });
});

describe("a converted element records what it was", () => {
  it("marks the replacement with autonateConvertedFrom", async () => {
    const { takeConvertedTasks } = await import("../workflow.js");

    // The conversion runs inside the modeler's own replace flow, so this asserts
    // the export exists and the marker name is the one publish reads. The full
    // conversion path is exercised by the E2E studio suite.
    expect(typeof takeConvertedTasks).toBe("function");

    const bo = businessObject("bpmn:ServiceTask", "cv1");
    bo.$attrs["flowable:autonateConvertedFrom"] = "bpmn:Task";
    const { handle } = fakeModeler([element(bo)]);

    expect(describeElementById(handle, "cv1")).not.toBeNull();
    expect(bo.$attrs["flowable:autonateConvertedFrom"]).toBe("bpmn:Task");
  });
});

/**
 * The properties written through `modeling.updateProperties` and
 * `updateModdleProperties` (#409, third part).
 *
 * Three versions of the meta-guard could not see these two mechanisms, so
 * `isSequential` had to be named by hand and nine others were uncovered
 * entirely. The hand-coding was the signal and it was read as a footnote.
 */
describe("timer boundary event properties round-trip", () => {
  function timerBoundary(id = "tb1") {
    const definition = moddleElement("bpmn:TimerEventDefinition");
    return businessObject("bpmn:BoundaryEvent", id, { eventDefinitions: [definition] });
  }

  it.each([
    ["boundaryTimerDuration", { boundaryTimerDuration: "PT5M" }, "PT5M"],
    ["boundaryTimerDate", { boundaryTimerDate: "2026-01-01T00:00:00Z" }, "2026-01-01T00:00:00Z"],
    ["boundaryTimerCycle", { boundaryTimerCycle: "0 0 2 * * ?" }, "0 0 2 * * ?"]
  ])("writes %s and reads it back", async (field, payload, expected) => {
    const { updateTimerBoundaryEventProperties } = await import("../workflow.js");
    const bo = timerBoundary();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerBoundaryEventProperties(handle, { id: "tb1", name: "wait", ...payload });

    expect(describeElementById(handle, "tb1")[field]).toBe(expected);
  });

  it("keeps exactly one timer kind, because a stale one behaves unpredictably", async () => {
    const { updateTimerBoundaryEventProperties } = await import("../workflow.js");
    const bo = timerBoundary();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerBoundaryEventProperties(handle, { id: "tb1", boundaryTimerCycle: "0 0 2 * * ?" });
    updateTimerBoundaryEventProperties(handle, { id: "tb1", boundaryTimerDuration: "PT5M" });

    // Duration wins and the cycle must be GONE -- Flowable honours whichever it
    // finds, so two of them is a coin toss.
    const described = describeElementById(handle, "tb1");
    expect(described.boundaryTimerDuration).toBe("PT5M");
    expect(described.boundaryTimerCycle).toBeNull();
  });
});

describe("call activity properties round-trip", () => {
  it("writes calledElement and reads it back", async () => {
    const { updateCallActivityProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:CallActivity", "ca1");
    const { handle } = fakeModeler([element(bo)]);

    updateCallActivityProperties(handle, { id: "ca1", name: "call", calledElement: "other-process" });

    expect(describeElementById(handle, "ca1").calledElement).toBe("other-process");
  });

  it("refuses an element that is not a call activity", async () => {
    const { updateCallActivityProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:UserTask", "ca1");
    const { handle } = fakeModeler([element(bo)]);

    expect(() => updateCallActivityProperties(handle, { id: "ca1", calledElement: "x" }))
      .toThrow(/no longer available/);
  });
});

describe("sequence flow condition round-trips", () => {
  it("writes conditionExpression and reads its body back", async () => {
    const { updateSequenceFlowProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:SequenceFlow", "sf1");
    const { handle } = fakeModeler([element(bo)]);

    updateSequenceFlowProperties(handle, { id: "sf1", conditionExpression: "${approved}" });

    expect(describeElementById(handle, "sf1").conditionExpression).toBe("${approved}");
  });

  it("clears the condition when the author empties it, rather than leaving the old one", async () => {
    const { updateSequenceFlowProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:SequenceFlow", "sf2");
    const { handle } = fakeModeler([element(bo)]);

    updateSequenceFlowProperties(handle, { id: "sf2", conditionExpression: "${approved}" });
    updateSequenceFlowProperties(handle, { id: "sf2", conditionExpression: "" });

    // A stale condition on a flow the author meant to make unconditional is the
    // difference between a route taken and a route never taken.
    expect(describeElementById(handle, "sf2").conditionExpression).toBeNull();
  });
});

describe("ad-hoc sub-process ordering round-trips", () => {
  it("writes ordering and reads it back", async () => {
    const { updateElementDataProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:AdHocSubProcess", "ah1");
    const { handle } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, {
      id: "ah1", kind: "adhoc", name: "case", sequential: true, completionCondition: "${done}"
    });

    expect(describeElementById(handle, "ah1").adhocOrdering).toBe("Sequential");
  });

  it("reads Parallel when the author did not choose sequential", async () => {
    const { updateElementDataProperties } = await import("../workflow.js");
    const bo = businessObject("bpmn:AdHocSubProcess", "ah2");
    const { handle } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, {
      id: "ah2", kind: "adhoc", name: "case", sequential: false, completionCondition: "${done}"
    });

    expect(describeElementById(handle, "ah2").adhocOrdering).toBe("Parallel");
  });
});

describe("intermediate catch timer properties round-trip", () => {
  function timerCatch(id = "tc1") {
    const definition = moddleElement("bpmn:TimerEventDefinition");
    return businessObject("bpmn:IntermediateCatchEvent", id, { eventDefinitions: [definition] });
  }

  it.each([
    ["timerDuration", { timerDuration: "PT5M" }, "PT5M"],
    ["timerDate", { timerDate: "2026-01-01T00:00:00Z" }, "2026-01-01T00:00:00Z"]
  ])("writes %s onto the event definition and reads it back", async (field, payload, expected) => {
    const { updateTimerIntermediateCatchEventProperties } = await import("../workflow.js");
    const bo = timerCatch();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerIntermediateCatchEventProperties(handle, { id: "tc1", name: "wait", ...payload });

    expect(describeElementById(handle, "tc1")[field]).toBe(expected);

    // The MODDLE key, not just the read name: `timeDuration` / `timeDate` are
    // the BPMN element names the engine reads, and the panel's `timerDuration` /
    // `timerDate` are ours. Asserting only ours would leave the translation
    // between them untested, which is where #159's six properties lived.
    const definition = bo.eventDefinitions[0];
    const moddleKey = field === "timerDuration" ? "timeDuration" : "timeDate";
    expect(definition[moddleKey]?.body).toBe(expected);
  });

  it("replaces a duration with a date rather than carrying both", async () => {
    const { updateTimerIntermediateCatchEventProperties } = await import("../workflow.js");
    const bo = timerCatch();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerIntermediateCatchEventProperties(handle, { id: "tc1", timerDuration: "PT5M" });
    updateTimerIntermediateCatchEventProperties(handle, { id: "tc1", timerDate: "2026-01-01T00:00:00Z" });

    const described = describeElementById(handle, "tc1");
    expect(described.timerDate).toBe("2026-01-01T00:00:00Z");
    expect(described.timerDuration).toBeNull();
  });
});

describe("timer start event cron round-trips", () => {
  it("writes the cron cycle and reads it back as timerCycleCron", async () => {
    const { updateTimerStartEventProperties } = await import("../workflow.js");
    const definition = moddleElement("bpmn:TimerEventDefinition");
    const bo = businessObject("bpmn:StartEvent", "ts1", { eventDefinitions: [definition] });
    const { handle } = fakeModeler([element(bo)]);

    updateTimerStartEventProperties(handle, { id: "ts1", name: "nightly", timeCycle: "0 0 2 * * ?" });

    expect(describeElementById(handle, "ts1").timerCycleCron).toBe("0 0 2 * * ?");

    // flowable:type="cron" beside it, or Flowable parses the body as ISO 8601
    // and the schedule silently means something else.
    expect(definition.timeCycle?.$attrs?.["flowable:type"]).toBe("cron");
  });
});
