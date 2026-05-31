import { loadPyodide, type PyodideInterface } from "pyodide";
import { CodeNodeFrame, CodeNodeRequest, normaliseOutput } from "./wire.js";

// Pyodide WASM sandbox. Browser-grade — no `os`, no `subprocess`, no
// host fs. Pandas / NumPy are NOT bundled in v1 because the WASM image
// load time would dominate every short call. Authors needing pandas
// flip `is_unsafe=true` on the host (Phase 6.1 wires the unsafe path).
//
// The Pyodide instance is shared across all invocations and lazy-loaded
// on the first call. The runtime cost moves to the first message; every
// subsequent call is fast.

let pyodide: PyodideInterface | null = null;
const initLock = (async () => {
  // Resolve a top-level await on cold start.
  pyodide = await loadPyodide({
    stdout: () => undefined,
    stderr: () => undefined,
  });
})();

export async function runPython(request: CodeNodeRequest): Promise<CodeNodeFrame> {
  await initLock;
  if (!pyodide) {
    throw new Error("Pyodide failed to initialise.");
  }

  // Wall-clock timeout via Promise.race. Pyodide doesn't expose a
  // per-script timeout; the WASM execution is bound to the JS thread, so
  // we abort the whole call if we cross the limit.
  const timeoutMs = Math.max(1000, request.timeoutMs);
  const timer = new Promise<never>((_, reject) => {
    const handle = setTimeout(() => {
      reject(new Error(`Python execution timed out after ${timeoutMs}ms.`));
    }, timeoutMs);
    handle.unref?.();
  });

  // Marshal inputs + config + entry expectation into the Pyodide
  // namespace as plain Python dicts/lists; author's code accesses them
  // through the wrapper.
  const inputsJson = JSON.stringify(request.inputs.map((f) => f.rows));
  const configJson = JSON.stringify(request.config);
  const wrapper =
    request.kind === "transformer"
      ? `
import json
__inputs = json.loads(${q(inputsJson)})
__config = json.loads(${q(configJson)})

${request.code}

if "transform" not in globals():
    raise RuntimeError("Python transformer must define a 'transform(inputs, config)' function.")
__result = transform(__inputs, __config)
json.dumps(__result, default=str)
`
      : `
import json
__inputs = json.loads(${q(inputsJson)})
__config = json.loads(${q(configJson)})

${request.code}

if "analyze" not in globals():
    raise RuntimeError("Python analyzer must define an 'analyze(input, config)' function.")
__result = analyze(__inputs[0], __config)
json.dumps(__result, default=str)
`;

  const exec = (async () => {
    const py = pyodide!;
    const rawJson = await py.runPythonAsync(wrapper);
    let parsed: unknown;
    try {
      parsed = JSON.parse(String(rawJson ?? "null"));
    } catch {
      parsed = null;
    }
    return normaliseOutput(parsed);
  })();

  return Promise.race([exec, timer]);
}

// Triple-quoted Python string literal for cleanly embedding JSON inside
// the wrapper source.
function q(text: string): string {
  // Escape any triple-quote markers; JSON strings can contain them.
  const escaped = text.replace(/"""/g, '\\"\\"\\"');
  return `"""${escaped}"""`;
}
