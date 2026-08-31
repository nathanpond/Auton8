// Messages between pythonRunner.ts (parent) and pythonWorker.ts (worker).

export interface PythonJob {
  kind: "transformer" | "analyzer";
  code: string;
  // JSON-encoded `inputs[].rows` and `config`; decoded inside Python so the
  // author's data never touches the source text (#64).
  inputsJson: string;
  configJson: string;
  memoryMb: number;
}

export type PythonWorkerMessage =
  | { type: "ready" }
  | { type: "result"; ok: true; json: string }
  | { type: "result"; ok: false; error: string }
  | { type: "fatal"; error: string };
