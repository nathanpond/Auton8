import { describe, it, expect } from "vitest";
import { fakeModeler, businessObject, element, moddleElement } from "./fake-modeler.js";

/**
 * The fake's fidelity, asserted against the library rather than against itself (#323, #411).
 *
 * The first version of this file stated a rule -- "namespaced goes to `$attrs`,
 * bare goes to a direct field" -- and certified it. #411 measured that rule
 * against the shipped bpmn-js and it was wrong twice over: routing is decided by
 * whether a **moddle descriptor declares the property**, not by the colon, and
 * `null` is STORED rather than clearing the key.
 *
 * Both errors had live consequences the round-trip tests could not see, because
 * they all ran on the fake. So these now assert what the REAL moddle does, and
 * the fake gets its answers from the same place.
 */
describe("the fake routes keys the way bpmn-js does", () => {
  it("parks an undeclared key in $attrs and a DECLARED key in a direct field", () => {
    const bo = businessObject("bpmn:Task", "t1");
    const { handle } = fakeModeler([element(bo)]);
    const modeling = handle.modeler.get("modeling");

    modeling.updateProperties(element(bo), {
      name: "Approve",
      "autonate:loopCardinality": "3",
      "flowable:collection": "${items}"
    });

    // `name` IS declared on bpmn:Task, so it becomes a direct field...
    expect(bo.name).toBe("Approve");
    // ...and neither of these is, so both land in $attrs -- the colon is not
    // what decides it, which is the whole of #411.
    expect(bo.$attrs["autonate:loopCardinality"]).toBe("3");
    expect(bo.$attrs["flowable:collection"]).toBe("${items}");
    expect(bo["autonate:loopCardinality"]).toBeUndefined();
  });

  it("puts an UNDECLARED BARE key in $attrs, which is where #411 was hiding", () => {
    const bo = businessObject("bpmn:ScriptTask", "s1");
    const { handle } = fakeModeler([element(bo)]);

    // `resultVariable` is not on bpmn:ScriptTask's descriptor. The studio writes
    // it bare (workflow.js), the old fake put it in a direct field, and
    // `describeElement` reads it as one -- so it never round-tripped in the real
    // studio and no test could see that.
    handle.modeler.get("modeling").updateProperties(element(bo), { resultVariable: "out" });

    expect(bo.$attrs.resultVariable).toBe("out");
    expect(bo.resultVariable).toBeUndefined();
  });

  it("STORES null rather than clearing the key, which is what moddle does", () => {
    const bo = businessObject("bpmn:Task", "t1");
    const { handle } = fakeModeler([element(bo)]);
    handle.modeler.get("modeling").updateProperties(element(bo), { "autonate:dataType": "string" });

    handle.modeler.get("modeling").updateProperties(element(bo), { "autonate:dataType": null });

    // The old version asserted the opposite and was believed. Real moddle keeps
    // the key with a null value, which serialises as the STRING "null" --
    // #411's second live consequence.
    expect("autonate:dataType" in bo.$attrs).toBe(true);
    expect(bo.$attrs["autonate:dataType"]).toBeNull();
  });

  it("only `undefined` removes a key", () => {
    const bo = businessObject("bpmn:Task", "t1");
    const { handle } = fakeModeler([element(bo)]);
    handle.modeler.get("modeling").updateProperties(element(bo), { "autonate:dataType": "string" });

    handle.modeler.get("modeling").updateProperties(element(bo), { "autonate:dataType": undefined });

    expect(bo.$attrs["autonate:dataType"]).toBeUndefined();
  });

  it("writes moddle properties onto the nested object, not the business object", () => {
    const loop = moddleElement("bpmn:MultiInstanceLoopCharacteristics");
    const bo = businessObject("bpmn:Task", "t1", { loopCharacteristics: loop });
    const { handle } = fakeModeler([element(bo)]);

    handle.modeler.get("modeling").updateModdleProperties(element(bo), loop, { isSequential: true });

    expect(loop.isSequential).toBe(true);
    expect(bo.isSequential).toBeUndefined();
  });
});

/**
 * The npm moddle and the SHIPPED bundle agree (#411).
 *
 * The fake gets its routing from `bpmn-moddle` on npm. The studio runs the
 * vendored `public/vendor/bpmn-js/bpmn-modeler.development.js`, which embeds its
 * own copy. Those are two copies of one library, and a version skew between them
 * would put the fake back where #411 found it: confidently answering a question
 * about a library the product does not use.
 *
 * So this asks both, on the cases the studio actually depends on, and fails if
 * they disagree. It reads the bundle as text and evaluates it, because the
 * bundle exports only the Modeler and constructing one needs a DOM.
 */
describe("the fake's library and the shipped bundle answer the same way", () => {
  it("routes the studio's own keys identically in both copies", async () => {
    const { readFileSync } = await import("node:fs");
    const path = await import("node:path");
    const { fileURLToPath } = await import("node:url");

    const here = path.dirname(fileURLToPath(import.meta.url));
    const bundlePath = path.join(
      here, "..", "..", "..", "..", "public", "vendor", "bpmn-js", "bpmn-modeler.development.js");

    const source = readFileSync(bundlePath, "utf8");

    // The bundle keeps SimpleBpmnModdle internal; expose it alongside the
    // existing default export rather than rewriting the file.
    const exposed = source.replace(
      "    default: () => bpmn_entry_default",
      "    default: () => bpmn_entry_default,\n    SimpleBpmnModdle: () => SimpleBpmnModdle");

    expect(
      exposed,
      "The bundle's export block changed shape, so this cross-check is evaluating "
      + "nothing. Re-point it before trusting the fake again (#411)."
    ).not.toBe(source);

    const bundled = new Function(`${exposed}\nreturn __AutoNateBpmnJS__;`)();
    const Vendored = bundled.SimpleBpmnModdle;
    expect(typeof Vendored).toBe("function");

    const { BpmnModdle } = await import("bpmn-moddle");

    const cases = [
      ["bpmn:ScriptTask", "resultVariable", "out"],      // undeclared, bare
      ["bpmn:UserTask", "name", "Approve"],              // declared, bare
      ["bpmn:Task", "autonate:dataType", "string"],      // undeclared, namespaced
      ["bpmn:MultiInstanceLoopCharacteristics", "isSequential", true]
    ];

    for (const [type, key, value] of cases) {
      const fromNpm = new BpmnModdle().create(type, {});
      const fromBundle = new Vendored().create(type, {});
      fromNpm.set(key, value);
      fromBundle.set(key, value);

      const npmWhere = fromNpm[key] === value ? "direct" : "attrs";
      const bundleWhere = fromBundle[key] === value ? "direct" : "attrs";

      expect(
        bundleWhere,
        `bpmn-moddle on npm and the vendored bundle disagree about ${type}.${key}: `
        + `npm says ${npmWhere}, the bundle says ${bundleWhere}. The fake follows npm, `
        + "so it is now answering for a library the studio does not run (#411)."
      ).toBe(npmWhere);
    }
  });
});
