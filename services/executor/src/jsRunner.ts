import ivm from "isolated-vm";
import { CodeNodeFrame, CodeNodeRequest, emptyFrame, normaliseOutput } from "./wire.js";

// V8 isolate sandbox. No `require`, no `fetch`, no `fs`. The author code
// is wrapped so we can pull either `transform(inputs, config)` or
// `analyze(input, config)` out of it after the script ends.
//
// Memory limit is request-driven; the wall-clock timeout is enforced via
// isolated-vm's per-script `timeout` option in milliseconds.
export async function runJs(request: CodeNodeRequest): Promise<CodeNodeFrame> {
  const isolate = new ivm.Isolate({ memoryLimit: request.memoryMb });
  try {
    const context = await isolate.createContext();
    const jail = context.global;

    // Don't leak the isolated-vm helpers — the global object stays
    // minimal. We expose only what the author needs: `JSON`, `Math`,
    // `Date`, etc. are available by default via V8 itself.
    await jail.set("global", jail.derefInto());

    // Wrapper: evaluate the author's source then call the right entry
    // function based on `kind`. `inputs` arrives as a JS structure the
    // bridge marshalled across the isolate boundary; the entry function
    // returns the output we read back.
    const wrapped =
      request.kind === "transformer"
        ? `${request.code}
;(function () {
  if (typeof transform !== "function") {
    throw new Error("JS transformer must define a 'transform(inputs, config)' function.");
  }
  return JSON.stringify(transform(__inputs, __config));
})()`
        : `${request.code}
;(function () {
  if (typeof analyze !== "function") {
    throw new Error("JS analyzer must define an 'analyze(input, config)' function.");
  }
  return JSON.stringify(analyze(__inputs[0], __config));
})()`;

    await context.evalClosure(
      `globalThis.__inputs = JSON.parse($0); globalThis.__config = JSON.parse($1);`,
      [
        JSON.stringify(request.inputs.map((f) => f.rows)),
        JSON.stringify(request.config),
      ]
    );

    const script = await isolate.compileScript(wrapped);
    const rawJson = await script.run(context, { timeout: request.timeoutMs });
    if (typeof rawJson !== "string") return emptyFrame();
    let parsed: unknown;
    try {
      parsed = JSON.parse(rawJson);
    } catch {
      return emptyFrame();
    }
    return normaliseOutput(parsed);
  } finally {
    isolate.dispose();
  }
}
