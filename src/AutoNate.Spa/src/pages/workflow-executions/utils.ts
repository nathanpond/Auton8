export function formatTimestamp(iso: string | null): string {
  if (!iso) return "Not available";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

// Re-exported so existing importers keep working; the implementation moved to
// lib/describeError.ts in #352 so the studio can reach it too.
export { describeError } from "@/lib/describeError";
