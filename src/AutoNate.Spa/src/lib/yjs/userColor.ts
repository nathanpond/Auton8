// Deterministic cursor color per user. Each user always gets the same
// color across sessions, so collaborators can recognize each other by hue
// alone (Notion / Google Docs convention). Palette chosen for visible
// contrast on both light and dark surfaces.
const PALETTE = [
  "#e03131", // red
  "#d6336c", // pink
  "#ae3ec9", // grape
  "#7048e8", // violet
  "#4263eb", // indigo
  "#1c7ed6", // blue
  "#1098ad", // cyan
  "#0ca678", // teal
  "#37b24d", // green
  "#74b816", // lime
  "#f08c00", // orange
  "#e8590c"  // vermilion
] as const;

export function userCursorColor(userId: string): string {
  if (!userId) return PALETTE[0];
  let h = 0;
  for (let i = 0; i < userId.length; i++) {
    h = (h * 31 + userId.charCodeAt(i)) | 0;
  }
  const idx = Math.abs(h) % PALETTE.length;
  return PALETTE[idx];
}
