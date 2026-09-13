import { describe, it, expect } from "vitest";
import { interpretSignalScope, updateSignalElementProperties } from "../workflow.js";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

/**
 * Signal scope: all four states, and the write path (#323, #311, #317).
 *
 * #311 found the panel silently rewriting a mistyped scope, defeating the
 * backend's refusal one layer up. Its replacement guard (#317) turned out to be
 * dead code -- the value is coerced two lines above the check -- and nothing
 * could run it, which is the argument for this whole milestone in one example.
 */
describe("interpretSignalScope names all four states", () => {
  it.each([
    ["instance", "instance"],
    ["processInstance", "instance"],
    ["  INSTANCE  ", "instance"],
    ["global", "global"],
    ["GLOBAL", "global"],
    ["", "unspecified"],
    ["   ", "unspecified"],
    [null, "unspecified"],
    [undefined, "unspecified"],
    ["gobal", "unrecognised"],
    ["everywhere", "unrecognised"]
  ])("reads %p as %s", (raw, expected) => {
    expect(interpretSignalScope(raw)).toBe(expected);
  });

  it("does not collapse an unrecognised scope into either real one", () => {
    // The #311 defect exactly: collapsing to "global" silently WIDENS a signal,
    // which is the one direction that cannot be safe.
    expect(interpretSignalScope("gobal")).not.toBe("global");
    expect(interpretSignalScope("gobal")).not.toBe("instance");
  });
});

describe("the panel never writes back a scope it does not understand", () => {
  function signalEvent(id = "sig1") {
    const definition = { $type: "bpmn:SignalEventDefinition", $attrs: {} };
    return businessObject("bpmn:IntermediateThrowEvent", id, { eventDefinitions: [definition] });
  }

  it("refuses a mistyped scope instead of silently narrowing or widening it", () => {
    const bo = signalEvent();
    const { handle } = fakeModeler([element(bo)]);

    // #317: this is the assertion that was unreachable. `scope` was coerced to
    // "instance" two lines above the guard, so a typo was silently NARROWED --
    // safer than #311's widening, and still a data change nobody asked for,
    // with the backend's refusal never seeing the typo.
    expect(() =>
      updateSignalElementProperties(handle, { id: "sig1", signalName: "orderPlaced", scope: "gobal" })
    ).toThrow(/does not understand/);
  });

  it("defaults an unspecified scope to instance, the narrow one", () => {
    const bo = signalEvent();
    const { handle } = fakeModeler([element(bo)]);

    updateSignalElementProperties(handle, { id: "sig1", signalName: "orderPlaced", scope: "" });

    // A signal nobody scoped should reach its own instance, not every instance.
    // #317's fix moved the interpretation ahead of the coercion, so this is now
    // a decision the code makes rather than a side effect of the old ternary.
    const written = (bo.extensionElements?.values ?? [])
      .find((v) => v?.$type === "flowable:autonateSignalScope");
    expect(written?.value).toBe("instance");
  });

  it.each(["instance", "global"])("accepts %s, which it does understand", (scope) => {
    const bo = signalEvent();
    const { handle } = fakeModeler([element(bo)]);

    expect(() =>
      updateSignalElementProperties(handle, { id: "sig1", signalName: "orderPlaced", scope })
    ).not.toThrow();
  });
});
