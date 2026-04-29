import { useEffect, useRef } from "react";
import { Routes, useLocation } from "react-router-dom";
import { usePage, usePages } from "@/hooks/usePages";
import NotFound from "@/pages/not-found/NotFound";
import { renderAppRoutes } from "@/routes/appRoutes";
import { PAGE_TEMPLATES } from "@/pageTemplates";
import { JsxPage } from "./JsxPage";

export default function DynamicPageRoute() {
  const location = useLocation();
  const path = location.pathname;
  const { data: registry, isLoading: registryLoading } = usePages();

  const matched = registry?.find((entry) => entry.path === path) ?? null;
  const { data: page, isLoading: pageLoading } = usePage(matched ? path : null);

  if (registryLoading || (matched && pageLoading)) {
    return (
      <div className="p-4">
        <div className="text-muted">
          <i className="fa fa-spinner fa-spin me-2" />
          Loading…
        </div>
      </div>
    );
  }

  if (!matched || !page) {
    return <NotFound />;
  }

  // Alias-route: render the target route's component at the alias URL by
  // matching `content` (the target path) against the static APP_ROUTES while
  // the URL bar stays as the alias.
  if (page.contentType === "alias") {
    if (!page.content) {
      return (
        <div className="p-4">
          <div className="alert alert-warning">
            Alias menu item has no target path configured.
          </div>
        </div>
      );
    }
    return <Routes location={page.content}>{renderAppRoutes()}</Routes>;
  }

  // Template: look up the built-in component by its key. The registry contains
  // every template the SPA ships; missing keys fall through to NotFound.
  if (page.contentType === "template") {
    const element = PAGE_TEMPLATES[page.content];
    return element ?? <NotFound />;
  }

  return (
    <div className="dynamic-page p-4">
      {page.contentType === "jsx" ? (
        <JsxPage source={page.content} />
      ) : (
        <HtmlPage html={page.content} />
      )}
    </div>
  );
}

// Renders admin-authored HTML and re-creates any <script> tags so they
// actually execute. Browser spec: scripts inserted via innerHTML (which
// dangerouslySetInnerHTML uses) silently no-op. Scripts created via
// document.createElement and appended *do* run, so after the markup is in
// the DOM we walk it once and swap each <script> for an equivalent one we
// build ourselves.
function HtmlPage({ html }: { html: string }) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const root = ref.current;
    if (!root) return;
    const originals = Array.from(root.querySelectorAll("script"));
    for (const original of originals) {
      const replacement = document.createElement("script");
      for (const attr of Array.from(original.attributes)) {
        replacement.setAttribute(attr.name, attr.value);
      }
      // createElement-built scripts default to async=true, which breaks
      // ordered dependencies (load A then run B). Preserve original document
      // order unless the author explicitly opted into async.
      if (!original.hasAttribute("async")) replacement.async = false;
      if (original.textContent) replacement.textContent = original.textContent;
      original.parentNode?.replaceChild(replacement, original);
    }
  }, [html]);

  return <div ref={ref} dangerouslySetInnerHTML={{ __html: html }} />;
}
