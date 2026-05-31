import type { AqlDataType } from "@/api/aql";
import type { Token } from "./aqlLexer";
import { tokenize } from "./aqlLexer";

// Clause the caret is currently inside.
export type ClauseKind =
  | "start"
  | "from"
  | "where"
  | "orderBy"
  | "columns"
  | "group"
  | "limit";

// What the autocomplete should offer at the caret. Each variant carries
// the data the completion source needs to build the option list (e.g.
// the field name for an operator completion).
export type Expecting =
  | { kind: "clauseKeyword"; alreadyUsed: ClauseKind[] }
  | { kind: "entity" }
  | { kind: "field"; scope: "where" | "orderBy" | "columns" | "group" }
  | { kind: "operator"; field: string; dataType: AqlDataType | "unknown" }
  | { kind: "value"; field: string; dataType: AqlDataType | "unknown"; afterOperator: string }
  | { kind: "functionName"; scope: "where" | "select" }
  | { kind: "functionArg"; fnName: string; argIndex: number }
  | { kind: "logicalOp" }
  | { kind: "orderDirection" }
  | { kind: "limitValue" }
  | { kind: "none" };

export type EditorContext = {
  clause: ClauseKind;
  entity: string;
  recordTypeFilter: string | null;
  expecting: Expecting;
  // The prefix the user has already typed at the caret (the lexeme of
  // the token under the caret when it's a partial identifier/keyword;
  // empty when the caret sits on whitespace).
  prefix: string;
  // The text range the completion should replace. When the caret is
  // mid-identifier, replace the whole identifier; otherwise both indexes
  // equal the caret position.
  replaceFrom: number;
  replaceTo: number;
};

// Type alias the completion source uses for field metadata. Mirrors
// AqlColumnMeta but without the import cycle.
export type FieldMeta = {
  name: string;
  dataType: AqlDataType;
};

// Tokens we ignore when walking back to find context (whitespace is
// already gone, but we may want to skip Eof or unknown).
function isMeaningful(t: Token): boolean {
  return t.kind !== "eof";
}

// Build the editor context for a `(text, caret)` pair. Tolerates
// partially-typed input — the lexer never throws.
export function detectContext(text: string, caret: number): EditorContext {
  const tokens = tokenize(text);

  // Determine the token "at" the caret. Only identifiers / keywords /
  // numbers count as "active" — structural tokens like `(`, `)`, `,`
  // are treated as boundaries, with the caret living *between* them.
  // Without this distinction, putting the caret right after a `(` would
  // mark the `(` as active and silently drop it from tokensBefore, so
  // the dispatcher would mistake `COLUMNS(|)` for the pre-paren state.
  let activeIdx = -1;
  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (t.kind === "eof") continue;
    if (caret > t.start && caret <= t.end) {
      if (isPartialAtCaret(t, caret)) {
        activeIdx = i;
      }
      break;
    }
    if (caret <= t.start) break;
  }

  const tokensBefore: Token[] = [];
  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (!isMeaningful(t)) continue;
    if (i === activeIdx) continue;
    if (t.end > caret) break;
    tokensBefore.push(t);
  }

  const active = activeIdx >= 0 ? tokens[activeIdx] : null;
  const prefix = active ? text.slice(active.start, caret) : "";
  const replaceFrom = active ? active.start : caret;
  const replaceTo = active ? active.end : caret;

  // Detect if the caret is inside an open string literal (the lexer
  // tolerated an unterminated quote). When inside, we offer value
  // completions for the field on the left of the operator before the
  // string. Compute this by checking the text from the most recent
  // unmatched quote up to the caret.
  const insideString = isInsideString(text, caret);

  // Resolve clause and entity by walking tokens before the caret.
  const walk = walkClauses(tokensBefore);
  const entity = walk.entity ?? "Records";
  const recordTypeFilter = extractRecordTypeFilter(tokensBefore);

  // Inside an open string after an operator → value completion. We
  // override the replace range to extend from the opening quote to
  // the caret, so accepting a completion replaces what the user has
  // typed so far inside the quotes.
  if (insideString.inside) {
    const fieldOp = findFieldOperatorBefore(tokensBefore);
    if (fieldOp) {
      const dataType = "unknown" as const;
      return {
        clause: walk.clause,
        entity,
        recordTypeFilter,
        expecting: {
          kind: "value",
          field: fieldOp.field,
          dataType,
          afterOperator: fieldOp.op
        },
        prefix: text.slice(insideString.contentStart, caret),
        replaceFrom: insideString.contentStart,
        replaceTo: caret
      };
    }
  }

  // Function-argument context: caret is between `(` and `)` of a
  // function call (not a clause keyword like COLUMNS(...) which has
  // its own field-list context).
  const fnArg = detectFunctionArg(tokensBefore);
  if (fnArg) {
    return {
      clause: walk.clause,
      entity,
      recordTypeFilter,
      expecting: { kind: "functionArg", fnName: fnArg.fnName, argIndex: fnArg.argIndex },
      prefix,
      replaceFrom,
      replaceTo
    };
  }

  // The detailed dispatch is per-clause.
  const expecting = dispatchExpecting(walk.clause, tokensBefore, walk);
  return {
    clause: walk.clause,
    entity,
    recordTypeFilter,
    expecting,
    prefix,
    replaceFrom,
    replaceTo
  };
}

// ---- Clause walk ----------------------------------------------------

type ClauseWalk = {
  clause: ClauseKind;
  entity: string | null;
  alreadyUsed: ClauseKind[];
  // For ORDER BY: track whether we've just emitted a field and are
  // expecting an ASC/DESC direction.
  lastClauseTokens: Token[];
};

function walkClauses(tokens: Token[]): ClauseWalk {
  let clause: ClauseKind = "start";
  let entity: string | null = null;
  const used: ClauseKind[] = [];
  let lastClauseTokens: Token[] = [];

  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (t.kind !== "keyword") {
      lastClauseTokens.push(t);
      continue;
    }
    switch (t.lexeme) {
      case "FROM":
        clause = "from";
        if (!used.includes("from")) used.push("from");
        lastClauseTokens = [];
        // Capture entity name if the next token is an identifier.
        if (i + 1 < tokens.length && tokens[i + 1].kind === "identifier") {
          entity = tokens[i + 1].lexeme;
          // Parameterized FROM (Phase 2 of the Data Stores plan):
          // `Entity("arg")`. Skip past the (string) suffix when present
          // so the downstream clause-keyword detection treats the entity
          // reference as a single unit and the dispatcher recognizes that
          // we've moved past the entity name.
          let j = i + 2;
          if (j < tokens.length && tokens[j].kind === "lparen") {
            j++;
            if (j < tokens.length && tokens[j].kind === "string") {
              j++;
              if (j < tokens.length && tokens[j].kind === "rparen") {
                j++;
              }
            }
            // Fast-forward the outer loop past the consumed suffix.
            i = j - 1;
          }
        }
        break;
      case "WHERE":
        clause = "where";
        if (!used.includes("where")) used.push("where");
        lastClauseTokens = [];
        break;
      case "ORDER":
        // The next keyword should be BY — treat them as a single clause anchor.
        clause = "orderBy";
        if (!used.includes("orderBy")) used.push("orderBy");
        lastClauseTokens = [];
        break;
      case "BY":
        // Already inside orderBy when we hit BY.
        lastClauseTokens = [];
        break;
      case "COLUMNS":
        clause = "columns";
        if (!used.includes("columns")) used.push("columns");
        lastClauseTokens = [];
        break;
      case "GROUP":
        clause = "group";
        if (!used.includes("group")) used.push("group");
        lastClauseTokens = [];
        break;
      case "LIMIT":
        clause = "limit";
        if (!used.includes("limit")) used.push("limit");
        lastClauseTokens = [];
        break;
      default:
        lastClauseTokens.push(t);
        break;
    }
  }

  return { clause, entity, alreadyUsed: used, lastClauseTokens };
}

// ---- Dispatch on clause --------------------------------------------

function dispatchExpecting(
  clause: ClauseKind,
  tokensBefore: Token[],
  walk: ClauseWalk
): Expecting {
  const last = lastMeaningful(tokensBefore);

  switch (clause) {
    case "start":
      return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };

    case "from":
      // Just after FROM keyword OR mid-identifier → entity name.
      if (last?.kind === "keyword" && last.lexeme === "FROM") {
        return { kind: "entity" };
      }
      if (last?.kind === "identifier") {
        // We've named an entity — the next legal thing is another clause.
        return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
      }
      // Parameterized FROM closed with `)` — same downstream expectation
      // as a bare entity identifier (next is a clause keyword).
      if (last?.kind === "rparen") {
        return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
      }
      return { kind: "entity" };

    case "where":
      return dispatchWhere(tokensBefore, walk);

    case "orderBy":
      return dispatchOrderBy(tokensBefore, walk);

    case "columns":
      return dispatchColumns(tokensBefore, walk);

    case "group":
      return dispatchGroup(tokensBefore, walk);

    case "limit":
      if (last?.kind === "keyword" && last.lexeme === "LIMIT") {
        return { kind: "limitValue" };
      }
      if (last?.kind === "number") {
        return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
      }
      return { kind: "limitValue" };
  }
}

function dispatchWhere(tokensBefore: Token[], walk: ClauseWalk): Expecting {
  const last = lastMeaningful(tokensBefore);
  if (!last) return { kind: "field", scope: "where" };
  // Just opened WHERE: last token is the WHERE keyword.
  if (last.kind === "keyword" && last.lexeme === "WHERE") {
    return { kind: "field", scope: "where" };
  }
  // After AND/OR → start of a new comparison.
  if (last.kind === "keyword" && (last.lexeme === "AND" || last.lexeme === "OR")) {
    return { kind: "field", scope: "where" };
  }
  // After an operator → value position.
  if (last.kind === "operator") {
    const fieldOp = findFieldOperatorBefore(tokensBefore);
    if (fieldOp) {
      return {
        kind: "value",
        field: fieldOp.field,
        dataType: "unknown",
        afterOperator: fieldOp.op
      };
    }
    return { kind: "none" };
  }
  // After a complete comparison (string/number/bool/null/relativeDate) → AND/OR.
  if (
    last.kind === "string" ||
    last.kind === "number" ||
    last.kind === "bool" ||
    last.kind === "null" ||
    last.kind === "relativeDate"
  ) {
    return { kind: "logicalOp" };
  }
  // After an identifier (a field name) → operator position.
  if (last.kind === "identifier") {
    return { kind: "operator", field: last.lexeme, dataType: "unknown" };
  }
  // After `)` → end of a sub-expression, expect AND/OR or end-of-clause.
  if (last.kind === "rparen") {
    return { kind: "logicalOp" };
  }
  return { kind: "field", scope: "where" };
}

function dispatchOrderBy(tokensBefore: Token[], walk: ClauseWalk): Expecting {
  const last = lastMeaningful(tokensBefore);
  if (!last) return { kind: "field", scope: "orderBy" };
  if (last.kind === "keyword" && (last.lexeme === "ORDER" || last.lexeme === "BY")) {
    return { kind: "field", scope: "orderBy" };
  }
  if (last.kind === "comma") {
    return { kind: "field", scope: "orderBy" };
  }
  if (last.kind === "identifier" || last.kind === "rparen") {
    return { kind: "orderDirection" };
  }
  if (last.kind === "keyword" && (last.lexeme === "ASC" || last.lexeme === "DESC")) {
    return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
  }
  return { kind: "field", scope: "orderBy" };
}

function dispatchColumns(tokensBefore: Token[], walk: ClauseWalk): Expecting {
  // Inside the COLUMNS(...) parens, every comma starts a new field
  // position; bare identifier after `(` is a field; identifier + `(`
  // would be a function name.
  const last = lastMeaningful(tokensBefore);
  if (!last) return { kind: "field", scope: "columns" };
  if (last.kind === "keyword" && last.lexeme === "COLUMNS") {
    // Pre-paren: nothing to suggest meaningfully other than `(`.
    return { kind: "none" };
  }
  if (last.kind === "lparen" || last.kind === "comma") {
    return { kind: "field", scope: "columns" };
  }
  if (last.kind === "rparen") {
    return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
  }
  if (last.kind === "identifier") {
    // Mid-edit — could be a field or about to become a function call.
    return { kind: "field", scope: "columns" };
  }
  return { kind: "field", scope: "columns" };
}

function dispatchGroup(tokensBefore: Token[], walk: ClauseWalk): Expecting {
  const last = lastMeaningful(tokensBefore);
  if (!last) return { kind: "field", scope: "group" };
  if (last.kind === "keyword" && last.lexeme === "GROUP") {
    return { kind: "none" };
  }
  if (last.kind === "lparen" || last.kind === "comma") {
    return { kind: "field", scope: "group" };
  }
  if (last.kind === "rparen") {
    return { kind: "clauseKeyword", alreadyUsed: walk.alreadyUsed };
  }
  return { kind: "field", scope: "group" };
}

// ---- Helpers --------------------------------------------------------

function lastMeaningful(tokens: Token[]): Token | null {
  for (let i = tokens.length - 1; i >= 0; i--) {
    if (tokens[i].kind !== "eof") return tokens[i];
  }
  return null;
}

function isPartialAtCaret(t: Token, caret: number): boolean {
  // Only identifier/keyword/number partials are useful filter prefixes.
  if (t.kind !== "identifier" && t.kind !== "keyword" && t.kind !== "number") {
    return false;
  }
  return caret > t.start && caret <= t.end;
}

// Walk back from the caret to find the most recent `field op` pair
// (where `op` is a comparison operator). Used to figure out which
// field's enum values to suggest after `Status = ` or `RecordType = `.
function findFieldOperatorBefore(
  tokens: Token[]
): { field: string; op: string } | null {
  for (let i = tokens.length - 1; i >= 1; i--) {
    const t = tokens[i];
    if (t.kind === "operator") {
      const prev = tokens[i - 1];
      if (prev?.kind === "identifier") {
        return { field: prev.lexeme, op: t.lexeme };
      }
      return null;
    }
    // A keyword AND/OR resets the comparison context.
    if (t.kind === "keyword" && (t.lexeme === "AND" || t.lexeme === "OR")) {
      return null;
    }
  }
  return null;
}

// Pulls the first `RecordType = "X"` literal out of the WHERE clause —
// mirrors RecordsQueryEntity.CollectRecordTypeLiterals on the backend.
function extractRecordTypeFilter(tokens: Token[]): string | null {
  for (let i = 0; i < tokens.length - 2; i++) {
    const a = tokens[i];
    const b = tokens[i + 1];
    const c = tokens[i + 2];
    if (
      a.kind === "identifier" &&
      a.lexeme.toLowerCase() === "recordtype" &&
      b.kind === "operator" &&
      b.lexeme === "=" &&
      c.kind === "string"
    ) {
      return c.lexeme;
    }
  }
  return null;
}

// Detect whether the caret sits inside an open string literal. Returns
// the start index of the string's *content* (one past the opening
// quote) so the caller can compute the replacement range.
function isInsideString(text: string, caret: number): { inside: boolean; contentStart: number } {
  let i = 0;
  while (i < caret && i < text.length) {
    const c = text[i];
    if (c === '"' || c === "'") {
      const quote = c;
      const contentStart = i + 1;
      let j = i + 1;
      while (j < text.length && text[j] !== quote) {
        if (text[j] === "\\" && j + 1 < text.length) {
          j += 2;
          continue;
        }
        j++;
      }
      if (j >= caret) {
        // The closing quote is at or past the caret → caret is inside.
        return { inside: true, contentStart };
      }
      i = j + 1;
      continue;
    }
    i++;
  }
  return { inside: false, contentStart: caret };
}

// Walk tokens to find an open function call enclosing the caret.
// "Function" here excludes clause keywords like COLUMNS/GROUP whose
// `(` opens a clause-arg list rather than a call.
function detectFunctionArg(
  tokens: Token[]
): { fnName: string; argIndex: number } | null {
  let depth = 0;
  let fnName: string | null = null;
  let argIndex = 0;
  for (let i = tokens.length - 1; i >= 0; i--) {
    const t = tokens[i];
    if (t.kind === "rparen") {
      depth++;
      continue;
    }
    if (t.kind === "lparen") {
      if (depth > 0) {
        depth--;
        continue;
      }
      // This `(` is the open one. The token immediately before should
      // be the function name (an identifier or whitelisted keyword)
      // unless it's a clause keyword.
      const prev = i > 0 ? tokens[i - 1] : null;
      if (!prev) return null;
      if (prev.kind === "keyword" && (prev.lexeme === "COLUMNS" || prev.lexeme === "GROUP")) {
        return null;
      }
      if (prev.kind === "identifier") {
        fnName = prev.lexeme;
        return { fnName, argIndex };
      }
      return null;
    }
    if (depth === 0 && t.kind === "comma") {
      argIndex++;
    }
  }
  return null;
}
