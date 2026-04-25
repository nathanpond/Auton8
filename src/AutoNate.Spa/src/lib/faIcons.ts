import iconsData from "./fa-icons.json";

export type FaIconStyle = "fa-solid" | "fa-regular" | "fa-brands";

export interface FaIcon {
  name: string;
  styles: FaIconStyle[];
  terms: string[];
}

export const FA_ICONS: FaIcon[] = iconsData as unknown as FaIcon[];

const BY_NAME = new Map(FA_ICONS.map((i) => [i.name, i]));

export function findIcon(name: string): FaIcon | undefined {
  return BY_NAME.get(stripFaPrefix(name));
}

export function stripFaPrefix(value: string): string {
  return value.replace(/^fa-/, "").trim();
}

export function preferredStyle(icon: FaIcon): FaIconStyle {
  if (icon.styles.includes("fa-solid")) return "fa-solid";
  if (icon.styles.includes("fa-regular")) return "fa-regular";
  return icon.styles[0];
}

export function searchIcons(query: string, limit = 50): FaIcon[] {
  const q = stripFaPrefix(query.toLowerCase());
  if (!q) return FA_ICONS.slice(0, limit);

  const results: { icon: FaIcon; score: number }[] = [];
  for (const icon of FA_ICONS) {
    const name = icon.name;
    let score = -1;
    if (name === q) score = 0;
    else if (name.startsWith(q)) score = 1;
    else if (name.includes(q)) score = 2;
    else if (icon.terms.some((t) => t === q)) score = 3;
    else if (icon.terms.some((t) => t.startsWith(q))) score = 4;
    else if (icon.terms.some((t) => t.includes(q))) score = 5;
    if (score >= 0) results.push({ icon, score });
  }
  results.sort((a, b) => a.score - b.score || a.icon.name.localeCompare(b.icon.name));
  return results.slice(0, limit).map((r) => r.icon);
}
