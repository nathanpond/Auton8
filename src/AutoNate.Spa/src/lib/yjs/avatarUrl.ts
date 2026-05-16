import { userCursorColor } from "./userColor";

// Deterministic avatar URL for a user. Returns a data: URI with an inline
// SVG — initials on a colored disc. The color reuses userCursorColor so a
// user's cursor color and avatar background line up visually.
//
// BlockNote's User type requires `avatarUrl: string` (not optional);
// AutoNate has no avatar storage today, so we generate one client-side.
// No network request needed to render a face.
export function avatarUrl(userId: string, displayName: string): string {
  const initials = computeInitials(displayName);
  const bg = userCursorColor(userId);
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">` +
    `<circle cx="32" cy="32" r="32" fill="${bg}"/>` +
    `<text x="50%" y="50%" text-anchor="middle" dominant-baseline="central" ` +
    `font-family="-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif" ` +
    `font-size="26" font-weight="600" fill="#fff">${escapeXml(initials)}</text>` +
    `</svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

function computeInitials(name: string): string {
  const trimmed = (name ?? "").trim();
  if (!trimmed) return "?";
  const parts = trimmed.split(/\s+/);
  if (parts.length >= 2) {
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }
  // One-word name: take the first two letters so we don't end up with a
  // single tiny letter floating in the disc.
  return trimmed.slice(0, 2).toUpperCase();
}

function escapeXml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}
