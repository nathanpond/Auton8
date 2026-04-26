import { StatusAppearanceEntry } from "@/types/statusAppearance";

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

export function badgeTextColor(color: string): string {
  const normalized = normalizeHex(color);
  if (!normalized) return "#111111";
  const hex = normalized.slice(1);
  const r = parseInt(hex.slice(0, 2), 16);
  const g = parseInt(hex.slice(2, 4), 16);
  const b = parseInt(hex.slice(4, 6), 16);
  const luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
  return luminance > 160 ? "#111111" : "#ffffff";
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
