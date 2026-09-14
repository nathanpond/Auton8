import { describe, it, expect } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const workflowSource = readFileSync(path.join(here, "..", "workflow.js"), "utf8");

/**
 * The meta-guard (#323).
 *
 * Written first and run against the pre-#323 tree on purpose: it reported six
 * uncovered multi-instance properties before a single round-trip test existed.
 * A test plan that only adds coverage cannot tell you when coverage STOPS, and
 * #159 shipped with exactly one of seven properties asserted because nobody had
 * a list.
 *
 * The list is DERIVED from `workflow.js`, not written here. Three literal lists
 * went stale during M4 and every one of them went stale silently -- so this
 * reads the properties the studio actually writes and demands a round-trip test
 * for each. A property added tomorrow fails here tomorrow.
 */

/** Every `autonate:`-prefixed property name `workflow.js` writes. */
function propertiesWritten() {
  const names = new Set();

  // writeAutoNateAttribute(<any receiver>, "aggregateTarget", ...)
  //
  // #409: this required a BARE identifier, so `writeAutoNateAttribute(
  // element.businessObject, ...)` was invisible -- and four real properties were
  // uncovered behind that: scriptFormat, routeScript, runAs, autonateConvertedFrom.
  // The fix for #411 then wrote resultVariable through the same dotted shape, so
  // this guard would have missed its own round's work.
  //
  // `[^,"]+` for the receiver: any expression that is not a comma or a quote,
  // which is every receiver in this file and cannot swallow the name argument.
  for (const m of workflowSource.matchAll(/writeAutoNateAttribute\(\s*[^,"]+,\s*"([^"]+)"/g)) {
    names.add(m[1]);
  }
  // [`${AUTONATE_ATTR_PREFIX}completionCondition`]: ...
  for (const m of workflowSource.matchAll(/\$\{AUTONATE_ATTR_PREFIX\}([A-Za-z0-9_]+)/g)) {
    names.add(m[1]);
  }
  // writeFlowableAttribute(loop, "collection", ...) -- the third mechanism, and
  // the one the first draft of this scan missed. Two of the seven multi-instance
  // properties travel it, which is precisely why the story says the three
  // mechanisms cannot stand in for each other.
  for (const m of workflowSource.matchAll(/writeFlowableAttribute\(\s*[^,"]+,\s*"([^"]+)"/g)) {
    names.add(m[1]);
  }
  // "flowable:collection"-style writes through the modeling API.
  for (const m of workflowSource.matchAll(/"flowable:([A-Za-z0-9_]+)"\s*:/g)) {
    names.add(m[1]);
  }

  // #409, third part: BARE keys through modeling.updateProperties and
  // modeling.updateModdleProperties -- the two mechanisms the first three
  // versions of this scan could not see. `isSequential`, the seventh of the
  // seven multi-instance properties, had to be hard-coded into the required
  // list by hand because of this, and that hand-coding was the signal.
  // TWO shapes, because the first version only matched the multi-line one and a
  // single-line `updateProperties(element, { name: x, other: y })` walked past
  // it -- caught by mutation, which is the only reason this line is here.
  const modelingCalls = [
    // multi-line: the literal closes on its own line, so nesting inside it is fine
    /update(?:Moddle)?Properties\([^,]+,\s*(?:[^,]+,\s*)?\{(.*?)\n\s*\}\)/gs,
    // single-line: no newline in the literal at all
    /update(?:Moddle)?Properties\([^,]+,\s*(?:[^,]+,\s*)?\{([^{}\n]*)\}\s*\)/g
  ];

  for (const pattern of modelingCalls) {
    for (const call of workflowSource.matchAll(pattern)) {
      for (const key of call[1].matchAll(/(?:^|[{,])\s*([A-Za-z_][A-Za-z0-9_]*)\s*:/gm)) {
        names.add(key[1]);
      }
    }
  }

  return [...names].sort();
}

function testSources() {
  const sources = readdirSync(here)
    .filter((f) => f.endsWith(".test.js") && f !== "property-coverage.test.js")
    .map((f) => readFileSync(path.join(here, f), "utf8"))
    .join("\n");

  // #409: COMMENTS STRIPPED. `tests.includes(name)` is a substring search, so
  // appending `// zzzProbe` to any test file satisfied it with zero assertions
  // written -- the same loose-oracle shape as #321's Contains("start event") and
  // #393's comment bypass, in a third place. A mention is not a test; a mention
  // in a comment is not even a mention.
  return sources
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .split("\n")
    .map((line) => line.replace(/\/\/.*$/, ""))
    .join("\n");
}

describe("the property list is derived, not declared", () => {
  it("finds the properties the studio writes", () => {
    const found = propertiesWritten();

    // A floor, because a regex that stops matching reports a clean tree forever
    // -- the failure this milestone has found more than any other.
    expect(found.length).toBeGreaterThan(6);

    // The seven the story names explicitly, because they travel three different
    // mechanisms and cannot stand in for one another.
    for (const required of [
      "collection",
      "elementVariable",
      "completionCondition",
      "loopCardinality",
      "aggregateTarget",
      "aggregateSource"
    ]) {
      expect(found).toContain(required);
    }
  });
});

describe("every property the studio writes has a round-trip test", () => {
  it("names no property that no test mentions", () => {
    const tests = testSources();

    // `isSequential` no longer needs naming here -- the scan reaches
    // updateModdleProperties now (#409). Kept in the set as a floor: if the new
    // branch ever stops matching, this one property still has to be covered, and
    // its absence from `propertiesWritten()` would be the tell.
    const required = [...new Set([...propertiesWritten(), "isSequential"])];

    // Structural keys bpmn-js's own modelling sets, not author-facing properties
    // the studio round-trips. `id` and `attachedTo` are identity and attachment;
    // `cancelActivity` and the *Ref keys are how a definition points at a root
    // the studio created for it. Each is exercised by the E2E studio suite
    // through the modeller rather than through a property panel.
    const structural = new Set([
      "id", "attachedTo", "cancelActivity", "signalRef", "errorRef", "escalationRef"
    ]);

    const uncovered = required
      .filter((name) => !structural.has(name))
      .filter((name) => !tests.includes(name))
      .sort();

    expect(
      uncovered,
      `These properties are written by workflow.js and asserted by no test:\n  ${uncovered.join("\n  ")}\n\n` +
        "Add a round-trip test -- write it through the studio's own update function, " +
        "read it back through the studio's own describe function. #159 shipped with " +
        "one of seven asserted because nobody had this list (#323)."
    ).toEqual([]);
  });
});
