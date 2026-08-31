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
import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Checkbox,
  Code,
  Divider,
  Grid,
  Group,
  Loader,
  NumberInput,
  PasswordInput,
  Radio,
  Select,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
} from "@mantine/core";
import { api } from "@/api/client";
import { DataTable } from "@/components/data-table/DataTable";

// Bag of Mantine primitives + project wrappers forwarded into the JSX runtime
// so admin-authored pages can use the design system directly (e.g. <Button>,
// <DataTable>, <Switch>) instead of falling back to raw HTML with no styles.
// Add new components here as plugin authors need them — bundle cost is paid
// once, since this module is only imported when a JSX page actually renders.
const mantineBindings = {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Checkbox,
  Code,
  DataTable,
  Divider,
  Grid,
  Group,
  Loader,
  NumberInput,
  PasswordInput,
  Radio,
  Select,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip
};
const MANTINE_BINDING_NAMES = Object.keys(mantineBindings) as (keyof typeof mantineBindings)[];
const MANTINE_DESTRUCTURE = `const { ${MANTINE_BINDING_NAMES.join(", ")} } = Mantine;`;

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
        <Alert color="red" variant="light">
          <strong>Page runtime error:</strong> {this.state.error.message}
        </Alert>
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
        "Mantine",
        `${MANTINE_DESTRUCTURE}\n${code}\n;return typeof Page === "function" ? Page : null;`
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
        logout,
        mantineBindings
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- navigate/api and the hooks are stable; only source and the lazy sucrase module should retrigger compilation
  }, [sucrase, source]);

  if (loadError) {
    return (
      <Alert color="red" variant="light">
        <strong>Failed to load page renderer:</strong> {loadError}
      </Alert>
    );
  }
  if (!compiled) {
    return (
      <Text c="dimmed">
        <i className="fa fa-spinner fa-spin" style={{ marginRight: 8 }} />
        Loading page…
      </Text>
    );
  }
  if (compiled.kind === "error") {
    return (
      <Alert color="red" variant="light">
        <strong>Page compile error:</strong> {compiled.message}
      </Alert>
    );
  }

  const Page = compiled.Page;
  return (
    <RuntimeBoundary key={source}>
      <Page {...(props ?? {})} />
    </RuntimeBoundary>
  );
}
