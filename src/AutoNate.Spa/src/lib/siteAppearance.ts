import { SiteAppearance, SiteAppearanceLogoMode } from "@/types/siteAppearance";

const HEX_RE = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

export const DEFAULT_SITE_APPEARANCE: SiteAppearance = {
  siteName: "Auto Nate",
  logoMode: "icon",
  logoImageUrl: null,
  logoIcon: "fa fa-robot",
  logoText: "Auto Nate",
  loginTagline: "Sign in to continue to the automation dashboard",
  loginCoverImageUrl: "/spa/assets/img/login-bg/login-bg-17.jpg",
  primaryAccentColor: "#00acac",
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
  sidebarSectionColor: "#adb5bd",
  surfaceBg: "#ffffff",
  surfaceSecondaryBg: "#dee2e6",
  surfaceTextColor: "#212529",
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

type Rgb = {
  r: number;
  g: number;
  b: number;
};

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
  const rgb = parseHex(color);
  if (!rgb) return "#111111";
  const luminance = (0.299 * rgb.r) + (0.587 * rgb.g) + (0.114 * rgb.b);
  return luminance > 160 ? "#111111" : "#ffffff";
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
  const accentHover = mixHex(accent, "#000000", 0.25);
  const accentDisabled = mixHex(accent, "#ffffff", 0.25);
  const accentText = badgeTextColor(accent);
  const accentHoverText = badgeTextColor(accentHover);
  const accentDisabledText = badgeTextColor(accentDisabled);

  setColorVar(rootStyle, "--bs-app-theme", accent);
  setRgbVar(rootStyle, "--bs-app-theme-rgb", accent);
  setColorVar(rootStyle, "--bs-app-theme-color", accentText);
  setRgbVar(rootStyle, "--bs-app-theme-color-rgb", accentText);
  setColorVar(rootStyle, "--bs-app-theme-hover", accentHover);
  setColorVar(rootStyle, "--bs-app-theme-hover-border-color", accentHover);
  setColorVar(rootStyle, "--bs-app-theme-hover-color", accentHoverText);
  setColorVar(rootStyle, "--bs-app-theme-active", accentHover);
  setColorVar(rootStyle, "--bs-app-theme-active-border-color", accentHover);
  setColorVar(rootStyle, "--bs-app-theme-active-color", accentHoverText);
  setColorVar(rootStyle, "--bs-app-theme-disabled", accentDisabled);
  setColorVar(rootStyle, "--bs-app-theme-disabled-border-color", accentDisabled);
  setColorVar(rootStyle, "--bs-app-theme-disabled-color", accentDisabledText);
  setColorVar(rootStyle, "--bs-primary", accent);
  setRgbVar(rootStyle, "--bs-primary-rgb", accent);
  setColorVar(rootStyle, "--bs-link-color", accent);
  setColorVar(rootStyle, "--bs-link-hover-color", accentHover);

  setColorVar(rootStyle, "--bs-app-header-bg", normalized.headerBg);
  setRgbVar(rootStyle, "--bs-app-header-bg-rgb", normalized.headerBg);
  setColorVar(rootStyle, "--bs-app-header-color", normalized.headerColor);
  setRgbVar(rootStyle, "--bs-app-header-color-rgb", normalized.headerColor);

  setColorVar(rootStyle, "--bs-app-top-menu-bg", normalized.topMenuBg);
  setColorVar(rootStyle, "--bs-app-top-menu-link-color", normalized.topMenuLinkColor);
  setColorVar(rootStyle, "--bs-app-top-menu-link-hover-bg", normalized.topMenuLinkHoverBg);
  setColorVar(rootStyle, "--bs-app-top-menu-link-hover-color", normalized.topMenuLinkHoverColor);
  setColorVar(rootStyle, "--bs-app-top-menu-link-active-bg", normalized.topMenuLinkActiveBg);
  setColorVar(rootStyle, "--bs-app-top-menu-link-active-color", normalized.topMenuLinkActiveColor);
  setColorVar(rootStyle, "--bs-app-top-menu-control-link-bg", normalized.topMenuBg);
  setColorVar(rootStyle, "--bs-app-top-menu-control-link-color", normalized.topMenuLinkColor);
  setColorVar(rootStyle, "--bs-app-top-menu-control-link-hover-bg", normalized.topMenuLinkHoverBg);
  setColorVar(rootStyle, "--bs-app-top-menu-control-link-hover-color", normalized.topMenuLinkHoverColor);

  setColorVar(rootStyle, "--bs-app-sidebar-bg", normalized.sidebarBg);
  setRgbVar(rootStyle, "--bs-app-sidebar-bg-rgb", normalized.sidebarBg);
  setColorVar(rootStyle, "--bs-app-sidebar-menu-link-color", normalized.sidebarLinkColor);
  setColorVar(rootStyle, "--bs-app-sidebar-menu-link-hover-color", normalized.sidebarLinkHoverColor);
  setColorVar(rootStyle, "--bs-app-sidebar-component-active-bg", normalized.sidebarActiveBg);
  setColorVar(rootStyle, "--bs-app-sidebar-component-active-color", normalized.sidebarActiveColor);
  setColorVar(rootStyle, "--bs-app-sidebar-menu-icon-color", normalized.sidebarIconColor);
  setColorVar(rootStyle, "--bs-app-sidebar-menu-submenu-bg", normalized.sidebarSubmenuBg);
  setColorVar(rootStyle, "--bs-app-sidebar-menu-header-color", normalized.sidebarSectionColor);
  setColorVar(rootStyle, "--bs-app-sidebar-float-submenu-bg", normalized.sidebarSubmenuBg);

  setColorVar(rootStyle, "--bs-body-bg", normalized.surfaceBg);
  setRgbVar(rootStyle, "--bs-body-bg-rgb", normalized.surfaceBg);
  setColorVar(rootStyle, "--bs-body-color", normalized.surfaceTextColor);
  setRgbVar(rootStyle, "--bs-body-color-rgb", normalized.surfaceTextColor);
  setColorVar(rootStyle, "--bs-component-bg", normalized.surfaceBg);
  setRgbVar(rootStyle, "--bs-component-bg-rgb", normalized.surfaceBg);
  setColorVar(rootStyle, "--bs-component-secondary-bg", normalized.surfaceSecondaryBg);
  setRgbVar(rootStyle, "--bs-component-secondary-bg-rgb", normalized.surfaceSecondaryBg);
  setColorVar(rootStyle, "--bs-component-color", normalized.surfaceTextColor);
  setRgbVar(rootStyle, "--bs-component-color-rgb", normalized.surfaceTextColor);
  setColorVar(rootStyle, "--bs-component-border-color", normalized.borderColor);
  setRgbVar(rootStyle, "--bs-component-border-color-rgb", normalized.borderColor);
  setColorVar(rootStyle, "--bs-border-color", normalized.borderColor);
  setRgbVar(rootStyle, "--bs-border-color-rgb", normalized.borderColor);
  setColorVar(rootStyle, "--bs-component-dropdown-bg", normalized.dropdownBg);
  setRgbVar(rootStyle, "--bs-component-dropdown-bg-rgb", normalized.dropdownBg);
  setColorVar(rootStyle, "--bs-component-dropdown-border-color", normalized.borderColor);
  setRgbVar(rootStyle, "--bs-component-dropdown-border-color-rgb", normalized.borderColor);
  setColorVar(rootStyle, "--bs-component-modal-bg", normalized.modalBg);
  setRgbVar(rootStyle, "--bs-component-modal-bg-rgb", normalized.modalBg);
  setColorVar(rootStyle, "--bs-component-modal-border-color", normalized.borderColor);
  setRgbVar(rootStyle, "--bs-component-modal-border-color-rgb", normalized.borderColor);
  setColorVar(rootStyle, "--bs-site-secondary-btn-bg", normalized.secondaryButtonBg);
  setColorVar(rootStyle, "--bs-site-secondary-btn-color", normalized.secondaryButtonTextColor);
  setColorVar(rootStyle, "--bs-site-secondary-btn-border-color", normalized.secondaryButtonBorderColor);
  setColorVar(rootStyle, "--bs-site-secondary-btn-hover-bg", normalized.secondaryButtonHoverBg);
  setColorVar(rootStyle, "--bs-site-secondary-btn-hover-color", normalized.secondaryButtonHoverTextColor);
  setColorVar(rootStyle, "--bs-site-secondary-btn-hover-border-color", normalized.secondaryButtonHoverBg);
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

function parseHex(value: string): Rgb | null {
  const normalized = normalizeHex(value);
  if (!normalized) return null;
  const hex = normalized.slice(1);
  return {
    r: parseInt(hex.slice(0, 2), 16),
    g: parseInt(hex.slice(2, 4), 16),
    b: parseInt(hex.slice(4, 6), 16)
  };
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

function toRgbCss(value: string): string {
  const rgb = parseHex(value) ?? parseHex("#000000")!;
  return `${rgb.r}, ${rgb.g}, ${rgb.b}`;
}

function setColorVar(style: CSSStyleDeclaration, name: string, value: string): void {
  style.setProperty(name, value);
}

function setRgbVar(style: CSSStyleDeclaration, name: string, value: string): void {
  style.setProperty(name, toRgbCss(value));
}
