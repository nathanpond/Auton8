import { ComponentType, ReactNode } from "react";
import { z } from "zod";

// Per-mount info handed to a widget's runtime component. Title is the
// user-overridden display label; falls back to `definition.title`. WidgetId
// + dashboardId are exposed so widgets can call back into the dashboard
// hooks (e.g. record an interaction) without prop drilling.
export type WidgetRuntimeProps<TConfig> = {
  config: TConfig;
  title: string | null;
  widgetId: string;
  dashboardId: string;
};

// Bespoke config form (escape hatch from AutoConfigForm). Receives the
// current value + an onChange callback. Errors keyed by Zod path are
// pre-populated by the Drawer; bespoke forms can wire them into Mantine
// inputs as needed.
export type WidgetConfigFormProps<TConfig> = {
  value: TConfig;
  onChange: (next: TConfig) => void;
  errors: Record<string, string>;
};

export type WidgetSize = {
  w: number;
  h: number;
  minW?: number;
  minH?: number;
};

export type WidgetDefinition<TConfig = unknown> = {
  // Unique registry key. Stored in dashboard_widgets.widget_type.
  type: string;
  // Picker grouping. Free-form, sorted alphabetically by the picker.
  category: string;
  // Display name in the picker AND default rendered title.
  title: string;
  description: string;
  // Either a URL/dataURI string or a fully-rendered React node. Strings
  // become <img> in the picker; nodes are rendered as-is.
  thumbnail: string | ReactNode;
  defaultSize: WidgetSize;
  defaultConfig: TConfig;
  // Zod schema used to (a) validate user-supplied config before save and
  // (b) drive the AutoConfigForm renderer when ConfigForm is not provided.
  schema: z.ZodType<TConfig>;
  Component: ComponentType<WidgetRuntimeProps<TConfig>>;
  // Optional override — when provided, the drawer renders this instead of
  // AutoConfigForm. Use sparingly; the auto form should cover ~95% of
  // cases.
  ConfigForm?: ComponentType<WidgetConfigFormProps<TConfig>>;
  // Keep the widget out of the "Add widget" picker. Used for back-compat
  // entries that should still render existing dashboard rows but no longer
  // appear as an option for new widgets (e.g. an old combined "Chart"
  // entry replaced by per-chart-type entries).
  hiddenFromPicker?: boolean;
};

// Module-level registry. Widgets self-register at import time by calling
// registerWidget(); src/widgets/index.ts imports each widget folder for the
// side effect so the registry is populated before anyone queries it.
const REGISTRY = new Map<string, WidgetDefinition>();

export function registerWidget<TConfig>(def: WidgetDefinition<TConfig>): void {
  REGISTRY.set(def.type, def as unknown as WidgetDefinition);
}

export function getWidget(type: string): WidgetDefinition | undefined {
  return REGISTRY.get(type);
}

export function listWidgets(): WidgetDefinition[] {
  return Array.from(REGISTRY.values())
    .filter((w) => !w.hiddenFromPicker)
    .sort((a, b) => {
      const catCmp = a.category.localeCompare(b.category);
      return catCmp !== 0 ? catCmp : a.title.localeCompare(b.title);
    });
}

// Recursively layer `stored` over `defaults` so widgets whose persisted
// config predates a schema addition (e.g. an old chart widget saved before
// `dataSource` existed) don't crash the drawer / runtime with "Cannot read
// properties of undefined". Plain objects merge per-key; arrays and
// primitives are taken from `stored` as-is when present.
//
// Save-time validation re-runs through Zod, so any default that got merged
// in here is persisted back the next time the user clicks Save — silent
// migration, no DB-side backfill needed.
export function mergeWidgetConfig<T>(defaults: T, stored: unknown): T {
  if (stored === undefined || stored === null) return defaults;
  if (
    typeof defaults !== "object" ||
    defaults === null ||
    Array.isArray(defaults) ||
    typeof stored !== "object" ||
    Array.isArray(stored)
  ) {
    return stored as T;
  }
  const out: Record<string, unknown> = { ...(defaults as Record<string, unknown>) };
  const storedObj = stored as Record<string, unknown>;
  for (const key of Object.keys(storedObj)) {
    out[key] = mergeWidgetConfig(out[key], storedObj[key]);
  }
  return out as T;
}
