import { StatusAppearanceEntry } from "@/types/statusAppearance";
import { DARK_TEXT, bestTextColorOn } from "@/lib/contrast";

const HEX_RE = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

export function normalizeHex(value: string): string | null {
  const trimmed = value.trim();
  if (!HEX_RE.test(trimmed)) return null;
  if (trimmed.length === 4) {
    const r = trimmed[1];
    const g = trimmed[2];
    const b = trimmed[3];
    return `#${r}${r}${g}${g}${b}${b}`.toLowerCase();
  }
  return trimmed.toLowerCase();
}

// Text colour for a filled badge, pill or button (#14).
//
// This used to threshold YIQ brightness — (0.299R + 0.587G + 0.114B) > 160 —
// which is a video-encoding measure with no defined relationship to contrast
// ratio. For a mid-tone accent it chose white on a colour that computes far
// below 4.5:1 (#00acac → white → 2.80:1), and the result feeds
// --mantine-primary-color-contrast, so it was the text colour of every filled
// primary button in the app, not just status pills.
//
// Now measured: whichever of black/white actually has the higher WCAG ratio.
export function badgeTextColor(color: string): string {
  const normalized = normalizeHex(color);
  if (!normalized) return DARK_TEXT;
  return bestTextColorOn(normalized);
}

export function resolveStatusBadgeColor(
  status: string,
  entries: StatusAppearanceEntry[]
): string {
  const exact = entries.find(
    (entry) => entry.status.trim().toLowerCase() === status.trim().toLowerCase()
  );
  if (exact) {
    return normalizeHex(exact.color) ?? "#d3d3d3";
  }

  const siteDefault = entries.find(
    (entry) => entry.status.trim().toLowerCase() === "site_default"
  );
  if (siteDefault) {
    return normalizeHex(siteDefault.color) ?? "#d3d3d3";
  }

  return "#d3d3d3";
}
