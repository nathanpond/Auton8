import { describe, it, expect } from "vitest";
import { updateTimerStartEventProperties, describeElementById } from "../workflow.js";
import { fakeModeler, businessObject, element, moddleElement } from "./fake-modeler.js";

/**
 * The two properties that live on a nested event definition rather than on the
 * element (#323): `endDate` on a timer, and `recordTypeShortCodes` on a signal.
 *
 * Both are the shape that hid #159's six: the writer reaches past the business
 * object into `eventDefinitions[n]`, so an assertion on the element alone says
 * nothing about whether the value landed.
 */
function timerStartEvent(id = "t1") {
  const timer = moddleElement("bpmn:TimerEventDefinition");
  return businessObject("bpmn:StartEvent", id, { eventDefinitions: [timer] });
}

describe("timer start event properties round-trip", () => {
  it("writes endDate onto the event definition and reads it back", () => {
    const bo = timerStartEvent();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerStartEventProperties(handle, {
      id: "t1",
      name: "Nightly",
      timeCycle: "0 0 2 * * ?",
      endDate: "2026-12-31T00:00:00Z"
    });

    expect(describeElementById(handle, "t1").timerEndDate).toBe("2026-12-31T00:00:00Z");
    // On the definition, not the element -- the distinction #159's gap turned on.
    expect(bo.eventDefinitions[0].$attrs["flowable:endDate"]).toBe("2026-12-31T00:00:00Z");
    expect(bo.$attrs["flowable:endDate"]).toBeUndefined();
  });

  it("clears endDate when the author empties it", () => {
    const bo = timerStartEvent();
    const { handle } = fakeModeler([element(bo)]);

    updateTimerStartEventProperties(handle, { id: "t1", timeCycle: "0 0 2 * * ?", endDate: "2026-12-31T00:00:00Z" });
    updateTimerStartEventProperties(handle, { id: "t1", timeCycle: "0 0 2 * * ?", endDate: "" });

    expect(describeElementById(handle, "t1").timerEndDate).toBeNull();
  });

  it("refuses a start event that is not a timer rather than writing a cycle to it", () => {
    const bo = businessObject("bpmn:StartEvent", "t1", { eventDefinitions: [] });
    const { handle } = fakeModeler([element(bo)]);

    expect(() =>
      updateTimerStartEventProperties(handle, { id: "t1", timeCycle: "0 0 2 * * ?" })
    ).toThrow(/not a timer start event/);
  });
});

describe("signal start event record-type filter round-trips", () => {
  it("reads recordTypeShortCodes back from the signal event definition", () => {
    const signal = moddleElement("bpmn:SignalEventDefinition", { "flowable:recordTypeShortCodes": "INV,PO" });
    const bo = businessObject("bpmn:StartEvent", "s1", { eventDefinitions: [signal] });
    const { handle } = fakeModeler([element(bo)]);

    // The whole list, in order: a reader that split on the wrong separator or
    // kept only the first entry passes a contains-check and fails this.
    expect(describeElementById(handle, "s1").recordTypeShortCodes).toEqual(["INV", "PO"]);
  });

  it("reads an absent record-type filter as empty rather than as a one-entry list", () => {
    const signal = moddleElement("bpmn:SignalEventDefinition");
    const bo = businessObject("bpmn:StartEvent", "s2", { eventDefinitions: [signal] });
    const { handle } = fakeModeler([element(bo)]);

    expect(describeElementById(handle, "s2").recordTypeShortCodes).toEqual([]);
  });
});
