// Runs against the compiled output: `npm test` builds first.
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { runScriptTask } from "../dist/scriptTaskRunner.js";
import { prewarmPython, runPythonScriptTask, shutdownPython } from "../dist/pythonRunner.js";

// #154's central claim: a language is a front-end, not a second execution path.
//
// If JavaScript and Python can reach different things, the gate is not on the
// host API and the design has failed — so the same probe set runs under both
// and the verdicts must match. Writing the assertions per-language would let
// the two drift silently, which is the failure this file exists to prevent.

prewarmPython();
after(async () => {
  await shutdownPython();
});

function request(language, code, variables = {}) {
  return {
    version: 1,
    nodeId: "n1",
    language,
    kind: "scripttask",
    code,
    config: {},
    inputs: [],
    variables,
    timeoutMs: 15000,
    memoryMb: 128,
  };
}

const run = (language, code, variables) =>
  language === "js"
    ? runScriptTask(request(language, code, variables))
    : runPythonScriptTask(request(language, code, variables));

// Each probe is the *same question* asked in both languages. `blocked: true`
// means the sandbox must refuse it; the verdicts are compared, not asserted
// separately, so a divergence fails rather than being described.
const PROBES = [
  {
    name: "read a process variable",
    blocked: false,
    js: `return variables.get('total');`,
    py: `return variables.get('total')`,
    variables: { total: 7 },
    expect: 7,
  },
  {
    name: "write a process variable",
    blocked: false,
    js: `variables.set('out', 3);`,
    py: `variables.set('out', 3)`,
    mutation: ["out", 3],
  },
  {
    name: "a script sees its own write",
    blocked: false,
    js: `variables.set('n', 5); return variables.get('n');`,
    py: `variables.set('n', 5)\nreturn variables.get('n')`,
    variables: { n: 1 },
    expect: 5,
  },
  {
    name: "nested values round-trip",
    blocked: false,
    js: `return variables.get('o').a;`,
    py: `return variables.get('o')['a']`,
    variables: { o: { a: "deep" } },
    expect: "deep",
  },
  { name: "filesystem", blocked: true, js: `return fs.readFileSync('/etc/passwd');`, py: `open('/etc/passwd')` },
  { name: "process / operating system", blocked: true, js: `return process.env;`, py: `import os\n    return os.environ` },
  { name: "spawning a process", blocked: true, js: `return child_process.exec('ls');`, py: `import subprocess\n    return subprocess.run(['ls'])` },
  { name: "network — sockets", blocked: true, js: `return new WebSocket('ws://x');`, py: `import socket\n    return socket.socket()` },
  { name: "network — http", blocked: true, js: `return fetch('http://x');`, py: `import urllib.request\n    return urllib.request.urlopen('http://x')` },
  { name: "module loading", blocked: true, js: `return require('fs');`, py: `import importlib\n    return importlib.import_module('os')` },
  { name: "host runtime", blocked: true, js: `return Java.type('java.lang.System');`, py: `import ctypes\n    return ctypes.CDLL(None)` },
];

async function verdict(language, probe) {
  try {
    const out = await run(language, language === "js" ? probe.js : probe.py, probe.variables ?? {});
    return { blocked: false, out };
  } catch (e) {
    return { blocked: true, error: e instanceof Error ? e.message : String(e) };
  }
}

// #231. The scoped write, in both languages. Without the parity rows a Python
// script task would silently lack the surface a JavaScript one has, which is
// exactly the drift this file exists to prevent.
PROBES.push(
  {
    name: "write a block-scoped process variable",
    blocked: false,
    js: `variables.setLocal('arrived', ['b1']); return 'ok';`,
    py: `variables.set_local('arrived', ['b1'])\nreturn 'ok'`,
    expect: "ok",
    localMutation: ["arrived", ["b1"]],
  },
  {
    name: "a scoped write is visible to a later read in the same script",
    blocked: false,
    js: `variables.setLocal('n', 1); return variables.get('n');`,
    py: `variables.set_local('n', 1)\nreturn variables.get('n')`,
    expect: 1,
  },
  {
    name: "a scoped value shadows an instance-scoped one of the same name",
    blocked: false,
    js: `variables.setLocal('n', 2); return variables.get('n');`,
    py: `variables.set_local('n', 2)\nreturn variables.get('n')`,
    variables: { n: 99 },
    expect: 2,
  }
);

for (const probe of PROBES) {
  test(`parity — ${probe.name}`, async () => {
    const js = await verdict("js", probe);
    const py = await verdict("python", probe);

    assert.equal(
      js.blocked,
      py.blocked,
      `JavaScript and Python disagree on "${probe.name}": ` +
        `js ${js.blocked ? "blocked" : "allowed"}, python ${py.blocked ? "blocked" : "allowed"}. ` +
        `A language must be a front-end onto one surface, not a second way in. ` +
        `js=${js.error ?? JSON.stringify(js.out)} py=${py.error ?? JSON.stringify(py.out)}`
    );
    assert.equal(js.blocked, probe.blocked, `expected "${probe.name}" to be ${probe.blocked ? "blocked" : "allowed"} in js`);

    if (!probe.blocked && probe.expect !== undefined) {
      assert.deepEqual(js.out.result, probe.expect);
      assert.deepEqual(py.out.result, probe.expect, "both languages must return the same value");
    }
    if (!probe.blocked && probe.mutation) {
      const [name, value] = probe.mutation;
      assert.deepEqual(js.out.mutations[name], value);
      assert.deepEqual(py.out.mutations[name], value, "both languages must record the same mutation");
    }
    // #231. The scoped bag is asserted separately AND the ordinary bag is
    // asserted empty: a runner that quietly folded setLocal back into
    // `mutations` would satisfy "the value came back" while writing it to the
    // whole process, which is the bug the scope exists to prevent.
    if (!probe.blocked && probe.localMutation) {
      const [name, value] = probe.localMutation;
      assert.deepEqual(js.out.localMutations[name], value);
      assert.deepEqual(
        py.out.localMutations[name], value,
        "both languages must record the same scoped mutation");
      assert.deepEqual(js.out.mutations, {}, "a scoped write must not also land in the process-wide bag");
      assert.deepEqual(py.out.mutations, {}, "a scoped write must not also land in the process-wide bag");
    }
  });
}

test("the host surface is reached by the same names in both languages", async () => {
  // Not just "both can read a variable" — both must do it through
  // `variables.get`. A Python-specific binding that happened to work would be
  // exactly the divergence this story tests for.
  const js = await runScriptTask(request("js", `return typeof variables.get === 'function' && typeof variables.set === 'function';`));
  const py = await runPythonScriptTask(request("python", `return callable(variables.get) and callable(variables.set)`));
  assert.equal(js.result, true);
  assert.equal(py.result, true);
});
