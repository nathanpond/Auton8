import { useEffect } from "react";

// Sets document.title for the current page and restores the previous value
// on unmount. Pages call this once per render with the title they want
// (typically a static string).
//
// Why: SiteAppearanceProvider sets the title once to the site name; without
// a per-page hook every browser tab / history entry is just "AutoNate",
// invisible to screen-reader users navigating between pages (WCAG 2.4.2).
// Sites can pass `appendSiteName: true` to get the conventional
// "Page · Site" pattern.
export function useDocumentTitle(title: string | null | undefined, opts?: { appendSiteName?: string }): void {
  useEffect(() => {
    if (!title) return;
    const previous = document.title;
    const full = opts?.appendSiteName ? `${title} · ${opts.appendSiteName}` : title;
    document.title = full;
    return () => {
      document.title = previous;
    };
  }, [title, opts?.appendSiteName]);
}
