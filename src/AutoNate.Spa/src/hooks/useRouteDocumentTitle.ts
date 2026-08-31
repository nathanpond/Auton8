import { useEffect } from "react";
import { matchPath, useLocation } from "react-router-dom";
import { APP_ROUTES, AppRoute } from "@/routes/appRoutes";

// Keeps document.title in step with the route.
//
// Before this, SiteAppearanceProvider set the title once to the site name and
// only four pages (all admin datastores) ever overrode it, so every tab,
// history entry and window-switcher row read "AutoNate". Screen-reader users
// orient by title on navigation, which made every destination
// indistinguishable — WCAG 2.4.2 (Page Titled) and 508 §502 (#18).
//
// Titles come from APP_ROUTES rather than from each page: one place to look,
// one line for a new route, and no page-level effect ordering to reason about.
// A page with a genuinely dynamic title (a record's name, say) can still call
// useDocumentTitle itself — it runs after this and wins.
export function useRouteDocumentTitle(siteName: string): void {
  const { pathname } = useLocation();

  useEffect(() => {
    const title = findRouteTitle(pathname);
    document.title = title ? `${title} · ${siteName}` : siteName;
  }, [pathname, siteName]);
}

function findRouteTitle(pathname: string): string | null {
  // Longest pattern first: "records/:typeShortCode/new" has to beat
  // "records/:typeShortCode", which matchPath would otherwise also satisfy.
  const candidates = flatten(APP_ROUTES)
    .filter((route): route is AppRoute & { path: string; title: string } =>
      Boolean(route.path) && Boolean(route.title))
    .sort((a, b) => b.path.split("/").length - a.path.split("/").length);

  for (const route of candidates) {
    if (matchPath({ path: `/${route.path}`, end: !route.path.endsWith("*") }, pathname)) {
      return route.title;
    }
  }
  return null;
}

function flatten(routes: AppRoute[]): AppRoute[] {
  return routes.flatMap((route) =>
    route.children ? [route, ...flatten(route.children)] : [route]);
}
