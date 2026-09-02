import { Worker } from "node:worker_threads";
import type { PythonJob, PythonWorkerMessage } from "./pythonProtocol.js";
import { CodeNodeFrame, CodeNodeRequest, normaliseOutput } from "./wire.js";

// Pyodide (WASM Python) sandbox, one interpreter per request (archived-58).
//
// Each request runs in its own `worker_threads` Worker (pythonWorker.ts)
// that loads a fresh Pyodide and is terminated afterwards, so no state
// crosses authors. A small warm pool hides the ~0.8 s interpreter load;
// concurrency is capped so a burst cannot fork unbounded interpreters.
//
// The deadline lives here, on the main thread, which the WASM cannot
// block: at `timeoutMs` the request is rejected, SIGINT is written into
// Pyodide's interrupt buffer (a KeyboardInterrupt in the script), and the
// worker is hard-terminated after a short grace for C-level loops the
// interrupt cannot reach. `memoryMb` is enforced inside the worker by
// refusing WebAssembly memory growth past baseline + memoryMb (a Python
// MemoryError); `EXECUTOR_PY_JS_HEAP_MB` caps the worker's JS heap as an
// operator-level backstop.
//
// Pandas / NumPy are NOT bundled in v1 because the WASM image load time
// would dominate every short call. Authors needing them flip
// `is_unsafe=true` on the host (Phase 6.1 wires the unsafe path).

const WARM_WORKERS = envInt("EXECUTOR_PY_WARM_WORKERS", 1);
const MAX_CONCURRENCY = envInt("EXECUTOR_PY_MAX_CONCURRENCY", 2);
const JS_HEAP_MB = envInt("EXECUTOR_PY_JS_HEAP_MB", 256);
const TERMINATE_GRACE_MS = 250;
const SIGINT = 2;

interface PythonWorker {
  worker: Worker;
  interrupt: Uint8Array;
  ready: Promise<void>;
}

const warm: PythonWorker[] = [];
let active = 0;
const waiters: Array<() => void> = [];
let shuttingDown = false;

function envInt(name: string, fallback: number): number {
  const raw = process.env[name];
  const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function spawn(): PythonWorker {
  const shared = new SharedArrayBuffer(1);
  const worker = new Worker(new URL("./pythonWorker.js", import.meta.url), {
    workerData: { interrupt: shared },
    // Fresh, empty environment: the worker must not see the host's env.
    env: {},
    resourceLimits: { maxOldGenerationSizeMb: JS_HEAP_MB },
  });
  const ready = new Promise<void>((resolve, reject) => {
    const onMessage = (message: PythonWorkerMessage) => {
      if (message.type === "ready") {
        cleanup();
        resolve();
      } else if (message.type === "fatal") {
        cleanup();
        reject(new Error(`Python runtime failed to start: ${message.error}`));
      }
    };
    const onError = (err: Error) => {
      cleanup();
      reject(new Error(`Python runtime failed to start: ${err.message}`));
    };
    const onExit = (code: number) => {
      cleanup();
      reject(new Error(`Python runtime exited during start-up (code ${code}).`));
    };
    const cleanup = () => {
      worker.off("message", onMessage);
      worker.off("error", onError);
      worker.off("exit", onExit);
    };
    worker.on("message", onMessage);
    worker.on("error", onError);
    worker.on("exit", onExit);
  });
  // Swallow here; the request that adopts this worker awaits `ready` and
  // surfaces the failure. Without this a warm spare failing to load would
  // be an unhandled rejection.
  ready.catch(() => undefined);
  // A warm spare must not keep the process alive on its own.
  worker.unref();
  return { worker, interrupt: new Uint8Array(shared), ready };
}

function replenish(): void {
  if (shuttingDown) return;
  while (warm.length < WARM_WORKERS) warm.push(spawn());
}

// Start the warm pool ahead of the first request. Idempotent.
export function prewarmPython(): void {
  replenish();
}

// Terminate warm spares; in-flight requests finish on their own workers.
export async function shutdownPython(): Promise<void> {
  shuttingDown = true;
  const spares = warm.splice(0, warm.length);
  await Promise.all(spares.map((w) => w.worker.terminate().catch(() => undefined)));
}

async function acquireSlot(): Promise<void> {
  if (active < MAX_CONCURRENCY) {
    active += 1;
    return;
  }
  await new Promise<void>((resolve) => waiters.push(resolve));
  active += 1;
}

function releaseSlot(): void {
  active -= 1;
  const next = waiters.shift();
  if (next) next();
}

export async function runPython(request: CodeNodeRequest): Promise<CodeNodeFrame> {
  await acquireSlot();
  const handle = warm.shift() ?? spawn();
  replenish();
  try {
    return await runOn(handle, request);
  } finally {
    // Single-use: whatever happened, this interpreter is never reused.
    void handle.worker.terminate().catch(() => undefined);
    releaseSlot();
  }
}

async function runOn(handle: PythonWorker, request: CodeNodeRequest): Promise<CodeNodeFrame> {
  const { worker, interrupt } = handle;
  worker.ref();
  await handle.ready;

  const timeoutMs = Math.max(1000, request.timeoutMs);
  const job: PythonJob = {
    kind: request.kind,
    code: request.code,
    inputsJson: JSON.stringify(request.inputs.map((f) => f.rows)),
    configJson: JSON.stringify(request.config),
    memoryMb: Math.max(1, request.memoryMb),
  };

  return new Promise<CodeNodeFrame>((resolve, reject) => {
    let settled = false;
    let graceTimer: NodeJS.Timeout | undefined;

    const finish = (fn: () => void) => {
      if (settled) return;
      settled = true;
      clearTimeout(deadline);
      if (graceTimer) clearTimeout(graceTimer);
      worker.off("message", onMessage);
      worker.off("error", onError);
      worker.off("exit", onExit);
      fn();
    };

    const deadline = setTimeout(() => {
      finish(() => reject(new Error(`Python execution timed out after ${timeoutMs}ms.`)));
      // Ask nicely first (KeyboardInterrupt at the next bytecode check),
      // then stop the thread if the script does not come back.
      Atomics.store(interrupt, 0, SIGINT);
      graceTimer = setTimeout(() => {
        void worker.terminate().catch(() => undefined);
      }, TERMINATE_GRACE_MS);
      graceTimer.unref();
      worker.once("message", () => {
        if (graceTimer) clearTimeout(graceTimer);
        void worker.terminate().catch(() => undefined);
      });
    }, timeoutMs);

    const onMessage = (message: PythonWorkerMessage) => {
      if (message.type === "result") {
        if (message.ok) {
          let parsed: unknown;
          try {
            parsed = JSON.parse(message.json);
          } catch {
            parsed = null;
          }
          finish(() => resolve(normaliseOutput(parsed)));
        } else {
          finish(() => reject(new Error(message.error)));
        }
      } else if (message.type === "fatal") {
        finish(() => reject(new Error(`Python runtime failed: ${message.error}`)));
      }
    };
    const onError = (err: Error & { code?: string }) => {
      const reason =
        err.code === "ERR_WORKER_OUT_OF_MEMORY"
          ? `Python execution exceeded the sandbox JS heap limit (${JS_HEAP_MB} MB).`
          : `Python runtime error: ${err.message}`;
      finish(() => reject(new Error(reason)));
    };
    const onExit = (code: number) => {
      finish(() => reject(new Error(`Python runtime exited before replying (code ${code}).`)));
    };

    worker.on("message", onMessage);
    worker.on("error", onError);
    worker.on("exit", onExit);
    worker.postMessage(job);
  });
}
