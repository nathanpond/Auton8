// Messages between pythonRunner.ts (parent) and pythonWorker.ts (worker).

export interface PythonJob {
  kind: "transformer" | "analyzer" | "scripttask";
  code: string;
  // JSON-encoded `inputs[].rows` and `config`; decoded inside Python so the
  // author's data never touches the source text (archived-64).
  inputsJson: string;
  configJson: string;
  // JSON-encoded process variables for a `scripttask` job. Decoded inside
  // Python for the same reason as inputs: the author's data never touches the
  // source text.
  variablesJson?: string;
  memoryMb: number;
}

export type PythonWorkerMessage =
  | { type: "ready" }
  | { type: "result"; ok: true; json: string }
  | { type: "result"; ok: false; error: string }
  | { type: "fatal"; error: string };
