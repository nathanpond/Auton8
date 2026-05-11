import { ReactNode } from "react";
import { MantineProvider, createTheme } from "@mantine/core";
import { generateColors } from "@mantine/colors-generator";
import { DEFAULT_SITE_APPEARANCE } from "@/lib/siteAppearance";

// Module-level static theme. Live brand-color changes flow through CSS
// variables (`--mantine-color-brand-0..9`) written by
// `applySiteAppearanceToDocument`, NOT through this theme object — recreating
// the theme on every keystroke caused MantineProvider to re-render the whole
// tree mid-interaction and infinite-looped the ColorInput popover's
// ScrollArea ref callback.
const STATIC_THEME = createTheme({
  primaryColor: "brand",
  colors: {
    brand: generateColors(DEFAULT_SITE_APPEARANCE.primaryAccentColor)
  }
});

export function MantineRoot({ children }: { children: ReactNode }) {
  return <MantineProvider theme={STATIC_THEME}>{children}</MantineProvider>;
}
