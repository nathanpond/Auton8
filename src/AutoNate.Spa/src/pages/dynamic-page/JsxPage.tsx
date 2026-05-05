import React, {
  Component,
  ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import { Link, NavLink, useNavigate } from "react-router-dom";
import { api } from "@/api/client";

type Sucrase = typeof import("sucrase");

// Sucrase is ~150 KB, so we only fetch it for users who actually visit a
// JSX-content page. The promise is module-level so concurrent JSX pages share
// one download.
let sucrasePromise: Promise<Sucrase> | null = null;
const loadSucrase = (): Promise<Sucrase> => {
  if (!sucrasePromise) sucrasePromise = import("sucrase");
  return sucrasePromise;
};

const logout = () => {
  // Mirror the existing logout menu action: form POST to the cookie-auth
  // endpoint (not the JSON /api/auth/logout used elsewhere).
  const form = document.createElement("form");
  form.method = "post";
  form.action = "/account/logout";
  document.body.appendChild(form);
  form.submit();
};

class RuntimeBoundary extends Component<
  { children: ReactNode },
  { error: Error | null }
> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: Error) {
    return { error };
  }
  componentDidCatch(error: Error) {
    console.error("[JsxPage] runtime error", error);
  }
  render() {
    if (this.state.error) {
      return (
        <div className="alert alert-danger">
          <strong>Page runtime error:</strong> {this.state.error.message}
        </div>
      );
    }
    return this.props.children;
  }
}

type Compiled =
  | { kind: "ok"; Page: React.ComponentType<Record<string, unknown>> }
  | { kind: "error"; message: string };

export type JsxPageProps = {
  source: string;
  // Optional bag of values forwarded to the authored `Page({ ... })`
  // component. Existing menu-page consumers don't pass anything; the Forms
  // runtime uses this to thread `data`, `onChange`, `onSubmit`, `mode`,
  // and `context` through. Unknown keys are passed verbatim.
  props?: Record<string, unknown>;
};

export function JsxPage({ source, props }: JsxPageProps) {
  const [sucrase, setSucrase] = useState<Sucrase | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    let cancelled = false;
    loadSucrase()
      .then((mod) => {
        if (!cancelled) setSucrase(mod);
      })
      .catch((err) => {
        if (!cancelled) {
          setLoadError(err instanceof Error ? err.message : String(err));
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const compiled = useMemo<Compiled | null>(() => {
    if (!sucrase) return null;
    try {
      const { code } = sucrase.transform(source, {
        transforms: ["jsx", "typescript"],
        production: true,
        jsxRuntime: "classic"
      });
      const factory = new Function(
        "React",
        "useState",
        "useEffect",
        "useMemo",
        "useCallback",
        "useRef",
        "navigate",
        "Link",
        "NavLink",
        "api",
        "logout",
        `${code}\n;return typeof Page === "function" ? Page : null;`
      );
      const Page = factory(
        React,
        useState,
        useEffect,
        useMemo,
        useCallback,
        useRef,
        navigate,
        Link,
        NavLink,
        api,
        logout
      );
      if (typeof Page !== "function") {
        return {
          kind: "error",
          message: "Define a `function Page() { … }` that returns JSX."
        };
      }
      return {
        kind: "ok",
        Page: Page as React.ComponentType<Record<string, unknown>>
      };
    } catch (err) {
      return {
        kind: "error",
        message: err instanceof Error ? err.message : String(err)
      };
    }
    // `navigate`, `api`, and the React hooks are stable references; only the
    // source and the lazy-loaded sucrase module need to retrigger compilation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sucrase, source]);

  if (loadError) {
    return (
      <div className="alert alert-danger">
        <strong>Failed to load page renderer:</strong> {loadError}
      </div>
    );
  }
  if (!compiled) {
    return (
      <div className="text-muted">
        <i className="fa fa-spinner fa-spin me-2" />
        Loading page…
      </div>
    );
  }
  if (compiled.kind === "error") {
    return (
      <div className="alert alert-danger">
        <strong>Page compile error:</strong> {compiled.message}
      </div>
    );
  }

  const Page = compiled.Page;
  return (
    <RuntimeBoundary key={source}>
      <Page {...(props ?? {})} />
    </RuntimeBoundary>
  );
}
