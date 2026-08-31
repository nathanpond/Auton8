// weather-xlsx-to-csv
//
// Sample Code Transformer that converts the rows of a parsed weather
// workbook into a single CSV-text frame ready for a file sink. Drop the
// body of this file into the SPA at /admin/code-transformers as a new
// JavaScript transformer (kind = "transformer", language = "js").
//
// Pipeline shape:
//   [dataset-source: file row from "Weather" datastore (contains
//                    base64-encoded XLSX bytes in a `content` column)]
//     -> [built-in transformer: xlsx-to-csv]   (parses workbook -> rows)
//     -> [code transformer: weather-xlsx-to-csv]  (this file)
//     -> [dataset-sink: writes the CSV row back into "Weather"
//                       as weather.csv via the sink's filename config]
//
// The JS sandbox has no XLSX/SheetJS library, so the workbook parsing
// must happen upstream in the built-in xlsx-to-csv node. This Code
// Transformer takes the already-parsed rows and:
//   1. trims whitespace from every cell (XLSX cells often pad),
//   2. drops fully-empty rows that ClosedXML emits past the last data row,
//   3. normalizes header names (lowercase, snake_case) so downstream
//      pipelines/queries don't break on spelling drift,
//   4. emits one row { content: "<csv-text>" } ready for the file sink
//      (same shape the built-in json-to-csv transformer emits).
//
// Optional node config (all strings):
//   includeHeader     "true" | "false"   default "true"
//   newline           "\n" | "\r\n"      default "\n"
//   trim              "true" | "false"   default "true"
//   dropEmptyRows     "true" | "false"   default "true"
//   normalizeHeaders  "true" | "false"   default "true"

function transform(inputs, config) {
  const rows = Array.isArray(inputs) && Array.isArray(inputs[0]) ? inputs[0] : [];
  const cfg = config || {};
  const truthy = (v, dflt) => {
    const s = (v == null ? dflt : String(v)).toLowerCase();
    return s !== "false" && s !== "0" && s !== "no";
  };
  const includeHeader = truthy(cfg.includeHeader, "true");
  const trim = truthy(cfg.trim, "true");
  const dropEmptyRows = truthy(cfg.dropEmptyRows, "true");
  const normalizeHeaders = truthy(cfg.normalizeHeaders, "true");
  const newline = cfg.newline === "\r\n" ? "\r\n" : "\n";

  const empty = {
    columns: [{ name: "content", type: 0 }],
    rows: [{ content: "" }]
  };
  if (rows.length === 0) return empty;

  const rawHeaders = [];
  const seen = new Set();
  for (const row of rows) {
    if (!row) continue;
    for (const key of Object.keys(row)) {
      if (!seen.has(key)) { seen.add(key); rawHeaders.push(key); }
    }
  }
  if (rawHeaders.length === 0) return empty;

  const headerLabels = normalizeHeaders
    ? rawHeaders.map(toSnakeCase)
    : rawHeaders.slice();

  const cellOf = (value) => {
    if (value === null || value === undefined) return "";
    let s = typeof value === "string" ? value : String(value);
    if (trim) s = s.trim();
    return s;
  };

  const escape = (s) => {
    if (s === "") return "";
    if (/[",\r\n]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
    return s;
  };

  const lines = [];
  if (includeHeader) lines.push(headerLabels.map(escape).join(","));

  for (const row of rows) {
    const cells = rawHeaders.map((h) => cellOf(row ? row[h] : ""));
    if (dropEmptyRows && cells.every((c) => c === "")) continue;
    lines.push(cells.map(escape).join(","));
  }

  return {
    columns: [{ name: "content", type: 0 }],
    rows: [{ content: lines.join(newline) + newline }]
  };
}

function toSnakeCase(name) {
  return String(name)
    .replace(/[‘’“”]/g, "")
    .replace(/[^A-Za-z0-9]+/g, "_")
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/^_+|_+$/g, "")
    .toLowerCase();
}
