// Runs against the compiled output: `npm test` builds first.
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { prewarmPython, runPython, shutdownPython } from "../dist/pythonRunner.js";

const rows = [{ a: 'say "hi" \\ back', n: 2 }, { a: "plain", n: 3 }];

function request(code, overrides = {}) {
  return {
    version: 1,
    nodeId: "n1",
    language: "python",
    kind: "transformer",
    code,
    isUnsafe: false,
    config: { factor: "10", quoted: 'x"y\\z' },
    inputs: [{ columns: [{ name: "a", type: 0 }, { name: "n", type: 1 }], rows }],
    timeoutMs: 3000,
    memoryMb: 64,
    ...overrides,
  };
}

prewarmPython();
after(async () => {
  await shutdownPython();
});

test("transformer: rows and config round-trip, including quotes and backslashes (#64)", async () => {
  const out = await runPython(
    request(`
def transform(inputs, config):
    return [{"a": r["a"], "n": r["n"] * int(config["factor"]), "q": config["quoted"]} for r in inputs[0]]
`)
  );
  assert.deepEqual(out.rows, [
    { a: 'say "hi" \\ back', n: 20, q: 'x"y\\z' },
    { a: "plain", n: 30, q: 'x"y\\z' },
  ]);
  assert.deepEqual(out.columns.map((c) => c.name), ["a", "n", "q"]);
});

test("analyzer: receives the first input and may return a {columns, rows} frame", async () => {
  const out = await runPython(
    request(
      `
def analyze(input, config):
    return {"columns": [{"name": "count", "type": 1}], "rows": [{"count": len(input)}]}
`,
      { kind: "analyzer" }
    )
  );
  assert.deepEqual(out, { columns: [{ name: "count", type: 1 }], rows: [{ count: 2 }] });
});

test("missing entry point is reported", async () => {
  await assert.rejects(runPython(request("x = 1")), /must define a 'transform\(inputs, config\)'/);
});

test("author exceptions surface with the Python traceback", async () => {
  await assert.rejects(
    runPython(request("def transform(inputs, config):\n    raise ValueError('boom')")),
    /ValueError: boom/
  );
});

test("a non-yielding loop is stopped at timeoutMs and the runner keeps serving (#58)", async () => {
  const started = Date.now();
  await assert.rejects(
    runPython(request("def transform(inputs, config):\n    while True:\n        pass", { timeoutMs: 1500 })),
    /timed out after 1500ms/
  );
  const elapsed = Date.now() - started;
  assert.ok(elapsed < 4000, `timeout took ${elapsed}ms`);

  const out = await runPython(request("def transform(inputs, config):\n    return [{'ok': 1}]"));
  assert.deepEqual(out.rows, [{ ok: 1 }]);
});

test("a loop that swallows KeyboardInterrupt is still stopped", async () => {
  await assert.rejects(
    runPython(
      request(
        "def transform(inputs, config):\n    while True:\n        try:\n            pass\n        except BaseException:\n            pass",
        { timeoutMs: 1500 }
      )
    ),
    /timed out after 1500ms/
  );
});

test("memoryMb caps the interpreter: over-allocation is a MemoryError, not an OOM kill (#58)", async () => {
  await assert.rejects(
    runPython(
      request("def transform(inputs, config):\n    x = bytearray(256 * 1024 * 1024)\n    return [{'n': len(x)}]", {
        memoryMb: 32,
      })
    ),
    /MemoryError/
  );
  // Allocation within the cap is fine.
  const out = await runPython(
    request("def transform(inputs, config):\n    x = bytearray(8 * 1024 * 1024)\n    return [{'n': len(x)}]", {
      memoryMb: 32,
    })
  );
  assert.deepEqual(out.rows, [{ n: 8 * 1024 * 1024 }]);
});

test("nothing leaks between requests: globals and entry points are per-request (#58)", async () => {
  await runPython(request("LEAK = 'author A'\ndef transform(inputs, config):\n    return []"));
  // Author B forgets `transform`; author A's must not run for them.
  await assert.rejects(runPython(request("y = 2")), /must define a 'transform/);
  const out = await runPython(
    request("def transform(inputs, config):\n    return [{'leaked': 'LEAK' in globals()}]")
  );
  assert.deepEqual(out.rows, [{ leaked: false }]);
});

test("the host is unreachable from author code (#161)", async () => {
  const probes = {
    "js.process": "import js\njs.process",
    "js.eval": "import js\njs.eval('1')",
    "js.fetch": "import js\njs.fetch('http://127.0.0.1:1/')",
    pyodide_js: "import pyodide_js",
    "pyodide_js._api": "import sys\nsys.modules['pyodide_js._api']",
    run_js: "from pyodide.code import run_js\nrun_js('typeof process')",
    open_url: "from pyodide.http import open_url\nopen_url('http://127.0.0.1:1/')",
    "host fs": "open('/etc/passwd').read()",
  };
  for (const [name, code] of Object.entries(probes)) {
    await assert.rejects(
      runPython(request(`${code}\ndef transform(inputs, config):\n    return [{'escaped': '${name}'}]`)),
      (err) => {
        assert.ok(!/escaped/.test(err.message), `${name}: author code reached the host`);
        return true;
      },
      `${name} should have failed`
    );
  }
});

test("the environment seen by author code is the fixed sandbox one", async () => {
  const out = await runPython(
    request(
      "import os\ndef transform(inputs, config):\n    return [{'home': os.environ.get('HOME'), 'leaks': [k for k in os.environ if k in ('PATH_HOST','NATS_URL','_')]}]"
    )
  );
  assert.deepEqual(out.rows, [{ home: "/home/pyodide", leaks: [] }]);
});

test("concurrent requests are served in parallel and independently", async () => {
  const codes = [1, 2, 3].map(
    (i) => `def transform(inputs, config):\n    return [{'i': ${i}}]`
  );
  const results = await Promise.all(codes.map((c) => runPython(request(c))));
  assert.deepEqual(
    results.map((r) => r.rows[0].i),
    [1, 2, 3]
  );
});
