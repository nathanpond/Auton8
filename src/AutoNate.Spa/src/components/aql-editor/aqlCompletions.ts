import type {
  Completion,
  CompletionContext,
  CompletionResult
} from "@codemirror/autocomplete";
import type { AqlDataType } from "@/api/aql";
import type {
  AqlEntityContext,
  AqlSchema,
  AqlValueCompletions
} from "@/api/aqlSchema";
import type { ClauseKind, EditorContext, Expecting } from "./aqlContext";
import { detectContext } from "./aqlContext";

// Schema + entity-context bundle the completion source needs to make
// decisions. The editor passes a getter (not the values directly) so the
// source can read the latest cache snapshot on every keystroke without
// re-binding when react-query refreshes.
export type CompletionDeps = {
  schema: AqlSchema | null;
  entityContext: AqlEntityContext | null;
  // Called when the source decides it needs to know about a different
  // entity than the one currently cached. The editor responds by
  // updating its react-query keys; results show up on the next
  // completion invocation.
  requestEntityContext: (entity: string, recordType: string | null) => void;
};

export function buildAqlCompletionSource(
  getDeps: () => CompletionDeps
): (ctx: CompletionContext) => CompletionResult | null {
  return (ctx: CompletionContext): CompletionResult | null => {
    const deps = getDeps();
    const text = ctx.state.doc.toString();
    const editorCtx = detectContext(text, ctx.pos);

    // Ask the schema layer for entity context if the entity (or its
    // RecordType filter) doesn't match what we have cached.
    if (
      !deps.entityContext ||
      deps.entityContext.entity.toLowerCase() !== editorCtx.entity.toLowerCase() ||
      (deps.entityContext.resolvedRecordType ?? null) !== (editorCtx.recordTypeFilter ?? null)
    ) {
      deps.requestEntityContext(editorCtx.entity, editorCtx.recordTypeFilter);
    }

    const options = buildOptions(editorCtx, deps);
    if (options.length === 0) {
      // Don't auto-open the dropdown when there's nothing to show;
      // Ctrl+Space (explicit) still triggers, but typing won't.
      if (!ctx.explicit) return null;
    }

    // `validFor` keeps the dropdown stable while the user continues
    // typing within the same identifier.
    return {
      from: editorCtx.replaceFrom,
      to: editorCtx.replaceTo,
      options,
      validFor: /^[\w-]*$/
    };
  };
}

// ---- Option builder -------------------------------------------------

function buildOptions(editorCtx: EditorContext, deps: CompletionDeps): Completion[] {
  const { schema, entityContext } = deps;
  if (!schema) {
    return [{ label: "Loading…", apply: "", type: "text" }];
  }

  switch (editorCtx.expecting.kind) {
    case "clauseKeyword":
      return clauseKeywordOptions(editorCtx.expecting.alreadyUsed, editorCtx.clause);

    case "entity":
      return schema.entities.map((e) => {
        // Parameterized FROM (Phase 2 of the Data Stores plan): entities
        // that take an argument get a `Name("` apply string so the user
        // immediately lands inside the quotes. Bare entities preserve the
        // trailing-space behavior that lets the next clause keyword flow.
        const apply = e.acceptsEntityArgument ? `${e.name}("` : `${e.name} `;
        const detail = e.acceptsEntityArgument
          ? e.entityArgumentHint ?? "parameterized"
          : undefined;
        return {
          label: e.name,
          type: "namespace",
          apply,
          detail
        };
      });

    case "field":
      return fieldOptions(editorCtx, schema, entityContext);

    case "operator":
      return operatorOptions(editorCtx.expecting.field, schema, entityContext);

    case "value":
      return valueOptions(editorCtx.expecting.field, entityContext);

    case "functionName":
      return functionOptions(editorCtx.expecting.scope, schema, entityContext);

    case "functionArg": {
      // Row functions that publish a closed-set arg vocabulary (e.g.
      // Flows.CURRENTSTEP → Name/Assignee/...) get their own list.
      // Anything else falls back to the entity's columns (the right
      // answer for aggregates like COUNT/MIN/MAX/AVG/MEDIAN and for
      // WHERE built-ins like IN/BETWEEN that take field references).
      const fnName = editorCtx.expecting.fnName;
      const entityMeta = entityContext
        ? schema.entities.find(
            (e) => e.name.toLowerCase() === entityContext.entity.toLowerCase()
          )
        : null;
      const rowFn = entityMeta?.rowFunctions.find(
        (f) => f.name.toLowerCase() === fnName.toLowerCase()
      );
      // Older backend revisions return rowFunctions without `arguments`;
      // tolerate undefined so a stale schema doesn't crash the source.
      const rowFnArgs = rowFn?.arguments ?? [];
      if (rowFnArgs.length > 0) {
        return rowFnArgs.map((arg) => ({
          label: arg,
          type: "enum"
        }));
      }
      return entityContext
        ? entityContext.columns.map((c) => ({
            label: c.name,
            type: "property",
            detail: c.dataType
          }))
        : [];
    }

    case "logicalOp":
      return [
        { label: "AND", type: "keyword", apply: "AND " },
        { label: "OR", type: "keyword", apply: "OR " }
      ];

    case "orderDirection":
      return [
        { label: "ASC", type: "keyword", apply: "ASC " },
        { label: "DESC", type: "keyword", apply: "DESC " }
      ];

    case "limitValue":
      return [
        { label: "100", type: "constant" },
        { label: "500", type: "constant" },
        { label: "1000", type: "constant" }
      ];

    case "none":
      return [];
  }
}

// ---- Clause keywords -----------------------------------------------

// Strict clause order from the parser: FROM → WHERE → ORDER BY →
// COLUMNS → GROUP → LIMIT. We only suggest clauses that haven't been
// used yet AND come after the current clause's slot.
const CLAUSE_ORDER: ClauseKind[] = [
  "from", "where", "orderBy", "columns", "group", "limit"
];

function clauseKeywordOptions(
  alreadyUsed: ClauseKind[],
  currentClause: ClauseKind
): Completion[] {
  const usedSet = new Set(alreadyUsed);
  // The current clause may legitimately appear in `alreadyUsed` (we
  // just emitted it). Allow clauses that come strictly *after* the
  // current clause in the canonical order.
  const currentIdx = CLAUSE_ORDER.indexOf(currentClause);
  const startIdx = currentIdx >= 0 ? currentIdx + 1 : 0;

  const out: Completion[] = [];
  for (let i = startIdx; i < CLAUSE_ORDER.length; i++) {
    const c = CLAUSE_ORDER[i];
    if (usedSet.has(c) && c !== currentClause) continue;
    out.push(clauseToCompletion(c));
  }
  if (currentClause === "start") {
    // Promote FROM at the start of a fresh query.
    return [clauseToCompletion("from"), ...out.filter((o) => o.label !== "FROM")];
  }
  return out;
}

function clauseToCompletion(c: ClauseKind): Completion {
  const label = clauseLabel(c);
  // COLUMNS and GROUP both take parens; place the caret inside.
  if (c === "columns" || c === "group") {
    return {
      label,
      type: "keyword",
      apply: applyWithCaret(label + "(", ")", -1)
    };
  }
  return { label, type: "keyword", apply: label + " " };
}

function clauseLabel(c: ClauseKind): string {
  switch (c) {
    case "from": return "FROM";
    case "where": return "WHERE";
    case "orderBy": return "ORDER BY";
    case "columns": return "COLUMNS";
    case "group": return "GROUP";
    case "limit": return "LIMIT";
    default: return "";
  }
}

// ---- Field options --------------------------------------------------

function fieldOptions(
  editorCtx: EditorContext,
  schema: AqlSchema,
  entityContext: AqlEntityContext | null
): Completion[] {
  if (!entityContext) {
    return [{ label: "Loading…", apply: "", type: "text" }];
  }

  const fieldCompletions: Completion[] = entityContext.columns.map((c) => ({
    label: c.name,
    type: "property",
    detail: c.dataType,
    boost: c.isSystem ? 1 : 0
  }));

  if (editorCtx.expecting.kind !== "field") return fieldCompletions;
  const scope = editorCtx.expecting.scope;

  // In COLUMNS / ORDER BY, also offer the aggregate + row-function
  // names (they emit `FN(` with the caret inside the parens).
  if (scope === "columns" || scope === "orderBy") {
    const fnCompletions = selectScopeFunctionCompletions(schema, entityContext);
    return [...fieldCompletions, ...fnCompletions];
  }
  return fieldCompletions;
}

// ---- Operator options ----------------------------------------------

function operatorOptions(
  fieldName: string,
  schema: AqlSchema,
  entityContext: AqlEntityContext | null
): Completion[] {
  const dt = lookupDataType(fieldName, entityContext);
  const fromTable = dt !== "unknown" ? schema.operatorsByDataType[dt] : undefined;
  const ops = fromTable ?? ["=", "!=", "<", "<=", ">", ">=", "~"];
  return ops.map((op: string) => ({
    label: op,
    type: "operator",
    apply: padOperator(op)
  }));
}

// ---- Value options --------------------------------------------------

function valueOptions(
  fieldName: string,
  entityContext: AqlEntityContext | null
): Completion[] {
  if (!entityContext) return [];
  const dt = lookupDataType(fieldName, entityContext);

  const fromEnums = lookupValueCompletions(fieldName, entityContext);
  const enumOptions = fromEnums
    ? fromEnums.values.map((v) => ({
        label: v,
        type: "enum",
        apply: quoteIfNeeded(v, dt)
      } as Completion))
    : [];

  if (enumOptions.length > 0) return enumOptions;

  if (dt === "bool") {
    return [
      { label: "True", type: "constant", apply: "True " },
      { label: "False", type: "constant", apply: "False " }
    ];
  }
  if (dt === "date") {
    // Relative-date templates — common deltas first.
    return [
      { label: "-1d", type: "constant", detail: "1 day ago" },
      { label: "-7d", type: "constant", detail: "7 days ago" },
      { label: "-1w", type: "constant", detail: "1 week ago" },
      { label: "-1m", type: "constant", detail: "1 month ago" },
      { label: "-1y", type: "constant", detail: "1 year ago" }
    ];
  }
  return [];
}

function quoteIfNeeded(value: string, dataType: AqlDataType | "unknown"): string {
  if (dataType === "string" || dataType === "date" || dataType === "unknown") {
    return `"${value.replace(/"/g, '\\"')}" `;
  }
  return value + " ";
}

// ---- Function options ----------------------------------------------

function functionOptions(
  scope: "where" | "select",
  schema: AqlSchema,
  entityContext: AqlEntityContext | null
): Completion[] {
  if (scope === "where") {
    const entity = entityContext
      ? schema.entities.find(
          (e) => e.name.toLowerCase() === entityContext.entity.toLowerCase()
        )
      : null;
    const builtins = schema.whereFunctions.map((f) => fnCompletion(f));
    const entityFns = (entity?.allowedWhereFunctions ?? []).map((f) => fnCompletion(f));
    return [...builtins, ...entityFns];
  }
  return selectScopeFunctionCompletions(schema, entityContext);
}

function selectScopeFunctionCompletions(
  schema: AqlSchema,
  entityContext: AqlEntityContext | null
): Completion[] {
  const aggregates = schema.globalAggregates.map((a) =>
    fnCompletion(a.name, a.requiresArgument)
  );
  const entity = entityContext
    ? schema.entities.find(
        (e) => e.name.toLowerCase() === entityContext.entity.toLowerCase()
      )
    : null;
  const rowFns = (entity?.rowFunctions ?? []).map((f) =>
    fnCompletion(f.name, f.acceptsArgument)
  );
  return [...aggregates, ...rowFns];
}

function fnCompletion(name: string, acceptsArg = true): Completion {
  return {
    label: name,
    type: "function",
    apply: acceptsArg
      ? applyWithCaret(name + "(", ")", -1)
      : name + "() "
  };
}

// ---- Lookup helpers -------------------------------------------------

function lookupDataType(
  fieldName: string,
  entityContext: AqlEntityContext | null
): AqlDataType | "unknown" {
  if (!entityContext) return "unknown";
  const col = entityContext.columns.find(
    (c) => c.name.toLowerCase() === fieldName.toLowerCase()
  );
  return col ? col.dataType : "unknown";
}

function lookupValueCompletions(
  fieldName: string,
  entityContext: AqlEntityContext | null
): AqlValueCompletions | null {
  if (!entityContext) return null;
  const key = Object.keys(entityContext.valueCompletions).find(
    (k) => k.toLowerCase() === fieldName.toLowerCase()
  );
  return key ? entityContext.valueCompletions[key] : null;
}

// ---- Apply helpers --------------------------------------------------

// Build an `apply` function that inserts `before + after` and places
// the caret `caretOffset` chars from the end (negative offsets).
function applyWithCaret(
  before: string,
  after: string,
  caretOffset: number
): NonNullable<Completion["apply"]> {
  return (view, _completion, from, to) => {
    const insert = before + after;
    const caret = from + insert.length + caretOffset;
    view.dispatch({
      changes: { from, to, insert },
      selection: { anchor: caret }
    });
  };
}

// Operators want padding spaces around them when missing. This makes
// `Name|` + `=` → `Name = |`, while `Name |` + `=` → `Name = |`
// (no double-space).
function padOperator(op: string): NonNullable<Completion["apply"]> {
  return (view, _completion, from, to) => {
    const doc = view.state.doc;
    const charBefore = from > 0 ? doc.sliceString(from - 1, from) : " ";
    const charAfter = to < doc.length ? doc.sliceString(to, to + 1) : " ";
    const leading = charBefore === " " ? "" : " ";
    const trailing = charAfter === " " ? "" : " ";
    const insert = leading + op + trailing;
    view.dispatch({
      changes: { from, to, insert },
      selection: { anchor: from + insert.length }
    });
  };
}
