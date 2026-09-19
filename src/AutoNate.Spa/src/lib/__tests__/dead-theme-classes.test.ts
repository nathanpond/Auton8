import { describe, it, expect } from "vitest";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Nothing in the SPA may carry a ColorAdmin / Bootstrap class (#235).
 *
 * The migration to Mantine removed that stylesheet from the bundle, so these
 * class names now match no rule anywhere. That is a worse failure than a broken
 * one: `<input className="form-control">` renders a browser-default input beside
 * Mantine controls, picks up none of the site's theming, and throws nothing. The
 * page looks *slightly* wrong, in a way easy to read as someone's deliberate
 * choice.
 *
 * #235 swept 110 of them out of WorkflowStudio.tsx alone, which they had reached
 * one copy-paste at a time. This exists so the next one is caught at the merge
 * gate rather than by the next person who looks closely at a form -- a cleanup
 * with no guard is a cleanup that gets to be done again.
 *
 * Deliberately NOT a lint rule: `react/forbid-dom-props` cannot see inside a
 * template literal, and the SPA's ratchet counts warnings, so a new violation
 * there would be a number going up rather than a build going red.
 */

const SPA_SRC = fileURLToPath(new URL("../..", import.meta.url));

/**
 * Whole class tokens only.
 *
 * Substring matching would be wrong in both directions here: `panel-body` is a
 * substring of the project's own `workflow-rsb-panel-body`, and `row` appears
 * inside a dozen legitimate names. Every entry below is compared against a
 * complete token split out of a className value.
 */
const DEAD_CLASSES = new Set([
  // form controls
  "form-control",
  "form-control-sm",
  "form-control-lg",
  "form-select",
  "form-select-sm",
  "form-check",
  "form-check-input",
  "form-check-label",
  "form-check-inline",
  "form-label",
  "form-text",
  "input-group",
  "input-group-text",
  "is-invalid",
  "is-valid",
  "invalid-feedback",
  "valid-feedback",
  // buttons
  "btn",
  "btn-sm",
  "btn-lg",
  "btn-group",
  "btn-primary",
  "btn-secondary",
  "btn-success",
  "btn-danger",
  "btn-warning",
  "btn-info",
  "btn-light",
  "btn-dark",
  "btn-link",
  "btn-outline-primary",
  "btn-outline-secondary",
  "btn-outline-success",
  "btn-outline-danger",
  // alerts, badges, panels
  "alert",
  "alert-primary",
  "alert-secondary",
  "alert-success",
  "alert-danger",
  "alert-warning",
  "alert-info",
  "badge",
  "bg-primary",
  "bg-secondary",
  "bg-success",
  "bg-danger",
  "bg-warning",
  "bg-info",
  "bg-light",
  "bg-dark",
  "panel",
  "panel-body",
  "panel-heading",
  "panel-title",
  "panel-footer",
  // layout + typography utilities
  "d-flex",
  "d-none",
  "d-block",
  "d-inline",
  "d-inline-block",
  "flex-column",
  "flex-row",
  "flex-wrap",
  "align-items-center",
  "align-items-start",
  "align-items-end",
  "justify-content-center",
  "justify-content-between",
  "justify-content-end",
  "text-muted",
  "text-body",
  "text-warning",
  "text-danger",
  "text-success",
  "text-primary",
  "text-center",
  "text-end",
  "font-monospace",
  "fw-bold",
  "small",
  // spacing utilities, enumerated rather than pattern-matched so a project
  // class that merely looks like one cannot be swept up by accident
  ...["m", "mt", "mb", "ms", "me", "mx", "my", "p", "pt", "pb", "ps", "pe", "px", "py"].flatMap(
    (prefix) => [0, 1, 2, 3, 4, 5].map((step) => `${prefix}-${step}`)
  ),
  // icon font that is not installed -- CLAUDE.md names it explicitly
  ...[
    "bi",
    "bi-play-fill",
    "bi-info-circle",
    "bi-check",
    "bi-x",
    "bi-trash",
    "bi-pencil"
  ]
]);

/** Every className value in a file: plain strings and template literals. */
const CLASSNAME = /className=(?:"([^"]*)"|\{`([^`]*)`\})/g;

function tokensIn(source: string): string[] {
  const found: string[] = [];
  for (const match of source.matchAll(CLASSNAME)) {
    const value = match[1] ?? match[2] ?? "";
    // A template literal interpolates expressions; split on the delimiters as
    // well as whitespace so `${a ? "btn-primary" : "x"}` yields its literals.
    for (const token of value.split(/[\s${}?:"'`]+/)) {
      if (token) found.push(token);
    }
  }
  return found;
}

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    if (entry === "node_modules" || entry === "dist" || entry === "__tests__") continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      sourceFiles(full, acc);
    } else if (/\.(tsx|ts|jsx|js)$/.test(entry)) {
      acc.push(full);
    }
  }
  return acc;
}

describe("dead ColorAdmin / Bootstrap classes", () => {
  // The detector first, on a string that is definitely a violation. A scanner
  // with a wrong regex or a wrong root reports "clean" exactly as loudly as a
  // clean codebase does, and that is the failure this test would otherwise be.
  it("recognises a violation when it sees one", () => {
    const planted = tokensIn('<input className="form-control mt-2" />').filter((t) =>
      DEAD_CLASSES.has(t)
    );
    expect(planted.sort()).toEqual(["form-control", "mt-2"]);

    const interpolated = tokensIn(
      "<button className={`btn btn-sm ${on ? \"btn-primary\" : \"x\"}`} />"
    ).filter((t) => DEAD_CLASSES.has(t));
    expect(interpolated).toContain("btn-primary");
  });

  it("does not flag a project class that merely contains one", () => {
    const ours = tokensIn('<div className="workflow-rsb-panel-body notification-unread" />').filter(
      (t) => DEAD_CLASSES.has(t)
    );
    expect(ours).toEqual([]);
  });

  it("finds none in the SPA source", () => {
    const files = sourceFiles(SPA_SRC);

    // The corpus itself is asserted. A glob that silently matched nothing is
    // the same green as a clean sweep, and this file has no other way to tell
    // those apart.
    expect(files.length).toBeGreaterThan(100);

    const offences: string[] = [];
    for (const file of files) {
      const source = readFileSync(file, "utf8");
      const hits = [...new Set(tokensIn(source).filter((t) => DEAD_CLASSES.has(t)))];
      if (hits.length > 0) {
        offences.push(`${relative(SPA_SRC, file).split(sep).join("/")}: ${hits.sort().join(", ")}`);
      }
    }

    expect(offences).toEqual([]);
  });
});
