// WCAG contrast primitives, shared by the appearance editor's warnings and by
// the badge/button text-colour picker (archived-14).
//
// These lived privately in siteAppearance.ts while statusAppearance.ts carried
// its own, different, non-WCAG heuristic. siteAppearance.ts imports
// `badgeTextColor` from statusAppearance.ts, so statusAppearance could not
// simply import the real math back — hence a third module both can depend on.

export type Rgb = { r: number; g: number; b: number };

const HEX_RE = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

export function normalizeHexColor(value: string): string | null {
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

export function parseHexColor(value: string): Rgb | null {
  const normalized = normalizeHexColor(value);
  if (!normalized) return null;
  const hex = normalized.slice(1);
  return {
    r: parseInt(hex.slice(0, 2), 16),
    g: parseInt(hex.slice(2, 4), 16),
    b: parseInt(hex.slice(4, 6), 16)
  };
}

// WCAG 2.x relative luminance — gamma-corrected, with the perceptual channel
// weights from the spec. Not the same thing as YIQ brightness, which is a
// video-encoding measure and has no defined relationship to contrast ratio.
export function relativeLuminance(rgb: Rgb): number {
  const channel = (c: number) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(rgb.r) + 0.7152 * channel(rgb.g) + 0.0722 * channel(rgb.b);
}

export function contrastRatio(a: Rgb, b: Rgb): number {
  const lA = relativeLuminance(a);
  const lB = relativeLuminance(b);
  const lighter = Math.max(lA, lB);
  const darker = Math.min(lA, lB);
  return (lighter + 0.05) / (darker + 0.05);
}

// The two colours anything filled in this app puts text in.
export const DARK_TEXT = "#111111";
export const LIGHT_TEXT = "#ffffff";

// Pick whichever of the two actually reads better on `background`, measured.
//
// The previous heuristic thresholded YIQ brightness at 160, which for a
// mid-tone accent returns white on a colour that computes well below 4.5:1 —
// #00acac took white at 2.80:1. Comparing real contrast ratios cannot make
// that mistake: it returns the better of the two options for any input.
//
// It can still return a colour below 4.5:1, because for some backgrounds
// neither black nor white reaches it. That is a property of the chosen
// background, not something a text colour can fix, which is why the appearance
// editor also warns on the pair rather than this silently papering over it.
export function bestTextColorOn(background: string): string {
  const bg = parseHexColor(background);
  if (!bg) return DARK_TEXT;
  const darkRatio = contrastRatio(parseHexColor(DARK_TEXT)!, bg);
  const lightRatio = contrastRatio(parseHexColor(LIGHT_TEXT)!, bg);
  return darkRatio >= lightRatio ? DARK_TEXT : LIGHT_TEXT;
}

// The best ratio achievable on `background` with either text colour. Below
// 4.5 means no text colour can make this background readable for body text.
export function bestTextContrastOn(background: string): number {
  const bg = parseHexColor(background);
  if (!bg) return 0;
  return Math.max(
    contrastRatio(parseHexColor(DARK_TEXT)!, bg),
    contrastRatio(parseHexColor(LIGHT_TEXT)!, bg)
  );
}
