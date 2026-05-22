import { createContext, ReactNode, useContext, useMemo } from "react";

// Per-mount JSON config a templated menu item carries (everything in
// menu_items.config minus templateKey/path). Templates that need per-mount
// configuration (dashboards, future widgets) read it via
// useTemplateConfig<T>(). Unmounted templates get null.
export type TemplateMountConfig = Record<string, unknown> | null;

const TemplateConfigContext = createContext<TemplateMountConfig>(null);

export function TemplateConfigProvider({
  value,
  children
}: {
  value: TemplateMountConfig;
  children: ReactNode;
}) {
  const memoized = useMemo<TemplateMountConfig>(() => value ?? null, [value]);
  return (
    <TemplateConfigContext.Provider value={memoized}>
      {children}
    </TemplateConfigContext.Provider>
  );
}

// Read a slice of the per-mount config with a fallback. Templates that don't
// declare config (or callers rendered outside a mount) get the default.
export function useTemplateConfig<T = TemplateMountConfig>(
  defaultValue: T
): T {
  const ctx = useContext(TemplateConfigContext);
  if (ctx === null) return defaultValue;
  return ctx as unknown as T;
}
