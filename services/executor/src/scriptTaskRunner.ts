import ivm from "isolated-vm";
import { CodeNodeRequest, ScriptTaskResult } from "./wire.js";
import { HostApiRegistry, defaultHostApi } from "./hostApi.js";

// BPMN script-task execution (#147).
//
// Flowable no longer runs these; the engine's ScriptTaskActivityBehavior is
// replaced so the author's code never reaches the JVM. It arrives here instead
// and runs in the same V8 isolate the pipeline code nodes use: no `require`, no
// `fetch`, no `fs`, no host object of any kind reachable from author code.
//
// The author writes bare statements rather than a function, because that is
// what a BPMN script task is. `variables` is the only host surface at v1.0.
//
// The host API façade is generated inside the isolate from the registry and
// closes over `__state`, which never leaves it. Only JSON crosses the boundary:
// the variable snapshot in, the mutations out. That is what keeps "the script
// cannot reach the host" true by construction rather than by review.
export async function runScriptTask(
  request: CodeNodeRequest,
  registry: HostApiRegistry = defaultHostApi()
): Promise<ScriptTaskResult> {
  const isolate = new ivm.Isolate({ memoryLimit: request.memoryMb });
  try {
    const context = await isolate.createContext();
    await context.global.set("global", context.global.derefInto());

    await context.evalClosure(`globalThis.__state = { variables: JSON.parse($0), mutations: {} };`, [
      JSON.stringify(request.variables ?? {}),
    ]);

    // The author's code is wrapped in a function body rather than concatenated
    // at top level so that `return` works the way a script task's author
    // expects, and so their declarations cannot shadow `__state`.
    const wrapped = `
${registry.buildFacadeSource()}
;(function () {
  const __result = (function () {
${request.code}
  })();
  return JSON.stringify({ result: __result === undefined ? null : __result, mutations: __state.mutations });
})()`;

    const script = await isolate.compileScript(wrapped);
    const rawJson = await script.run(context, { timeout: request.timeoutMs });
    if (typeof rawJson !== "string") {
      return { result: null, mutations: {} };
    }
    const parsed = JSON.parse(rawJson) as ScriptTaskResult;
    return { result: parsed.result ?? null, mutations: parsed.mutations ?? {} };
  } finally {
    isolate.dispose();
  }
}
