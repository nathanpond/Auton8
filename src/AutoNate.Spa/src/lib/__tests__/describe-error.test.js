import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { describeError } from "../describeError.ts";

/**
 * The server's own words, when it sent any (#256, #352, #323).
 *
 * A publish refusal answers `{ errors: [...] }` and carries no `message`. A
 * reader that looks only at `data.message` renders "Request failed with status
 * code 400" -- and every refusal message M4 wrote, naming the element and the
 * remedy, was invisible on that path.
 *
 * #352 fixed it and could not test it: there was no SPA test runner. This is
 * that test.
 */
function axiosLike(data) {
  const error = new Error("Request failed with status code 400");
  error.response = { data };
  return error;
}

describe("describeError surfaces what the server actually sent", () => {
  it("renders a publish refusal list, not the axios default", () => {
    const rendered = describeError(
      axiosLike({ errors: ["'Pool' cannot be deployed: no engine support.", "'Task_1' has no name."] })
    );

    expect(rendered).toContain("cannot be deployed");
    expect(rendered).not.toContain("Request failed with status code");
  });

  it("puts each refusal on its own line, so two element names do not run together", () => {
    const rendered = describeError(axiosLike({ errors: ["first", "second"] }));
    expect(rendered).toBe("first\nsecond");
  });

  it("prefers an explicit message when the server sent one", () => {
    expect(describeError(axiosLike({ message: "Nope", errors: ["ignored"] }))).toBe("Nope");
  });

  it("reads ASP.NET's ValidationProblemDetails shape, where errors is an object", () => {
    const rendered = describeError(axiosLike({ errors: { name: ["Name is required."] } }));
    expect(rendered).toBe("Name is required.");
  });

  it("falls back to title, then to the error's own message", () => {
    expect(describeError(axiosLike({ title: "Bad Request" }))).toBe("Bad Request");
    expect(describeError(axiosLike({}))).toBe("Request failed with status code 400");
  });

  it("ignores blank and non-string entries rather than rendering empty lines", () => {
    expect(describeError(axiosLike({ errors: ["", "  ", 42, "real"] }))).toBe("real");
    expect(describeError(axiosLike({ message: "   ", errors: ["real"] }))).toBe("real");
  });

  it("describes a non-Error without throwing", () => {
    expect(describeError("plain string")).toBe("plain string");
  });
});

describe("the studio reads the shared describer, not a private copy", () => {
  it("imports it from lib rather than defining its own", () => {
    const here = path.dirname(fileURLToPath(import.meta.url));
    const studio = readFileSync(
      path.join(here, "..", "..", "pages", "workflow", "WorkflowStudio.tsx"),
      "utf8"
    );

    // #352: the fix landed once in `pages/workflow-executions/utils.ts` while the
    // studio kept a private copy and went on rendering the axios default. There
    // are 36 files in this SPA with their own `describeError`; this pins the one
    // the publish path uses.
    expect(studio).toContain('import { describeError } from "@/lib/describeError"');
    expect(studio).not.toMatch(/function describeError\s*\(/);
  });
});
