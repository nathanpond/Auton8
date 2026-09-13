import { describe, it, expect } from "vitest";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

/**
 * The fake's fidelity is itself asserted (#323).
 *
 * Every round-trip test in this directory is only as trustworthy as the rule
 * below. A fake that put namespaced keys somewhere else would make all of them
 * pass while the studio wrote attributes the serialiser drops -- a green suite
 * over broken authoring, which is the exact shape this milestone exists to end.
 */
describe("the fake routes keys the way bpmn-js does", () => {
  it("parks a namespaced key in $attrs and a bare key as a direct field", () => {
    const bo = businessObject("bpmn:Task", "t1");
    const { handle } = fakeModeler([element(bo)]);
    const modeling = handle.modeler.get("modeling");

    modeling.updateProperties(element(bo), {
      name: "Approve",
      "autonate:loopCardinality": "3",
      "flowable:collection": "${items}"
    });

    expect(bo.name).toBe("Approve");
    expect(bo.$attrs["autonate:loopCardinality"]).toBe("3");
    expect(bo.$attrs["flowable:collection"]).toBe("${items}");
    expect(bo["autonate:loopCardinality"]).toBeUndefined();
  });

  it("clears a namespaced key when the value is null, rather than storing null", () => {
    const bo = businessObject("bpmn:Task", "t1", { $attrs: { "autonate:dataType": "string" } });
    const { handle } = fakeModeler([element(bo)]);

    handle.modeler.get("modeling").updateProperties(element(bo), { "autonate:dataType": null });

    expect("autonate:dataType" in bo.$attrs).toBe(false);
  });

  it("writes moddle properties onto the nested object, not the business object", () => {
    const loop = { $type: "bpmn:MultiInstanceLoopCharacteristics", $attrs: {} };
    const bo = businessObject("bpmn:Task", "t1", { loopCharacteristics: loop });
    const { handle } = fakeModeler([element(bo)]);

    handle.modeler.get("modeling").updateModdleProperties(element(bo), loop, { isSequential: true });

    expect(loop.isSequential).toBe(true);
    expect(bo.isSequential).toBeUndefined();
  });
});
