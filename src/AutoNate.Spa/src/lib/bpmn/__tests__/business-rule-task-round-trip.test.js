import { describe, it, expect } from "vitest";
import { updateBusinessRuleTaskProperties, describeElementById } from "../workflow.js";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

/**
 * The decision table an author picks, written and read back (#111).
 *
 * `decisionKey` travels `writeAutoNateAttribute` -> `$attrs` -> the serialised
 * `autonate:decisionKey`, and comes back through `readAutoNateAttribute`. A round
 * trip is the only thing that proves the two halves agree: writing it under one
 * spelling and reading it under another gives a studio where the picker looks
 * empty every time the author reopens the modal, with nothing failing.
 */
function businessRuleTask(id = "decide") {
  return businessObject("bpmn:BusinessRuleTask", id);
}

describe("business rule task properties round-trip", () => {
  it("writes decisionKey and reads it back", () => {
    const bo = businessRuleTask();
    const { handle } = fakeModeler([element(bo)]);

    updateBusinessRuleTaskProperties(handle, {
      id: "decide",
      name: "Route the invoice",
      decisionKey: "invoiceRouting"
    });

    expect(describeElementById(handle, "decide").decisionKey).toBe("invoiceRouting");
    expect(describeElementById(handle, "decide").name).toBe("Route the invoice");
  });

  it("clears decisionKey when the author picks nothing", () => {
    // The complement, and not a formality: `writeAutoNateAttribute` with an empty
    // value has to REMOVE the attribute rather than write an empty one. An empty
    // `autonate:decisionKey=""` survives to publish, where the expansion reads it
    // as "configured" and deploys a step pointing at a table named "".
    const bo = businessRuleTask();
    const { handle } = fakeModeler([element(bo)]);

    updateBusinessRuleTaskProperties(handle, {
      id: "decide",
      name: "Route",
      decisionKey: "invoiceRouting"
    });
    expect(describeElementById(handle, "decide").decisionKey).toBe("invoiceRouting");

    updateBusinessRuleTaskProperties(handle, { id: "decide", name: "Route", decisionKey: "" });

    // Asserted on $attrs, NOT on the describe output. `describeElementById`
    // answers "" for both a removed attribute and an empty one -- its reader maps
    // a zero-length value to null and the merge does `?? ""` -- so an assertion
    // there cannot tell the two apart, and the distinction is the whole point:
    // an empty `autonate:decisionKey=""` serialises, survives to publish, and the
    // expansion reads it as configured, deploying a step that points at a table
    // named "". The attribute has to be GONE.
    expect("autonate:decisionKey" in bo.$attrs).toBe(false);
    expect(describeElementById(handle, "decide").decisionKey).toBe("");
  });

  it("is not described on an element that is not a business rule task", () => {
    // The routing rule: `onRequestConfigure` matches on `$type` AND the key's
    // presence, so the key must be ABSENT -- not null, not empty -- on everything
    // else. Merging it unconditionally would send every task-shaped element to
    // the decision modal.
    const bo = businessObject("bpmn:UserTask", "u1");
    const { handle } = fakeModeler([element(bo)]);

    expect("decisionKey" in describeElementById(handle, "u1")).toBe(false);
  });

  it("refuses to write onto an element that is not a business rule task", () => {
    const bo = businessObject("bpmn:UserTask", "u1");
    const { handle } = fakeModeler([element(bo)]);

    expect(() =>
      updateBusinessRuleTaskProperties(handle, { id: "u1", decisionKey: "x" })
    ).toThrow(/not a business rule task/);
  });
});
