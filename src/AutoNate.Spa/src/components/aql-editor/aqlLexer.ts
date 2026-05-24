// TypeScript port of AqlLexer.cs. Kept 1:1 with the C# source so the
// SPA's autocomplete state machine sees the same tokens the backend
// parser will see. Tokens carry their absolute offsets so callers can
// map back to the editor's caret.
//
// Unlike the C# lexer, this one never throws — unterminated strings
// and stray characters are emitted as best-effort tokens so the editor
// can still offer completions mid-edit (when the buffer is necessarily
// malformed).

export type TokenKind =
  | "keyword"
  | "identifier"
  | "string"
  | "number"
  | "relativeDate"
  | "bool"
  | "null"
  | "operator"
  | "lparen"
  | "rparen"
  | "comma"
  | "unknown"
  | "eof";

export type Token = {
  kind: TokenKind;
  // The canonical lexeme: keywords/bools/null are upper-cased; strings
  // are the un-quoted, escape-processed value; numbers and relative
  // dates are the raw source text; identifiers are the raw source text.
  lexeme: string;
  // Source range. `start` is the index of the first character of this
  // token in the input; `end` is one past the last character (so
  // `source.slice(start, end)` round-trips the original text).
  start: number;
  end: number;
};

const KEYWORDS = new Set([
  "FROM", "WHERE", "ORDER", "BY", "ASC", "DESC",
  "COLUMNS", "GROUP", "LIMIT",
  "AND", "OR", "AS"
]);

const REL_DATE_UNITS = new Set(["h", "d", "w", "m", "y", "H", "D", "W", "M", "Y"]);

export function tokenize(source: string): Token[] {
  const tokens: Token[] = [];
  let i = 0;
  while (i < source.length) {
    const c = source[i];

    if (isWhitespace(c)) {
      i++;
      continue;
    }

    // Sign-prefixed number/relative-date. `-Foo` is never a number;
    // `-2w` always is a relative date.
    if ((c === "-" || c === "+") && i + 1 < source.length && isDigit(source[i + 1])) {
      tokens.push(readNumericOrRelativeDate(source, i));
      i = tokens[tokens.length - 1].end;
      continue;
    }
    if (isDigit(c)) {
      tokens.push(readNumericOrRelativeDate(source, i));
      i = tokens[tokens.length - 1].end;
      continue;
    }

    if (c === '"' || c === "'") {
      tokens.push(readString(source, i));
      i = tokens[tokens.length - 1].end;
      continue;
    }

    if (c === "(") { tokens.push({ kind: "lparen", lexeme: "(", start: i, end: i + 1 }); i++; continue; }
    if (c === ")") { tokens.push({ kind: "rparen", lexeme: ")", start: i, end: i + 1 }); i++; continue; }
    if (c === ",") { tokens.push({ kind: "comma", lexeme: ",", start: i, end: i + 1 }); i++; continue; }

    if (c === "=" || c === "<" || c === ">" || c === "!" || c === "~") {
      tokens.push(readOperator(source, i));
      i = tokens[tokens.length - 1].end;
      continue;
    }

    if (isIdentStart(c)) {
      tokens.push(readIdentifierOrKeyword(source, i));
      i = tokens[tokens.length - 1].end;
      continue;
    }

    tokens.push({ kind: "unknown", lexeme: c, start: i, end: i + 1 });
    i++;
  }
  tokens.push({ kind: "eof", lexeme: "", start: source.length, end: source.length });
  return tokens;
}

// ---- Helpers ---------------------------------------------------------

function readNumericOrRelativeDate(source: string, start: number): Token {
  let i = start;
  if (source[i] === "-" || source[i] === "+") i++;
  while (i < source.length && isDigit(source[i])) i++;

  let hasFraction = false;
  if (i < source.length && source[i] === "." && i + 1 < source.length && isDigit(source[i + 1])) {
    hasFraction = true;
    i++;
    while (i < source.length && isDigit(source[i])) i++;
  }

  if (!hasFraction && i < source.length) {
    const ch = source[i];
    // Relative-date suffix must NOT be followed by another ident char,
    // so `2hours` stays as Number(2) + Identifier(hours).
    if (REL_DATE_UNITS.has(ch) && (i + 1 >= source.length || !isIdentPart(source[i + 1]))) {
      i++;
      return { kind: "relativeDate", lexeme: source.slice(start, i), start, end: i };
    }
  }

  return { kind: "number", lexeme: source.slice(start, i), start, end: i };
}

function readString(source: string, start: number): Token {
  const quote = source[start];
  let i = start + 1;
  let out = "";
  while (i < source.length && source[i] !== quote) {
    if (source[i] === "\\" && i + 1 < source.length) {
      const next = source[i + 1];
      out += escapeMap[next] ?? next;
      i += 2;
      continue;
    }
    out += source[i];
    i++;
  }
  // Tolerate unterminated strings (mid-typing). The token ends at
  // wherever we ran out of input; callers treat "we're inside an open
  // string" specially.
  const end = i < source.length ? i + 1 : i;
  return { kind: "string", lexeme: out, start, end };
}

const escapeMap: Record<string, string> = {
  n: "\n",
  t: "\t",
  r: "\r",
  "\\": "\\",
  "\"": "\"",
  "'": "'"
};

function readOperator(source: string, start: number): Token {
  const c = source[start];
  if (c === "!" && start + 1 < source.length && source[start + 1] === "=") {
    return { kind: "operator", lexeme: "!=", start, end: start + 2 };
  }
  if ((c === "<" || c === ">") && start + 1 < source.length && source[start + 1] === "=") {
    return { kind: "operator", lexeme: source.slice(start, start + 2), start, end: start + 2 };
  }
  if (c === "!") {
    // Stand-alone `!` is invalid AQL — emit as unknown so the detector
    // doesn't mistake it for an operator the backend will accept.
    return { kind: "unknown", lexeme: "!", start, end: start + 1 };
  }
  return { kind: "operator", lexeme: c, start, end: start + 1 };
}

function readIdentifierOrKeyword(source: string, start: number): Token {
  let i = start;
  while (i < source.length && isIdentPart(source[i])) i++;
  const lexeme = source.slice(start, i);
  const upper = lexeme.toUpperCase();
  if (upper === "TRUE" || upper === "FALSE") {
    return { kind: "bool", lexeme: upper, start, end: i };
  }
  if (upper === "NULL") {
    return { kind: "null", lexeme: "NULL", start, end: i };
  }
  if (KEYWORDS.has(upper)) {
    return { kind: "keyword", lexeme: upper, start, end: i };
  }
  return { kind: "identifier", lexeme, start, end: i };
}

function isWhitespace(c: string): boolean {
  return c === " " || c === "\t" || c === "\n" || c === "\r" || c === "\f" || c === "\v";
}

function isDigit(c: string): boolean {
  return c >= "0" && c <= "9";
}

function isIdentStart(c: string): boolean {
  return (c >= "a" && c <= "z") || (c >= "A" && c <= "Z") || c === "_";
}

function isIdentPart(c: string): boolean {
  return isIdentStart(c) || isDigit(c);
}
