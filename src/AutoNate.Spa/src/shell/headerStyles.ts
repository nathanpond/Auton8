import { CSSProperties, MouseEvent } from "react";

// Shared color tokens for the dark Mantine top bar. Drawn from the SiteAppearance
// CSS vars (which the bridge in `applySiteAppearanceToDocument` keeps in sync),
// with fallbacks for contexts where those vars haven't loaded yet.
export const HEADER_BG = "var(--app-top-menu-bg, #20252a)";
export const HEADER_FG = "var(--app-top-menu-link-color, rgba(255,255,255,0.78))";
export const HEADER_HOVER_BG = "var(--app-top-menu-link-hover-bg, rgba(255,255,255,0.08))";
export const HEADER_ACTIVE_BG = "var(--app-top-menu-link-active-bg, rgba(255,255,255,0.12))";
export const HEADER_ACTIVE_FG = "var(--app-top-menu-link-active-color, #ffffff)";

// Inline style for a 40x40 icon button that sits in the dark top bar — used
// for notification bell, chatbot trigger, icon-menu items, and the user menu
// avatar trigger so they all match. ActionIcon defaults to dark-on-light, which
// is invisible against the dark header — this replaces it with explicit
// color tokens that are visible at rest AND get a brighter background on hover.
//
// `fontSize: 16` + `lineHeight: 1` shrink the FA glyph's line-box to the em-box
// so flex alignItems:center actually centers the visible glyph, not the
// baseline-anchored character. Without this the icons sit a few px above the
// avatar / text on the same row.
export const headerIconButtonStyle: CSSProperties = {
  width: 40,
  height: 40,
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  fontSize: 16,
  lineHeight: 1,
  color: HEADER_FG,
  background: "transparent",
  border: 0,
  cursor: "pointer",
  borderRadius: 4,
  transition: "background 120ms ease, color 120ms ease"
};

export function applyHeaderHover(e: MouseEvent<HTMLElement>) {
  e.currentTarget.style.background = HEADER_HOVER_BG;
  e.currentTarget.style.color = HEADER_ACTIVE_FG;
}

export function clearHeaderHover(e: MouseEvent<HTMLElement>) {
  e.currentTarget.style.background = "transparent";
  e.currentTarget.style.color = HEADER_FG;
}
