import { runScriptTask } from "../dist/scriptTaskRunner.js";
import { prewarmPython, runPythonScriptTask, shutdownPython } from "../dist/pythonRunner.js";

const req = (language, code) => ({
  version: 1, nodeId: "m", language, kind: "scripttask", code,
  isUnsafe: false, config: {}, inputs: [], variables: { a: 1 },
  timeoutMs: 30000, memoryMb: 128,
});

const JS_CODE = "variables.set('b', variables.get('a') + 1);";
const PY_CODE = "variables.set('b', variables.get('a') + 1)";

async function time(fn) { const t = process.hrtime.bigint(); await fn(); return Number(process.hrtime.bigint() - t) / 1e6; }

// Cold: nothing prewarmed.
const jsCold = await time(() => runScriptTask(req("js", JS_CODE)));
const pyCold = await time(() => runPythonScriptTask(req("python", PY_CODE)));

// Warm: the pool has had a chance to spin up a spare.
prewarmPython();
await new Promise((r) => setTimeout(r, 8000));

const jsWarm = [], pyWarm = [];
for (let i = 0; i < 5; i++) jsWarm.push(await time(() => runScriptTask(req("js", JS_CODE))));
for (let i = 0; i < 5; i++) pyWarm.push(await time(() => runPythonScriptTask(req("python", PY_CODE))));

const median = (xs) => [...xs].sort((a, b) => a - b)[Math.floor(xs.length / 2)];
console.log(JSON.stringify({
  jsCold: +jsCold.toFixed(1), pyCold: +pyCold.toFixed(1),
  jsWarmMedian: +median(jsWarm).toFixed(1), pyWarmMedian: +median(pyWarm).toFixed(1),
  jsWarmAll: jsWarm.map(x => +x.toFixed(1)), pyWarmAll: pyWarm.map(x => +x.toFixed(1)),
}, null, 2));
await shutdownPython();
