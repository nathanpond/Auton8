// Pulls the names of process variables referenced by a BPMN model so the
// Studio can offer a default-value editor for each, and best-effort
// infers a type for each name from how it's used (number/boolean/json/string).
// Pragmatic, not a full FEEL/Groovy parser — works on the raw XML.

import type { WorkflowDefaultVariableType } from "@/types/flowable";

const RESERVED = new Set([
  "true",
  "false",
  "null",
  "undefined",
  "this",
  "execution",
  "task",
  "if",
  "else",
  "return",
  "for",
  "while",
  "function",
  "var",
  "let",
  "const",
  "new",
  "typeof",
  "instanceof",
  "void"
]);

const IDENTIFIER_RE = /[A-Za-z_$][A-Za-z0-9_$]*/g;
const EXPRESSION_RE = /[$#]\{([^}]*)\}/g;
const GET_VARIABLE_RE =
  /\bexecution\s*\.\s*(?:getVariable|getVariableLocal)\s*\(\s*['"]([A-Za-z_$][A-Za-z0-9_$]*)['"]\s*[\),]/g;
const SET_VARIABLE_RE =
  /\bexecution\s*\.\s*(?:setVariable|setVariableLocal)\s*\(\s*['"]([A-Za-z_$][A-Za-z0-9_$]*)['"]\s*,/g;

const STRING_METHODS =
  "length|charAt|toUpperCase|toLowerCase|substring|substr|trim|split|indexOf|replace|startsWith|endsWith|includes|concat";

export type ExtractedProcessVariable = {
  name: string;
  // Best-effort inferred type from usage. Undefined when the model gives
  // no clear signal — the caller should fall back to "string".
  inferredType?: WorkflowDefaultVariableType;
};

export function extractProcessVariableNames(
  bpmnXml: string | null | undefined
): string[] {
  return extractProcessVariables(bpmnXml).map((v) => v.name);
}

export function extractProcessVariables(
  bpmnXml: string | null | undefined
): ExtractedProcessVariable[] {
  if (!bpmnXml) return [];

  const found = new Set<string>();

  for (const match of bpmnXml.matchAll(EXPRESSION_RE)) {
    const body = match[1] ?? "";
    collectIdentifiersFromExpression(body, found);
  }

  for (const match of bpmnXml.matchAll(GET_VARIABLE_RE)) {
    found.add(match[1]);
  }

  for (const match of bpmnXml.matchAll(SET_VARIABLE_RE)) {
    found.add(match[1]);
  }

  const names = [...found].sort((a, b) => a.localeCompare(b));
  return names.map((name) => ({
    name,
    inferredType: inferVariableType(bpmnXml, name)
  }));
}

function collectIdentifiersFromExpression(body: string, out: Set<string>) {
  // BPMN XML escapes `<`, `>`, `&`, `"`, `'` as XML entities inside
  // attribute values, so a condition authored as `${counter < 5}` shows up
  // as `${counter &lt; 5}`. Drop the entities first so we don't pick up
  // `lt`/`gt`/`amp`/`quot`/`apos` as identifiers. Then strip string
  // literals so identifiers inside `"foo"` aren't treated as variable
  // names either.
  const stripped = body
    .replace(/&(?:[a-zA-Z]+|#[0-9]+|#x[0-9a-fA-F]+);/g, " ")
    .replace(/"[^"]*"|'[^']*'/g, "");
  for (const idMatch of stripped.matchAll(IDENTIFIER_RE)) {
    const offset = idMatch.index ?? 0;
    // Skip dotted accessors like `.bar` in `foo.bar` — only the leftmost
    // identifier is the variable; subsequent ones are property names.
    if (offset > 0 && stripped[offset - 1] === ".") continue;
    const name = idMatch[0];
    if (RESERVED.has(name)) continue;
    out.add(name);
  }
}

function inferVariableType(
  xml: string,
  name: string
): WorkflowDefaultVariableType | undefined {
  const w = `\\b${escapeRegex(name)}\\b`;

  // Number signals (most specific first): ++/--, compound numeric assigns,
  // arithmetic operators with a numeric operand, numeric literal assignment,
  // numeric comparisons.
  const numberPatterns: RegExp[] = [
    new RegExp(`\\+\\+\\s*${w}`),
    new RegExp(`${w}\\s*\\+\\+`),
    new RegExp(`--\\s*${w}`),
    new RegExp(`${w}\\s*--`),
    new RegExp(`${w}\\s*[+\\-*/%]=\\s*-?\\d`),
    new RegExp(`${w}\\s*[*/%]\\s*[^=]`),
    new RegExp(`[^=*/%]\\s*[*/%]\\s*${w}\\b`),
    new RegExp(`${w}\\s*-\\s*-?\\d`),
    new RegExp(`-?\\d\\s*-\\s*${w}\\b`),
    new RegExp(`${w}\\s*=\\s*-?\\d(?!['"])`),
    new RegExp(`${w}\\s*<=?\\s*-?\\d`),
    new RegExp(`${w}\\s*>=?\\s*-?\\d`),
    new RegExp(`-?\\d\\s*<=?\\s*${w}\\b`),
    new RegExp(`-?\\d\\s*>=?\\s*${w}\\b`),
    new RegExp(`${w}\\s*===?\\s*-?\\d(?!['"])`),
    new RegExp(`-?\\d\\s*===?\\s*${w}\\b`)
  ];
  if (numberPatterns.some((re) => re.test(xml))) return "number";

  // Boolean signals: assigned to or compared against a boolean literal.
  const booleanPatterns: RegExp[] = [
    new RegExp(`${w}\\s*=\\s*(?:true|false)\\b`),
    new RegExp(`${w}\\s*===?\\s*(?:true|false)\\b`),
    new RegExp(`(?:true|false)\\s*===?\\s*${w}\\b`),
    new RegExp(`${w}\\s*!==?\\s*(?:true|false)\\b`),
    new RegExp(`(?:true|false)\\s*!==?\\s*${w}\\b`)
  ];
  if (booleanPatterns.some((re) => re.test(xml))) return "boolean";

  // JSON-ish: assigned to object/array literal, or used with bracket access.
  const jsonPatterns: RegExp[] = [
    new RegExp(`${w}\\s*=\\s*[\\[{]`),
    new RegExp(`${w}\\s*\\[`)
  ];
  if (jsonPatterns.some((re) => re.test(xml))) return "json";

  // String: assigned a string literal, compared against a string literal,
  // or accessed via a string-only method/property.
  const stringPatterns: RegExp[] = [
    new RegExp(`${w}\\s*=\\s*['"]`),
    new RegExp(`${w}\\s*===?\\s*['"]`),
    new RegExp(`['"]\\s*===?\\s*${w}\\b`),
    new RegExp(`${w}\\.(?:${STRING_METHODS})\\b`)
  ];
  if (stringPatterns.some((re) => re.test(xml))) return "string";

  return undefined;
}

function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
