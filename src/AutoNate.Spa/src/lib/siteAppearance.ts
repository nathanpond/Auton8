import { SiteAppearance, SiteAppearanceLogoMode } from "@/types/siteAppearance";
import { badgeTextColor } from "@/lib/statusAppearance";
import {
  Rgb,
  bestTextContrastOn,
  contrastRatio,
  parseHexColor as parseHex,
  relativeLuminance
} from "@/lib/contrast";
import { generateColors } from "@mantine/colors-generator";

const HEX_RE = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

export const DEFAULT_SITE_APPEARANCE: SiteAppearance = {
  siteName: "Auton8",
  logoMode: "icon",
  logoImageUrl: null,
  logoIcon: "fa fa-robot",
  logoText: "Auton8",
  loginTagline: "Sign in to continue to the automation dashboard",
  loginCoverImageUrl: "/assets/img/login-bg/space.jpg",
  // Darkened from the original #00acac (which computed 2.78:1 against #fff
  // and failed WCAG 1.4.11's 3:1 UI-component threshold). #008080 (CSS teal)
  // computes ~4.77:1 against #fff — comfortably past the 3:1 floor for UI
  // components and the 4.5:1 floor for text.
  primaryAccentColor: "#008080",
  headerBg: "#ffffff",
  headerColor: "#212529",
  topMenuBg: "#20252a",
  topMenuLinkColor: "#a6aaac",
  topMenuLinkHoverBg: "#20252a",
  topMenuLinkHoverColor: "#ffffff",
  topMenuLinkActiveBg: "#20252a",
  topMenuLinkActiveColor: "#ffffff",
  sidebarBg: "#ffffff",
  sidebarLinkColor: "#6c757d",
  sidebarLinkHoverColor: "#212529",
  sidebarActiveBg: "#f1f3f5",
  sidebarActiveColor: "#212529",
  sidebarIconColor: "#212529",
  sidebarSubmenuBg: "#ffffff",
  // #adb5bd was 2.07:1 on the white sidebar — the SITE / SECURITY group
  // headings that make a 30-item admin nav navigable were effectively
  // invisible to low-vision users (WCAG 1.4.3, archived-7). #5c636a is 6.09:1 and
  // still reads as a muted heading rather than body text.
  sidebarSectionColor: "#5c636a",
  surfaceBg: "#ffffff",
  surfaceSecondaryBg: "#dee2e6",
  surfaceTextColor: "#212529",
  surfaceDimmedColor: "#6c757d",
  borderColor: "#ced4da",
  dropdownBg: "#ffffff",
  modalBg: "#ffffff",
  secondaryButtonBg: "#ffffff",
  secondaryButtonTextColor: "#495057",
  secondaryButtonBorderColor: "#6c757d",
  secondaryButtonHoverBg: "#f1f3f5",
  secondaryButtonHoverTextColor: "#212529"
};

type PartialSiteAppearance = Partial<SiteAppearance> | null | undefined;

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

export function coerceSiteAppearance(source: PartialSiteAppearance): SiteAppearance {
  const appearance = source ?? {};

  return {
    siteName: normalizeRequiredText(appearance.siteName, DEFAULT_SITE_APPEARANCE.siteName),
    logoMode: normalizeLogoMode(appearance.logoMode),
    logoImageUrl: normalizeOptionalText(appearance.logoImageUrl),
    logoIcon: normalizeOptionalText(appearance.logoIcon),
    logoText: normalizeRequiredText(appearance.logoText, DEFAULT_SITE_APPEARANCE.logoText),
    loginTagline: normalizeOptionalText(appearance.loginTagline),
    loginCoverImageUrl: normalizeOptionalText(appearance.loginCoverImageUrl),
    primaryAccentColor: normalizeHexOrDefault(
      appearance.primaryAccentColor,
      DEFAULT_SITE_APPEARANCE.primaryAccentColor
    ),
    headerBg: normalizeHexOrDefault(appearance.headerBg, DEFAULT_SITE_APPEARANCE.headerBg),
    headerColor: normalizeHexOrDefault(appearance.headerColor, DEFAULT_SITE_APPEARANCE.headerColor),
    topMenuBg: normalizeHexOrDefault(appearance.topMenuBg, DEFAULT_SITE_APPEARANCE.topMenuBg),
    topMenuLinkColor: normalizeHexOrDefault(
      appearance.topMenuLinkColor,
      DEFAULT_SITE_APPEARANCE.topMenuLinkColor
    ),
    topMenuLinkHoverBg: normalizeHexOrDefault(
      appearance.topMenuLinkHoverBg,
      DEFAULT_SITE_APPEARANCE.topMenuLinkHoverBg
    ),
    topMenuLinkHoverColor: normalizeHexOrDefault(
      appearance.topMenuLinkHoverColor,
      DEFAULT_SITE_APPEARANCE.topMenuLinkHoverColor
    ),
    topMenuLinkActiveBg: normalizeHexOrDefault(
      appearance.topMenuLinkActiveBg,
      DEFAULT_SITE_APPEARANCE.topMenuLinkActiveBg
    ),
    topMenuLinkActiveColor: normalizeHexOrDefault(
      appearance.topMenuLinkActiveColor,
      DEFAULT_SITE_APPEARANCE.topMenuLinkActiveColor
    ),
    sidebarBg: normalizeHexOrDefault(appearance.sidebarBg, DEFAULT_SITE_APPEARANCE.sidebarBg),
    sidebarLinkColor: normalizeHexOrDefault(
      appearance.sidebarLinkColor,
      DEFAULT_SITE_APPEARANCE.sidebarLinkColor
    ),
    sidebarLinkHoverColor: normalizeHexOrDefault(
      appearance.sidebarLinkHoverColor,
      DEFAULT_SITE_APPEARANCE.sidebarLinkHoverColor
    ),
    sidebarActiveBg: normalizeHexOrDefault(
      appearance.sidebarActiveBg,
      DEFAULT_SITE_APPEARANCE.sidebarActiveBg
    ),
    sidebarActiveColor: normalizeHexOrDefault(
      appearance.sidebarActiveColor,
      DEFAULT_SITE_APPEARANCE.sidebarActiveColor
    ),
    sidebarIconColor: normalizeHexOrDefault(
      appearance.sidebarIconColor,
      DEFAULT_SITE_APPEARANCE.sidebarIconColor
    ),
    sidebarSubmenuBg: normalizeHexOrDefault(
      appearance.sidebarSubmenuBg,
      DEFAULT_SITE_APPEARANCE.sidebarSubmenuBg
    ),
    sidebarSectionColor: normalizeHexOrDefault(
      appearance.sidebarSectionColor,
      DEFAULT_SITE_APPEARANCE.sidebarSectionColor
    ),
    surfaceBg: normalizeHexOrDefault(appearance.surfaceBg, DEFAULT_SITE_APPEARANCE.surfaceBg),
    surfaceSecondaryBg: normalizeHexOrDefault(
      appearance.surfaceSecondaryBg,
      DEFAULT_SITE_APPEARANCE.surfaceSecondaryBg
    ),
    surfaceTextColor: normalizeHexOrDefault(
      appearance.surfaceTextColor,
      DEFAULT_SITE_APPEARANCE.surfaceTextColor
    ),
    surfaceDimmedColor: normalizeHexOrDefault(
      appearance.surfaceDimmedColor,
      DEFAULT_SITE_APPEARANCE.surfaceDimmedColor
    ),
    borderColor: normalizeHexOrDefault(appearance.borderColor, DEFAULT_SITE_APPEARANCE.borderColor),
    dropdownBg: normalizeHexOrDefault(appearance.dropdownBg, DEFAULT_SITE_APPEARANCE.dropdownBg),
    modalBg: normalizeHexOrDefault(appearance.modalBg, DEFAULT_SITE_APPEARANCE.modalBg),
    secondaryButtonBg: normalizeHexOrDefault(
      appearance.secondaryButtonBg,
      DEFAULT_SITE_APPEARANCE.secondaryButtonBg
    ),
    secondaryButtonTextColor: normalizeHexOrDefault(
      appearance.secondaryButtonTextColor,
      DEFAULT_SITE_APPEARANCE.secondaryButtonTextColor
    ),
    secondaryButtonBorderColor: normalizeHexOrDefault(
      appearance.secondaryButtonBorderColor,
      DEFAULT_SITE_APPEARANCE.secondaryButtonBorderColor
    ),
    secondaryButtonHoverBg: normalizeHexOrDefault(
      appearance.secondaryButtonHoverBg,
      DEFAULT_SITE_APPEARANCE.secondaryButtonHoverBg
    ),
    secondaryButtonHoverTextColor: normalizeHexOrDefault(
      appearance.secondaryButtonHoverTextColor,
      DEFAULT_SITE_APPEARANCE.secondaryButtonHoverTextColor
    )
  };
}

export function toUpdateSiteAppearanceRequest(
  appearance: SiteAppearance
): SiteAppearance {
  const normalized = coerceSiteAppearance(appearance);
  return {
    ...normalized,
    logoImageUrl: normalizeOptionalText(appearance.logoImageUrl),
    logoIcon: normalizeOptionalText(appearance.logoIcon),
    loginTagline: normalizeOptionalText(appearance.loginTagline),
    loginCoverImageUrl: normalizeOptionalText(appearance.loginCoverImageUrl)
  };
}

export function validateSiteAppearance(appearance: SiteAppearance): Partial<Record<keyof SiteAppearance, string>> {
  const errors: Partial<Record<keyof SiteAppearance, string>> = {};

  if (!appearance.siteName.trim()) {
    errors.siteName = "Site name is required.";
  }

  if (!appearance.logoText.trim()) {
    errors.logoText = "Brand text is required.";
  }

  const colorFields: Array<
    | "primaryAccentColor"
    | "headerBg"
    | "headerColor"
    | "topMenuBg"
    | "topMenuLinkColor"
    | "topMenuLinkHoverBg"
    | "topMenuLinkHoverColor"
    | "topMenuLinkActiveBg"
    | "topMenuLinkActiveColor"
    | "sidebarBg"
    | "sidebarLinkColor"
    | "sidebarLinkHoverColor"
    | "sidebarActiveBg"
    | "sidebarActiveColor"
    | "sidebarIconColor"
    | "sidebarSubmenuBg"
    | "sidebarSectionColor"
    | "surfaceBg"
    | "surfaceSecondaryBg"
    | "surfaceTextColor"
    | "surfaceDimmedColor"
    | "borderColor"
    | "dropdownBg"
    | "modalBg"
    | "secondaryButtonBg"
    | "secondaryButtonTextColor"
    | "secondaryButtonBorderColor"
    | "secondaryButtonHoverBg"
    | "secondaryButtonHoverTextColor"
  > = [
    "primaryAccentColor",
    "headerBg",
    "headerColor",
    "topMenuBg",
    "topMenuLinkColor",
    "topMenuLinkHoverBg",
    "topMenuLinkHoverColor",
    "topMenuLinkActiveBg",
    "topMenuLinkActiveColor",
    "sidebarBg",
    "sidebarLinkColor",
    "sidebarLinkHoverColor",
    "sidebarActiveBg",
    "sidebarActiveColor",
    "sidebarIconColor",
    "sidebarSubmenuBg",
    "sidebarSectionColor",
    "surfaceBg",
    "surfaceSecondaryBg",
    "surfaceTextColor",
    "surfaceDimmedColor",
    "borderColor",
    "dropdownBg",
    "modalBg",
    "secondaryButtonBg",
    "secondaryButtonTextColor",
    "secondaryButtonBorderColor",
    "secondaryButtonHoverBg",
    "secondaryButtonHoverTextColor"
  ];

  for (const field of colorFields) {
    if (!normalizeHex(appearance[field])) {
      errors[field] = "Use a valid hex color like #336699.";
    }
  }

  return errors;
}

// Surface-bg-vs-text and other load-bearing color pairs that need to clear
// WCAG 1.4.3 (text 4.5:1) or 1.4.11 (UI components / large text 3:1).
//
// Returned warnings drive a non-blocking advisory in the SiteAppearance
// editor — admins can still save a low-contrast theme if they have a reason
// (debug, dev, brand override), but they see the computed ratio and the
// pair that's failing.
export type ContrastWarning = {
  fieldKey: keyof SiteAppearance;
  pairLabel: string;
  fgKey: keyof SiteAppearance;
  bgKey: keyof SiteAppearance;
  ratio: number;
  required: number;
  reason: "text" | "ui";
};

type ContrastCheck = {
  fgKey: keyof SiteAppearance;
  bgKey: keyof SiteAppearance;
  pairLabel: string;
  required: number;
  reason: "text" | "ui";
};

const CONTRAST_CHECKS: ContrastCheck[] = [
  { fgKey: "surfaceTextColor", bgKey: "surfaceBg", pairLabel: "Body text on surface", required: 4.5, reason: "text" },
  { fgKey: "surfaceTextColor", bgKey: "surfaceSecondaryBg", pairLabel: "Body text on secondary surface", required: 4.5, reason: "text" },
  { fgKey: "surfaceDimmedColor", bgKey: "surfaceBg", pairLabel: "Secondary text on surface", required: 4.5, reason: "text" },
  { fgKey: "headerColor", bgKey: "headerBg", pairLabel: "Header text on header background", required: 4.5, reason: "text" },
  { fgKey: "topMenuLinkColor", bgKey: "topMenuBg", pairLabel: "Top-menu link on top-menu background", required: 4.5, reason: "text" },
  { fgKey: "topMenuLinkActiveColor", bgKey: "topMenuLinkActiveBg", pairLabel: "Active top-menu link", required: 4.5, reason: "text" },
  { fgKey: "sidebarLinkColor", bgKey: "sidebarBg", pairLabel: "Sidebar link on sidebar background", required: 4.5, reason: "text" },
  { fgKey: "sidebarActiveColor", bgKey: "sidebarActiveBg", pairLabel: "Active sidebar link", required: 4.5, reason: "text" },
  // 0.78rem bold uppercase is not WCAG "large text", so this needs the full
  // 4.5:1. Omitting the pair is why the default shipped at 2.07:1 without the
  // admin editor ever warning (archived-7).
  { fgKey: "sidebarSectionColor", bgKey: "sidebarBg", pairLabel: "Sidebar section heading", required: 4.5, reason: "text" },
  // UI components (3:1): the primary accent has to register against the
  // surface bg as a button / focus ring boundary.
  { fgKey: "primaryAccentColor", bgKey: "surfaceBg", pairLabel: "Primary accent against surface", required: 3.0, reason: "ui" }
];

export function checkContrastWarnings(appearance: SiteAppearance): ContrastWarning[] {
  const warnings: ContrastWarning[] = [];

  // Filled primary buttons and status pills take their text colour from
  // badgeTextColor, which now returns the better of black/white rather than a
  // YIQ guess (archived-14). For some accents neither reaches 4.5:1 — that is a
  // property of the accent, not something the text colour can fix, so the
  // admin has to be told rather than shipped an unreadable button.
  const accentText = bestTextContrastOn(appearance.primaryAccentColor);
  if (accentText > 0 && accentText < 4.5) {
    warnings.push({
      fieldKey: "primaryAccentColor",
      pairLabel: "Text on filled primary buttons",
      fgKey: "primaryAccentColor",
      bgKey: "primaryAccentColor",
      ratio: accentText,
      required: 4.5,
      reason: "text"
    });
  }

  for (const check of CONTRAST_CHECKS) {
    const fg = parseHex(appearance[check.fgKey] as string);
    const bg = parseHex(appearance[check.bgKey] as string);
    if (!fg || !bg) continue;
    const ratio = contrastRatio(fg, bg);
    if (ratio < check.required) {
      warnings.push({
        fieldKey: check.fgKey,
        pairLabel: check.pairLabel,
        fgKey: check.fgKey,
        bgKey: check.bgKey,
        ratio,
        required: check.required,
        reason: check.reason
      });
    }
  }
  return warnings;
}

export function areSiteAppearancesEqual(a: SiteAppearance, b: SiteAppearance): boolean {
  const left = toUpdateSiteAppearanceRequest(a);
  const right = toUpdateSiteAppearanceRequest(b);
  const keys = Object.keys(DEFAULT_SITE_APPEARANCE) as Array<keyof SiteAppearance>;

  return keys.every((key) => left[key] === right[key]);
}

export function applySiteAppearanceToDocument(
  appearance: SiteAppearance,
  doc: Document = document
): void {
  const normalized = coerceSiteAppearance(appearance);
  const rootStyle = doc.documentElement.style;

  const accent = normalized.primaryAccentColor;
  const accentText = badgeTextColor(accent);

  // SiteAppearance-owned tokens for the header/top-menu/sidebar surfaces.
  // Header chrome (`headerStyles.ts`) and the SiteAppearance preview read
  // from these `--app-*` names; the Mantine bridge vars below cover everything
  // Mantine components pick up automatically.
  setColorVar(rootStyle, "--app-header-bg", normalized.headerBg);
  setColorVar(rootStyle, "--app-header-color", normalized.headerColor);
  setColorVar(rootStyle, "--app-top-menu-bg", normalized.topMenuBg);
  setColorVar(rootStyle, "--app-top-menu-link-color", normalized.topMenuLinkColor);
  setColorVar(rootStyle, "--app-top-menu-link-hover-bg", normalized.topMenuLinkHoverBg);
  setColorVar(rootStyle, "--app-top-menu-link-hover-color", normalized.topMenuLinkHoverColor);
  setColorVar(rootStyle, "--app-top-menu-link-active-bg", normalized.topMenuLinkActiveBg);
  setColorVar(rootStyle, "--app-top-menu-link-active-color", normalized.topMenuLinkActiveColor);
  // Site Configuration left sidenav (ConfigLayout.css) reads these. The
  // chatbot AgentSidebar deliberately does NOT — it tracks the surface
  // theme via --mantine-color-body / --mantine-color-text instead, so the
  // two sidebars can be themed independently.
  setColorVar(rootStyle, "--app-sidebar-bg", normalized.sidebarBg);
  setColorVar(rootStyle, "--app-sidebar-text-color", normalized.sidebarLinkColor);
  setColorVar(rootStyle, "--app-sidebar-menu-link-color", normalized.sidebarLinkColor);
  setColorVar(rootStyle, "--app-sidebar-link-hover-color", normalized.sidebarLinkHoverColor);
  setColorVar(rootStyle, "--app-sidebar-component-active-bg", normalized.sidebarActiveBg);
  setColorVar(rootStyle, "--app-sidebar-component-active-color", normalized.sidebarActiveColor);
  setColorVar(rootStyle, "--app-sidebar-icon-color", normalized.sidebarIconColor);
  setColorVar(rootStyle, "--app-sidebar-section-color", normalized.sidebarSectionColor);

  // Mantine bridge: mirror SiteAppearance into Mantine's root CSS vars so
  // migrated pages and Mantine components share one theme source.
  setColorVar(rootStyle, "--mantine-color-body", normalized.surfaceBg);
  setColorVar(rootStyle, "--mantine-color-text", normalized.surfaceTextColor);
  setColorVar(rootStyle, "--mantine-color-default", normalized.surfaceBg);
  setColorVar(rootStyle, "--mantine-color-default-hover", normalized.surfaceSecondaryBg);
  setColorVar(rootStyle, "--mantine-color-default-color", normalized.surfaceTextColor);
  setColorVar(rootStyle, "--mantine-color-default-border", normalized.borderColor);
  // Dimmed surface text (Mantine `c="dimmed"`, `Text c="dimmed"`, etc.) is its
  // own knob in the appearance editor so admins can pick contrast directly.
  // The sidebar group-label color is a separate concern and stays bound to
  // --app-sidebar-section-color.
  setColorVar(rootStyle, "--mantine-color-dimmed", normalized.surfaceDimmedColor);

  // Live brand palette. The MantineProvider's static theme establishes that
  // `brand` is the primary color; here we overwrite the 10-shade tuple at
  // runtime so accent edits flicker through to every Mantine component using
  // `color="brand"` without re-rendering the theme object.
  //
  // generateColors() places the input color wherever it fits on a lightness
  // spectrum — NOT necessarily at index 6 (the "filled" slot). For an admin
  // configurable accent, the user expects their saved color to be the actual
  // button color, so we pin index 6 (and the `-filled` alias) to the input
  // and derive hover/light from manual mix.
  const brandShades = generateColors(accent).slice() as string[];
  brandShades[6] = accent;
  const filledHover = mixHex(accent, "#000000", 0.12);
  const lightBg = mixHex(accent, "#ffffff", 0.9);
  const lightHoverBg = mixHex(accent, "#ffffff", 0.8);

  brandShades.forEach((shade, idx) => {
    rootStyle.setProperty(`--mantine-color-brand-${idx}`, shade);
  });
  rootStyle.setProperty("--mantine-color-brand-light", lightBg);
  rootStyle.setProperty("--mantine-color-brand-light-hover", lightHoverBg);
  rootStyle.setProperty("--mantine-color-brand-light-color", accent);
  rootStyle.setProperty("--mantine-color-brand-filled", accent);
  rootStyle.setProperty("--mantine-color-brand-filled-hover", filledHover);
  rootStyle.setProperty("--mantine-color-brand-outline", accent);
  rootStyle.setProperty("--mantine-color-brand-outline-hover", lightBg);
  // Mantine's primary-color aliases mirror the brand palette; pin them too
  // so anything that resolves to the primary color (e.g. <Tabs>, <Indicator>
  // defaults) picks up the same accent.
  rootStyle.setProperty("--mantine-primary-color-filled", accent);
  rootStyle.setProperty("--mantine-primary-color-filled-hover", filledHover);
  rootStyle.setProperty("--mantine-primary-color-light", lightBg);
  rootStyle.setProperty("--mantine-primary-color-light-hover", lightHoverBg);
  rootStyle.setProperty("--mantine-primary-color-light-color", accent);
  rootStyle.setProperty("--mantine-primary-color-contrast", accentText);

  // Infer color scheme from surfaceBg luminance (Phase I; an explicit field on
  // SiteAppearance can replace this later).
  const scheme = inferColorScheme(normalized.surfaceBg);
  doc.documentElement.setAttribute("data-mantine-color-scheme", scheme);
}

export function inferColorScheme(surfaceBg: string): "light" | "dark" {
  const rgb = parseHex(surfaceBg) ?? parseHex(DEFAULT_SITE_APPEARANCE.surfaceBg)!;
  const lum = relativeLuminance(rgb);
  return lum < 0.4 ? "dark" : "light";
}

function normalizeRequiredText(value: string | null | undefined, fallback: string): string {
  const trimmed = (value ?? "").trim();
  return trimmed || fallback;
}

function normalizeOptionalText(value: string | null | undefined): string | null {
  const trimmed = (value ?? "").trim();
  return trimmed ? trimmed : null;
}

function normalizeLogoMode(value: SiteAppearanceLogoMode | string | null | undefined): SiteAppearanceLogoMode {
  return value === "image" ? "image" : "icon";
}

function normalizeHexOrDefault(value: string | null | undefined, fallback: string): string {
  return normalizeHex(value ?? "") ?? fallback;
}

function mixHex(color: string, target: string, ratio: number): string {
  const sourceRgb = parseHex(color) ?? parseHex(DEFAULT_SITE_APPEARANCE.primaryAccentColor)!;
  const targetRgb = parseHex(target) ?? parseHex("#000000")!;

  const mixChannel = (source: number, end: number) =>
    Math.round(source + ((end - source) * ratio));

  return rgbToHex({
    r: mixChannel(sourceRgb.r, targetRgb.r),
    g: mixChannel(sourceRgb.g, targetRgb.g),
    b: mixChannel(sourceRgb.b, targetRgb.b)
  });
}

function rgbToHex(rgb: Rgb): string {
  return `#${toHex(rgb.r)}${toHex(rgb.g)}${toHex(rgb.b)}`;
}

function toHex(value: number): string {
  return value.toString(16).padStart(2, "0");
}

function setColorVar(style: CSSStyleDeclaration, name: string, value: string): void {
  style.setProperty(name, value);
}
