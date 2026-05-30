# AutoNate Executor Sidecar

Phase 6 of the Data Stores & Analytics Pipeline plan (`docs/plans/2026-05-30-data-stores-implementation.md`).

Runs user-authored transformer / analyzer code submitted from the host's `JetStreamCodeNodeRunner`. Each invocation is a NATS request/reply on subject `pipeline-code-run.<runId>.<nodeId>`.

## Runtimes

| Language | Default sandbox | Notes |
|---|---|---|
| `js` | [`isolated-vm`](https://github.com/laverdet/isolated-vm) (V8 isolate) | No `require`, no `fetch`, no `fs`. Hard wall-clock timeout, memory cap from the host request. |
| `python` | [`pyodide`](https://pyodide.org) (WASM Python) | Browser-grade sandbox — no `os`, no `subprocess`, no host fs. Pandas / NumPy NOT available in v1. |

The host's `is_unsafe` flag is received over the wire but **not yet honored** in the sidecar — the v1 path always sandboxes. The Phase 6.1 follow-up wires a separate code path that shells out to a host-side CPython container when the flag is set; that path is gated by the `transformer:executeunsafe` permission on the host side already.

## Wire format

Request body (JSON):

```jsonc
{
  "version": 1,
  "nodeId": "node_abc123",
  "language": "js",
  "kind": "transformer",
  "code": "function transform(inputs, config) { return inputs[0]; }",
  "isUnsafe": false,
  "config": { "factor": "1.5" },
  "inputs": [{ "columns": [{ "name": "x", "type": 2 }], "rows": [{ "x": 1 }] }],
  "timeoutMs": 30000,
  "memoryMb": 128
}
```

Reply body:

```jsonc
{ "success": true,  "errorMessage": null, "output": { "columns": [...], "rows": [...] } }
{ "success": false, "errorMessage": "...", "output": null }
```

`DataColumnType` is the same int mapping the host uses (`Text=0, Integer=1, Number=2, Boolean=3, Date=4, Json=5`).

## Author conventions

* **JS** — define `function transform(inputs, config)` (transformers) or `function analyze(input, config)` (analyzers). Return rows directly OR `{ columns, rows }`.
* **Python** — define `def transform(inputs, config)` or `def analyze(input, config)`. Same return shape.

## Local dev

```
npm install
npm run build
NATS_URL=nats://localhost:4222 npm start
```

## Container

The Dockerfile mirrors `services/hocuspocus/` — multi-stage Node 22 alpine. Note that `isolated-vm` requires a native build, so the runtime image carries `python3` + `make` + `g++` for the npm install step.

## TODO before sidecar GA

* Honour `isUnsafe=true` via a separate CPython child-process runner.
* Per-invocation memory caps via `IsolateOptions.memoryLimit` and Pyodide's `lockFileURL` pin.
* Health endpoint mirroring hocuspocus' `/health`.
* Integration test (Testcontainer) that the host's Phase 6 build calls into.
