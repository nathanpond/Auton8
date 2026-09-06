import { parentPort, workerData } from "node:worker_threads";
import { loadPyodide, type PyodideInterface } from "pyodide";
import type { PythonJob, PythonWorkerMessage } from "./pythonProtocol.js";

// Worker-thread entry for one Python request. The parent (pythonRunner.ts)
// spawns a fresh worker per request — a warm one is usually waiting — so
// nothing an author does here (globals, monkey-patching, leftover state)
// survives into the next request. The parent owns the deadline: it raises
// SIGINT through the shared interrupt buffer and hard-terminates the
// thread if the script does not stop, which is the only way to stop
// blocking WASM. Both are archived-58.
//
// Sandbox hardening (archived-161): Pyodide's `js` module is bound to
// `jsglobals`, which we make an empty object so `js.process`, `js.eval`,
// `js.fetch`… do not exist; `pyodide_js` is unregistered (its
// `loadPackage(url)` is a real network fetch and `FS.mount(NODEFS)` is the
// host filesystem); `fetch`/`WebSocket` are removed from this thread's
// globals after load as a backstop. What remains is Pyodide's in-memory
// filesystem and a fixed fake environment.

const MiB = 1024 * 1024;

// Memory limit (archived-58). Pyodide's linear memory is a WebAssembly.Memory
// that Emscripten grows on demand; refusing the grow makes malloc fail and
// surfaces in Python as a plain MemoryError, leaving the interpreter
// usable. The cap is baseline-after-load + request.memoryMb so the
// configured number means "what the script may allocate".
let limitBytes = Number.POSITIVE_INFINITY;
const originalGrow = WebAssembly.Memory.prototype.grow;
WebAssembly.Memory.prototype.grow = function grow(this: WebAssembly.Memory, pages: number) {
  const next = this.buffer.byteLength + pages * 65536;
  if (next > limitBytes) {
    throw new RangeError("executor memory limit exceeded");
  }
  return originalGrow.call(this, pages);
};

function post(message: PythonWorkerMessage): void {
  parentPort!.postMessage(message);
}

function wasmBytes(py: PyodideInterface): number {
  const module = (py as unknown as { _module?: { HEAPU8?: Uint8Array } })._module;
  return module?.HEAPU8?.byteLength ?? 0;
}

function formatError(err: unknown): string {
  const text = err instanceof Error ? err.message : String(err);
  // Pyodide's PythonError message is the full traceback; keep it (authors
  // need the line) but bound it.
  return text.length > 4000 ? `${text.slice(0, 4000)}…` : text;
}

// Modules a BPMN script task may not import (#154).
//
// The JavaScript sandbox has no filesystem, no process and no network to
// withhold — they simply are not there. Python is different: Pyodide ships a
// real CPython, so `import os` and `import socket` SUCCEED, and `open()` reads
// its in-memory filesystem. Their capabilities are heavily curtailed already,
// but "curtailed" is not the same claim as "unreachable", and the story's
// requirement is that the two front-ends reach the same surface — not that one
// is merely harder to misuse than the other.
//
// So for script tasks these are refused outright, which is what makes the
// parity assertion true rather than approximately true.
const ScriptTaskDeniedModules = [
  "os", "subprocess", "socket", "shutil", "ctypes", "multiprocessing",
  "threading", "urllib", "http", "ssl", "pathlib", "tempfile", "glob",
  "importlib", "sysconfig", "platform", "webbrowser", "pty", "signal",
];

// The `variables` façade plus the import and filesystem guards. Kept separate
// from the author's code so the guard is installed before that code runs and
// cannot be edited by it.
function scriptTaskPreamble(): string {
  const denied = ScriptTaskDeniedModules.map((m) => `"${m}"`).join(", ");
  // Names here are single-underscore-prefixed on purpose. Python mangles a
  // double-underscore name referenced inside a class body to
  // `_ClassName__name`, so `__mutations` read from within `__Variables` becomes
  // `_Variables__mutations` and fails with a NameError that points nowhere near
  // the cause.
  return `import json as __json
import builtins as _an8_builtins
import sys as _an8_sys

_an8_vars = __json.loads(__variables_json)
_an8_mutations = {}
del __variables_json

class _An8Variables:
    def get(self, name):
        # Reads see writes made earlier in this same script, or a set followed
        # by a get would return the stale snapshot.
        if name in _an8_mutations:
            return _an8_mutations[name]
        return _an8_vars.get(name)

    def set(self, name, value):
        if not isinstance(name, str) or not name:
            raise ValueError("variables.set requires a non-empty variable name.")
        _an8_mutations[name] = value
        return value

variables = _An8Variables()

_an8_denied = {${denied}}

class _An8DenyImports:
    def find_module(self, name, path=None):
        return self.find_spec(name, path)

    def find_spec(self, name, path=None, target=None):
        root = name.split(".")[0]
        if root in _an8_denied:
            raise ImportError(
                "'" + root + "' is not available to script tasks: scripts run in a sandbox "
                "with no access to the operating system, the filesystem or the network."
            )
        return None

# Purge anything already imported, then refuse future imports. Both are needed:
# the finder does not help against a module already in sys.modules.
for _an8_k in [k for k in _an8_sys.modules if k.split(".")[0] in _an8_denied]:
    del _an8_sys.modules[_an8_k]
_an8_sys.meta_path.insert(0, _An8DenyImports())

def _an8_denied_open(*args, **kwargs):
    raise PermissionError(
        "open() is not available to script tasks: scripts run in a sandbox with no filesystem."
    )

_an8_builtins.open = _an8_denied_open
`;
}

function wrapper(job: PythonJob): string {
  // `__inputs_json` / `__config_json` are set on the Python globals by the
  // worker (never spliced into source — archived-64). Entry-point check runs in a
  // fresh interpreter, so "defined by a previous author" cannot happen.
  const entry =
    job.kind === "transformer"
      ? `
if "transform" not in globals():
    raise RuntimeError("Python transformer must define a 'transform(inputs, config)' function.")
__result = transform(__inputs, __config)`
      : `
if "analyze" not in globals():
    raise RuntimeError("Python analyzer must define an 'analyze(input, config)' function.")
__result = analyze(__inputs[0], __config)`;
  if (job.kind === "scripttask") {
    // The author writes bare statements, as a BPMN script task does. Python has
    // no top-level `return`, so the body is indented into a function to keep the
    // surface identical to JavaScript's rather than making Python authors write
    // something different for the same job.
    const body = job.code
      .split("\n")
      .map((line) => (line.trim() === "" ? line : `    ${line}`))
      .join("\n");
    return `${scriptTaskPreamble()}
def __script():
${body}

__result = __script()
__json.dumps({"result": __result, "mutations": _an8_mutations}, default=str)
`;
  }

  return `import json as __json
__inputs = __json.loads(__inputs_json)
__config = __json.loads(__config_json)
del __inputs_json, __config_json

${job.code}
${entry}
__json.dumps(__result, default=str)
`;
}

async function main(): Promise<void> {
  const interrupt = new Uint8Array(workerData.interrupt as SharedArrayBuffer);

  const py = await loadPyodide({
    stdout: () => undefined,
    stderr: () => undefined,
    // Empty `js` module: no host globals reachable from Python (archived-161).
    jsglobals: Object.create(null),
    // Fixed fake environment — the default copies bits of the host's.
    env: { HOME: "/home/pyodide", LANG: "en_US.UTF-8", PATH: "/" },
  });

  // Remove the Pyodide API object from Python's reach. `unregisterJsModule`
  // drops the import hook; the `sys.modules` purge drops the already-imported
  // module and its `_api` submodule, which the JS side had registered too.
  py.unregisterJsModule("pyodide_js");
  py.runPython(
    "import sys\n" +
      "for __k in [k for k in sys.modules if k == 'pyodide_js' or k.startswith('pyodide_js.')]:\n" +
      "    del sys.modules[__k]\n" +
      "del __k, sys\n" +
      // Emscripten sets `_` to this worker script's host path; nothing in
      // the sandbox needs it and it is the only host path that leaks.
      "import os\n" +
      "os.environ.pop('_', None)\n" +
      "del os\n"
  );

  // Network backstop: nothing Pyodide runs after load needs these, and
  // anything author code could dig up would go through them.
  const disabled = () => {
    throw new Error("Network access is disabled in the Python sandbox.");
  };
  (globalThis as { fetch?: unknown }).fetch = disabled;
  (globalThis as { WebSocket?: unknown }).WebSocket = undefined;

  py.setInterruptBuffer(interrupt);
  post({ type: "ready" });

  parentPort!.once("message", async (job: PythonJob) => {
    limitBytes = wasmBytes(py) + job.memoryMb * MiB;
    try {
      py.globals.set("__inputs_json", job.inputsJson);
      py.globals.set("__config_json", job.configJson);
      py.globals.set("__variables_json", job.variablesJson ?? "{}");
      const raw = await py.runPythonAsync(wrapper(job));
      post({ type: "result", ok: true, json: String(raw ?? "null") });
    } catch (err) {
      post({ type: "result", ok: false, error: formatError(err) });
    }
  });
}

main().catch((err) => {
  post({ type: "fatal", error: formatError(err) });
});
