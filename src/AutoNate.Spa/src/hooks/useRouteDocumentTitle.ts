import { useEffect } from "react";
import { matchPath, useLocation } from "react-router-dom";
import { APP_ROUTES, AppRoute } from "@/routes/appRoutes";

// Keeps document.title in step with the route.
//
// Before this, SiteAppearanceProvider set the title once to the site name and
// only four pages (all admin datastores) ever overrode it, so every tab,
// history entry and window-switcher row read "Auton8". Screen-reader users
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
    const title = findRouteTitle(pathname) ?? titleFromPath(pathname);
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

// Most of the app is served by DynamicPageRoute from the page registry, and
// PageRegistryEntry carries only { id, path, contentType } — no title. Rather
// than leave every one of those reading the bare site name (the whole point of
// archived-18), derive a title from the last meaningful path segment.
//
// The menu item for the path would give a nicer, operator-authored label, but
// the menu is not loaded in the shell and threading it here would couple title
// rendering to menu fetch state. Deriving from the URL is deterministic and
// never blank; a page that wants better can call useDocumentTitle.
function titleFromPath(pathname: string): string | null {
  const segments = pathname.split("/").filter(Boolean);
  if (segments.length === 0) return null;

  // Trailing ids/guids/numbers say nothing to a reader — back up to the last
  // word-ish segment (…/records/42 reads better as "Records").
  const meaningful = [...segments].reverse().find((segment) => /[a-z]/i.test(segment) && !isIdLike(segment));
  if (!meaningful) return null;

  return meaningful
    .split("-")
    .filter(Boolean)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}

function isIdLike(segment: string): boolean {
  return /^[0-9]+$/.test(segment)
    || /^[0-9a-f]{8}-[0-9a-f]{4}-/i.test(segment);
}
