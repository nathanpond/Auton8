import { describe, it, expect } from "vitest";
import { updateElementDataProperties, describeElementById } from "../workflow.js";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

/**
 * All seven multi-instance properties, written and read back (#323, #159).
 *
 * #159 shipped with exactly ONE of these asserted. The other six travel three
 * different mechanisms -- `writeFlowableAttribute`, `writeAutoNateAttribute`
 * and `updateModdleProperties` -- and a test of one says nothing about the
 * others, which is why the story names all seven rather than "multi-instance".
 */

function multiInstanceTask(id = "task1") {
  const loop = { $type: "bpmn:MultiInstanceLoopCharacteristics", $attrs: {} };
  return businessObject("bpmn:UserTask", id, { loopCharacteristics: loop });
}

const editor = {
  id: "task1",
  kind: "multiInstance",
  name: "Approve each",
  collection: "${items}",
  elementVariable: "item",
  completionCondition: "${approved}",
  cardinality: "3",
  aggregateTarget: "decisions",
  aggregateSource: "decision",
  sequential: true
};

describe("multi-instance properties round-trip through the studio's own functions", () => {
  it.each([
    ["collection", "multiInstanceCollection", "${items}"],
    ["elementVariable", "multiInstanceElementVariable", "item"],
    ["completionCondition", "multiInstanceCompletionCondition", "${approved}"],
    ["loopCardinality", "multiInstanceCardinality", "3"],
    ["aggregateTarget", "multiInstanceAggregateTarget", "decisions"],
    ["aggregateSource", "multiInstanceAggregateSource", "decision"]
  ])("writes %s and reads it back as %s", (_written, readAs, expected) => {
    const bo = multiInstanceTask();
    const { handle } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, editor);
    const described = describeElementById(handle, "task1");

    expect(described[readAs]).toBe(expected);
  });

  it("writes isSequential through updateModdleProperties and reads it back", () => {
    const bo = multiInstanceTask();
    const { handle, commands } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, editor);

    // The mechanism matters, not only the value: $attrs written without a
    // command are never re-serialised, which is what the source comment at the
    // write site warns about.
    expect(commands.some((c) => c.kind === "updateModdleProperties")).toBe(true);
    expect(describeElementById(handle, "task1").multiInstanceSequential).toBe(true);
  });

  it("reads back parallel when the author did not choose sequential", () => {
    const bo = multiInstanceTask();
    const { handle } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, { ...editor, sequential: false });

    expect(describeElementById(handle, "task1").multiInstanceSequential).toBe(false);
  });

  it("refuses an element that is no longer multi-instance rather than writing nothing", () => {
    const bo = businessObject("bpmn:UserTask", "task1");
    const { handle } = fakeModeler([element(bo)]);

    expect(() => updateElementDataProperties(handle, editor)).toThrow(/no longer marked as multi-instance/);
  });

  it("clears a property the author emptied, instead of leaving the old value", () => {
    const bo = multiInstanceTask();
    const { handle } = fakeModeler([element(bo)]);

    updateElementDataProperties(handle, editor);
    updateElementDataProperties(handle, { ...editor, collection: "", aggregateTarget: "" });

    const data = describeElementById(handle, "task1");
    expect(data.multiInstanceCollection).toBe("");
    expect(data.multiInstanceAggregateTarget).toBe("");
  });
});
