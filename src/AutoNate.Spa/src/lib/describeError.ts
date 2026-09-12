/**
 * The server's own words, when it sent any (#256, #352).
 *
 * A publish refusal answers `{ errors: [...] }` and carries no `message`, so a
 * reader that looks only at `data.message` renders "Request failed with status
 * code 400" -- and every refusal message this milestone wrote, naming the element
 * and the remedy, was invisible on that path.
 *
 * **Why this lives in `lib/` rather than beside one page.** It was fixed once, in
 * `pages/workflow-executions/utils.ts`, and the studio kept its own local copy and
 * kept rendering the axios default. There are 36 files in this SPA with a private
 * `describeError`; the fix landing on one of them and not on the one the publish
 * path uses is what #352 is. A function every page can import is the only version
 * of this fix that stays fixed.
 *
 * The remaining 34 copies are not touched here -- consolidating them is a
 * refactor of its own, and doing it blind with no SPA test runner (#323) would be
 * trading one silent regression for thirty-four.
 */
export function describeError(error: unknown): string {
  if (!(error instanceof Error)) return String(error);

  const data = (error as {
    response?: { data?: { message?: string; errors?: unknown; title?: string } };
  }).response?.data;

  if (typeof data?.message === "string" && data.message.trim() !== "") {
    return data.message;
  }

  // Validation refusals: one per line, because they are a list of things to fix
  // and joining them with commas runs two element names together.
  if (Array.isArray(data?.errors)) {
    const errors = data.errors.filter(
      (entry): entry is string => typeof entry === "string" && entry.trim() !== ""
    );
    if (errors.length > 0) return errors.join("\n");
  }

  // ASP.NET's ValidationProblemDetails shape: { errors: { field: [messages] } }.
  if (data?.errors && typeof data.errors === "object") {
    const errors = Object.values(data.errors as Record<string, unknown>)
      .flatMap((entry) => (Array.isArray(entry) ? entry : [entry]))
      .filter((entry): entry is string => typeof entry === "string" && entry.trim() !== "");
    if (errors.length > 0) return errors.join("\n");
  }

  if (typeof data?.title === "string" && data.title.trim() !== "") {
    return data.title;
  }

  return error.message;
}
