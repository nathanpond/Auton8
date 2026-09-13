import { describe, it, expect } from "vitest";
import {
  updateServiceTaskProperties,
  updateMessageElementProperties,
  describeElementById
} from "../workflow.js";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

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
