import { Routes, useLocation } from "react-router-dom";
import JsxParser from "react-jsx-parser";
import { usePage, usePages } from "@/hooks/usePages";
import NotFound from "@/pages/not-found/NotFound";
import { renderAppRoutes } from "@/routes/appRoutes";
import {
  JSX_BLACKLISTED_ATTRS,
  JSX_BLACKLISTED_TAGS,
  JSX_COMPONENTS
} from "./jsxWhitelist";

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

  return (
    <div className="dynamic-page p-4">
      {page.contentType === "jsx" ? (
        <JsxParser
          jsx={page.content}
          // react-jsx-parser ships an outdated @types/react that conflicts
          // with React 19's types; the runtime behavior is fine, so cast.
          components={JSX_COMPONENTS as Record<string, never>}
          blacklistedAttrs={JSX_BLACKLISTED_ATTRS}
          blacklistedTags={JSX_BLACKLISTED_TAGS}
          renderError={({ error }) => (
            <div className="alert alert-danger">
              <strong>Page render error:</strong> {error}
            </div>
          )}
        />
      ) : (
        <div dangerouslySetInnerHTML={{ __html: page.content }} />
      )}
    </div>
  );
}
