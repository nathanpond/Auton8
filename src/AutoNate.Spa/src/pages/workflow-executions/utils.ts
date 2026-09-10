export function formatTimestamp(iso: string | null): string {
  if (!iso) return "Not available";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

/**
 * The server's own words, when it sent any (#256).
 *
 * This read only `data.message`, so a publish refusal -- which answers
 * `{ errors: [...] }` and carries no `message` -- rendered as "Request failed
 * with status code 400". Every refusal message this milestone wrote, naming the
 * element and the remedy and how to fix it, was invisible on that path.
 *
 * It was masked in the studio's usual flow, where `prepareAndStore` runs first
 * and surfaces prepare's errors, but the call-activity refusal is raised at
 * publish and prepare does not check it -- so the shape was already reachable
 * before anything added a second producer of it.
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
