// Runs against the compiled output: `npm test` builds first.
import { test } from "node:test";
import assert from "node:assert/strict";
import { runScriptTask } from "../dist/scriptTaskRunner.js";
import { HostApiRegistry, defaultHostApi } from "../dist/hostApi.js";

function request(code, variables = {}, overrides = {}) {
  return {
    version: 1,
    nodeId: "script-1",
    language: "js",
    kind: "scripttask",
    code,
    isUnsafe: false,
    config: {},
    inputs: [],
    variables,
    timeoutMs: 3000,
    memoryMb: 64,
    ...overrides,
  };
}

test("variables.get reads process variables the host sent", async () => {
  const out = await runScriptTask(request(`return variables.get("orderTotal");`, { orderTotal: 42 }));
  assert.equal(out.result, 42);
});

test("variables.set is returned as a mutation for the host to apply", async () => {
  const out = await runScriptTask(request(`variables.set("approved", true);`, {}));
  assert.deepEqual(out.mutations, { approved: true });
});

test("a script sees its own write before it returns", async () => {
  // Without the mutation overlay in variables.get, a set followed by a get in
  // one script would read the stale snapshot.
  const out = await runScriptTask(
    request(`variables.set("n", 5); return variables.get("n");`, { n: 1 })
  );
  assert.equal(out.result, 5);
});

test("values round-trip for the types Flowable supports", async () => {
  const vars = {
    s: "text",
    b: true,
    i: 7,
    d: 1.5,
    nested: { a: [1, 2, { deep: "yes" }] },
    nul: null,
  };
  const out = await runScriptTask(request(`return variables.get("nested").a[2].deep;`, vars));
  assert.equal(out.result, "yes");

  const echo = await runScriptTask(
    request(`for (const k of ["s","b","i","d","nested","nul"]) variables.set(k, variables.get(k));`, vars)
  );
  assert.deepEqual(echo.mutations, vars);
});

test("a script with no return produces a null result rather than undefined", async () => {
  // JSON has no undefined; the host applies `resultVariable` from this value,
  // so it has to be representable.
  const out = await runScriptTask(request(`variables.set("x", 1);`));
  assert.equal(out.result, null);
});

// --- the boundary ---------------------------------------------------------
//
// Each of these asserts a separate escape route is closed. One blocked path
// does not prove a closed boundary, which is why they are not folded into one
// test with an alternation.

test("author code cannot reach require", async () => {
  const out = await runScriptTask(request(`return typeof require;`));
  assert.equal(out.result, "undefined");
});

test("author code cannot reach process", async () => {
  const out = await runScriptTask(request(`return typeof process;`));
  assert.equal(out.result, "undefined");
});

test("author code cannot reach fetch or any network primitive", async () => {
  const out = await runScriptTask(
    request(`return [typeof fetch, typeof XMLHttpRequest, typeof WebSocket].join(",");`)
  );
  assert.equal(out.result, "undefined,undefined,undefined");
});

test("author code cannot reach a module loader", async () => {
  // `import.meta` is a syntax error outside a module and dynamic `import` has
  // no resolver in the isolate. Either way the script fails rather than
  // reaching one; the refusal is the assertion.
  await assert.rejects(() => runScriptTask(request(`return import.meta.url;`)));
  await assert.rejects(() => runScriptTask(request(`return import("node:fs");`)));
});

test("__state is reachable but exposes nothing the façade does not", async () => {
  // Recorded rather than asserted away: the façade closes over `__state`
  // inside the isolate, so author code can see it. That is not an escalation —
  // reading `__state.variables` is what `variables.get` does, and writing
  // `__state.mutations` is what `variables.set` does. It is worth a test so
  // that if the object ever starts carrying something the façade does not
  // expose, this fails and forces the question.
  const out = await runScriptTask(
    request(`return Object.keys(__state).sort().join(",");`, { a: 1 })
  );
  assert.equal(out.result, "mutations,variables");
});

test("a runaway script is stopped by the timeout rather than hanging the executor", async () => {
  await assert.rejects(
    () => runScriptTask(request(`while (true) {}`, {}, { timeoutMs: 300 })),
    /time|Script execution timed out/i
  );
});

test("a script that throws surfaces the error rather than succeeding silently", async () => {
  await assert.rejects(
    () => runScriptTask(request(`throw new Error("author mistake");`)),
    /author mistake/
  );
});

// --- the registry ---------------------------------------------------------

test("a newly registered operation becomes reachable without touching the transport", async () => {
  // The point of the registry: a helper is an addition here, not a change to
  // the runner or the wire format.
  const registry = defaultHostApi().register({
    namespace: "helpers",
    name: "double",
    description: "Doubles a number.",
    parameters: [{ name: "n", type: "any", description: "The number.", required: true }],
    source: `function (n) { return n * 2; }`,
  });
  const out = await runScriptTask(request(`return helpers.double(21);`), registry);
  assert.equal(out.result, 42);
});

test("the host API surface is describable as tool definitions", async () => {
  const tools = defaultHostApi().toToolDefinitions();
  const names = tools.map((t) => t.name).sort();
  assert.deepEqual(names, ["variables_get", "variables_set"]);

  const set = tools.find((t) => t.name === "variables_set");
  assert.equal(set.parameters.type, "object");
  assert.deepEqual(set.parameters.required, ["name", "value"]);
  assert.ok(set.description.length > 0);
});

test("tool definitions are generated from the registry, not written twice", async () => {
  // If they were maintained separately they would drift; registering an
  // operation must change both the façade and the tool list.
  const registry = defaultHostApi().register({
    namespace: "helpers",
    name: "noop",
    description: "Does nothing.",
    parameters: [],
    source: `function () { return null; }`,
  });
  assert.ok(registry.toToolDefinitions().some((t) => t.name === "helpers_noop"));
  assert.ok(registry.buildFacadeSource().includes("helpers"));
});

test("a namespace or name that is not an identifier is refused", async () => {
  // These are interpolated into generated source, so a non-identifier is a
  // code-injection vector rather than a typo.
  assert.throws(
    () =>
      new HostApiRegistry().register({
        namespace: "x = 1; const y",
        name: "go",
        description: "",
        parameters: [],
        source: `function () {}`,
      }),
    /valid identifier/
  );
});

test("registering the same operation twice is refused", async () => {
  assert.throws(
    () =>
      defaultHostApi().register({
        namespace: "variables",
        name: "get",
        description: "",
        parameters: [],
        source: `function () {}`,
      }),
    /already registered/
  );
});
