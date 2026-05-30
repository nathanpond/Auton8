// Wire format mirroring AutoNate.Web.Services.Pipelines.Execution.CodeNodeWireFormat.
// Keep the field shapes byte-identical with the host C# records or the
// JSON round-trip will silently drop fields. DataColumnType is the same
// int mapping the abstractions package uses.

export type DataColumnType = 0 | 1 | 2 | 3 | 4 | 5;
// 0=Text, 1=Integer, 2=Number, 3=Boolean, 4=Date, 5=Json

export interface CodeNodeColumn {
  name: string;
  type: DataColumnType;
}

export interface CodeNodeFrame {
  columns: CodeNodeColumn[];
  rows: Array<Record<string, unknown>>;
}

export interface CodeNodeRequest {
  version: number;
  nodeId: string;
  language: "js" | "python";
  kind: "transformer" | "analyzer";
  code: string;
  isUnsafe: boolean;
  config: Record<string, string>;
  inputs: CodeNodeFrame[];
  timeoutMs: number;
  memoryMb: number;
}

export interface CodeNodeReply {
  success: boolean;
  errorMessage: string | null;
  output: CodeNodeFrame | null;
}

export function emptyFrame(): CodeNodeFrame {
  return { columns: [], rows: [] };
}

// Permissive normaliser. Author code can return either a plain row array
// or a { columns, rows } object — both shapes round-trip. Unknown shapes
// reduce to an empty frame so the orchestrator sees a clean Succeeded.
export function normaliseOutput(raw: unknown): CodeNodeFrame {
  if (raw == null) return emptyFrame();
  if (Array.isArray(raw)) {
    // Author returned rows; derive columns from the first row's keys.
    const rows = raw.filter((r) => r && typeof r === "object") as Array<Record<string, unknown>>;
    if (rows.length === 0) return emptyFrame();
    const keys = Array.from(new Set(rows.flatMap((r) => Object.keys(r))));
    return {
      columns: keys.map((k) => ({ name: k, type: 0 })),
      rows,
    };
  }
  if (typeof raw === "object") {
    const obj = raw as Partial<CodeNodeFrame>;
    if (Array.isArray(obj.columns) && Array.isArray(obj.rows)) {
      return obj as CodeNodeFrame;
    }
  }
  return emptyFrame();
}
