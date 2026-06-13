import { useCallback, useMemo, useRef } from "react";
import { useRegisterPageContext } from "@/agent/pageContext/PageContextRegistry";
import type {
  PageActionDefinition,
  PageActionRequest,
  PageActionResult,
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "@/agent/pageContext/types";
import type { Dashboard, DashboardWidget, DashboardWithWidgets } from "@/api/dashboards";
import { getWidget, listWidgets } from "@/widgets";

const PAGE_KEY = "dashboard";
const SCHEMA_VERSION = 1;

// Soft cap for the snapshot data section. Each widget's config can be
// several KB; with 10–15 widgets we'd brush the 64KB cap. Once the
// rough budget is blown we drop `config` on non-selected widgets and set
// safetyHints.truncated=true so the model knows to call query_page.
const SNAPSHOT_BUDGET_BYTES = 50_000;

type Options = {
  dashboards: readonly Dashboard[];
  selectedId: string | null;
  dashboard: DashboardWithWidgets | null;
  widgets: readonly DashboardWidget[];
  configWidgetId: string | null;
  pickerOpen: boolean;
  renameOpen: boolean;
  deleteOpen: boolean;
  selectDashboard: (id: string) => void;
  openPicker: () => void;
  closePicker: () => void;
  openRename: () => void;
  openDelete: () => void;
  openWidgetConfig: (widgetId: string) => void;
  closeWidgetConfig: () => void;
  removeWidget: (widgetId: string) => void;
  repositionWidget: (
    widgetId: string,
    grid: { gridX: number; gridY: number; gridW: number; gridH: number }
  ) => void;
  addWidgetOfType: (widgetType: string) => void;
};

const ACTIONS: PageActionDefinition[] = [
  {
    name: "select_dashboard",
    description:
      "Switch the dashboard the canvas is showing. args: { id: string }. Refuses when the id isn't in the visible list."
  },
  {
    name: "open_widget_picker",
    description: "Open the 'Add widget' picker. No args."
  },
  {
    name: "close_widget_picker",
    description: "Close the 'Add widget' picker. No args."
  },
  {
    name: "add_widget_of_type",
    description:
      "Add a widget of the given registry type to the bottom of the current dashboard. args: { widgetType: string }. " +
      "Equivalent to clicking the picker. The new widget opens in the config drawer immediately."
  },
  {
    name: "open_widget_config",
    description:
      "Open the per-widget config drawer for an existing widget. args: { widgetId: string }. " +
      "Refuses when the widget isn't on the current dashboard."
  },
  {
    name: "close_widget_config",
    description: "Close the per-widget config drawer without saving. No args."
  },
  {
    name: "remove_widget",
    description:
      "Open the 'Remove widget?' confirmation modal. args: { widgetId: string }. " +
      "Does not directly delete — the user still confirms in the modal. Use this when the chatbot wants to drive a removal."
  },
  {
    name: "reposition_widget",
    description:
      "Move/resize a single widget. args: { widgetId: string, gridX: number, gridY: number, gridW: number, gridH: number }. " +
      "Triggers the same persistence path as a drag from the canvas."
  },
  {
    name: "open_rename_dashboard",
    description: "Open the rename-dashboard modal. No args."
  },
  {
    name: "open_delete_dashboard",
    description: "Open the delete-dashboard confirmation. No args."
  }
];

export function useDashboardPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;

  const getSnapshot = useCallback((): PageSnapshot | null => {
    const o = optsRef.current;
    if (!o.dashboard) {
      // Selector visible, no dashboard loaded yet — emit a minimal snapshot.
      return {
        pageKey: PAGE_KEY,
        schemaVersion: SCHEMA_VERSION,
        summary: `Dashboard page · ${o.dashboards.length} dashboards · none selected`,
        version: o.dashboards.length,
        data: {
          dashboards: o.dashboards.map((d) => ({
            id: d.id,
            name: d.name,
            isArchived: d.isArchived,
            updatedAtUtc: d.updatedAtUtc
          })),
          selectedId: o.selectedId,
          dashboard: null,
          widgets: [],
          widgetCatalog: catalog(),
          modals: {
            pickerOpen: o.pickerOpen,
            renameOpen: o.renameOpen,
            deleteOpen: o.deleteOpen,
            configWidgetId: o.configWidgetId
          },
          safetyHints: { truncated: false, truncatedFields: [] as string[] }
        }
      };
    }

    // Pass 1: include every widget with full config. If we blow the soft
    // budget, drop config on non-selected widgets.
    const widgetsFull = o.widgets.map((w) => ({
      id: w.id,
      widgetType: w.widgetType,
      title: w.title,
      gridX: w.gridX,
      gridY: w.gridY,
      gridW: w.gridW,
      gridH: w.gridH,
      sortOrder: w.sortOrder,
      config: w.config as Record<string, unknown>
    }));

    let degraded = false;
    let widgets: typeof widgetsFull = widgetsFull;
    const fullSize = JSON.stringify(widgetsFull).length;
    if (fullSize > SNAPSHOT_BUDGET_BYTES) {
      degraded = true;
      widgets = widgetsFull.map((w) =>
        w.id === o.configWidgetId
          ? w
          : { ...w, config: { __redacted: "use query_page topic=widget.config" } as Record<string, unknown> }
      );
    }

    const summary = [
      `Dashboard "${o.dashboard.dashboard.name}"`,
      `${o.widgets.length} widget${o.widgets.length === 1 ? "" : "s"}`,
      o.configWidgetId ? `editing widget ${o.configWidgetId}` : null,
      o.pickerOpen ? "picker open" : null,
      degraded ? "(widget configs truncated)" : null
    ]
      .filter(Boolean)
      .join(" · ");

    return {
      pageKey: PAGE_KEY,
      schemaVersion: SCHEMA_VERSION,
      summary,
      version: o.widgets.length + (o.configWidgetId ? 1 : 0) + (o.pickerOpen ? 1 : 0),
      data: {
        dashboards: o.dashboards.map((d) => ({
          id: d.id,
          name: d.name,
          isArchived: d.isArchived,
          updatedAtUtc: d.updatedAtUtc
        })),
        selectedId: o.selectedId,
        dashboard: {
          id: o.dashboard.dashboard.id,
          name: o.dashboard.dashboard.name,
          description: o.dashboard.dashboard.description,
          visibility: o.dashboard.dashboard.visibility,
          scope: o.dashboard.dashboard.scope,
          source: o.dashboard.dashboard.source,
          templateKey: o.dashboard.dashboard.templateKey,
          settings: o.dashboard.dashboard.settings,
          isArchived: o.dashboard.dashboard.isArchived
        },
        widgets,
        widgetCatalog: catalog(),
        modals: {
          pickerOpen: o.pickerOpen,
          renameOpen: o.renameOpen,
          deleteOpen: o.deleteOpen,
          configWidgetId: o.configWidgetId
        },
        safetyHints: {
          truncated: degraded,
          truncatedFields: degraded ? ["widgets[].config (non-selected)"] : []
        }
      }
    };
  }, []);

  const onPageQuery = useCallback(async (req: PageQueryRequest): Promise<PageQueryResult> => {
    const o = optsRef.current;
    switch (req.topic) {
      case "widget.config": {
        const widgetId = (req.args as { widgetId?: string } | undefined)?.widgetId;
        if (!widgetId) return { ok: false, error: "bad_args", message: "widget.config requires { widgetId: string }." };
        const w = o.widgets.find((x) => x.id === widgetId);
        if (!w) return { ok: false, error: "not_found", message: `Widget ${widgetId} not on current dashboard.` };
        return { ok: true, data: { id: w.id, widgetType: w.widgetType, title: w.title, config: w.config } };
      }
      case "widget_type.schema": {
        const widgetType = (req.args as { widgetType?: string } | undefined)?.widgetType;
        if (!widgetType) return { ok: false, error: "bad_args", message: "widget_type.schema requires { widgetType: string }." };
        const def = getWidget(widgetType);
        if (!def) return { ok: false, error: "not_found", message: `Unknown widget type '${widgetType}'.` };
        return {
          ok: true,
          data: {
            type: def.type,
            category: def.category,
            title: def.title,
            description: def.description,
            defaultSize: def.defaultSize,
            defaultConfig: def.defaultConfig
          }
        };
      }
      default:
        return { ok: false, error: "unknown_topic", message: `DashboardPage does not handle '${req.topic}'.` };
    }
  }, []);

  const onPageAction = useCallback(async (req: PageActionRequest): Promise<PageActionResult> => {
    const o = optsRef.current;
    const args = (req.args ?? {}) as Record<string, unknown>;
    switch (req.action) {
      case "select_dashboard": {
        const id = typeof args.id === "string" ? args.id : null;
        if (!id) return { ok: false, error: "bad_args", message: "select_dashboard requires { id: string }." };
        if (!o.dashboards.some((d) => d.id === id))
          return { ok: false, error: "not_found", message: `Dashboard ${id} not in list.` };
        o.selectDashboard(id);
        return { ok: true, summary: `Selected dashboard ${id}.` };
      }
      case "open_widget_picker":
        o.openPicker();
        return { ok: true, summary: "Opened widget picker." };
      case "close_widget_picker":
        o.closePicker();
        return { ok: true, summary: "Closed widget picker." };
      case "add_widget_of_type": {
        const widgetType = typeof args.widgetType === "string" ? args.widgetType : null;
        if (!widgetType) return { ok: false, error: "bad_args", message: "add_widget_of_type requires { widgetType: string }." };
        const def = getWidget(widgetType);
        if (!def) return { ok: false, error: "not_found", message: `Unknown widget type '${widgetType}'.` };
        o.addWidgetOfType(widgetType);
        return { ok: true, summary: `Added a ${def.title} widget.` };
      }
      case "open_widget_config": {
        const widgetId = typeof args.widgetId === "string" ? args.widgetId : null;
        if (!widgetId) return { ok: false, error: "bad_args", message: "open_widget_config requires { widgetId: string }." };
        if (!o.widgets.some((w) => w.id === widgetId))
          return { ok: false, error: "not_found", message: `Widget ${widgetId} not on current dashboard.` };
        o.openWidgetConfig(widgetId);
        return { ok: true, summary: `Opened config drawer for widget ${widgetId}.` };
      }
      case "close_widget_config":
        o.closeWidgetConfig();
        return { ok: true, summary: "Closed widget config drawer." };
      case "remove_widget": {
        const widgetId = typeof args.widgetId === "string" ? args.widgetId : null;
        if (!widgetId) return { ok: false, error: "bad_args", message: "remove_widget requires { widgetId: string }." };
        if (!o.widgets.some((w) => w.id === widgetId))
          return { ok: false, error: "not_found", message: `Widget ${widgetId} not on current dashboard.` };
        o.removeWidget(widgetId);
        return { ok: true, summary: `Opened remove confirmation for ${widgetId}.` };
      }
      case "reposition_widget": {
        const widgetId = typeof args.widgetId === "string" ? args.widgetId : null;
        if (!widgetId) return { ok: false, error: "bad_args", message: "reposition_widget requires { widgetId, gridX, gridY, gridW, gridH }." };
        if (!o.widgets.some((w) => w.id === widgetId))
          return { ok: false, error: "not_found", message: `Widget ${widgetId} not on current dashboard.` };
        const grid = {
          gridX: typeof args.gridX === "number" ? args.gridX : NaN,
          gridY: typeof args.gridY === "number" ? args.gridY : NaN,
          gridW: typeof args.gridW === "number" ? args.gridW : NaN,
          gridH: typeof args.gridH === "number" ? args.gridH : NaN
        };
        if (Number.isNaN(grid.gridX) || Number.isNaN(grid.gridY) || Number.isNaN(grid.gridW) || Number.isNaN(grid.gridH))
          return { ok: false, error: "bad_args", message: "gridX/gridY/gridW/gridH are required and must be numbers." };
        o.repositionWidget(widgetId, grid);
        return { ok: true, summary: `Repositioned ${widgetId}.` };
      }
      case "open_rename_dashboard":
        o.openRename();
        return { ok: true, summary: "Opened rename dashboard modal." };
      case "open_delete_dashboard":
        o.openDelete();
        return { ok: true, summary: "Opened delete dashboard confirmation." };
      default:
        return { ok: false, error: "unknown_action", message: `DashboardPage does not implement '${req.action}'.` };
    }
  }, []);

  const entry = useMemo<PageContextProviderEntry>(
    () => ({
      pageKey: PAGE_KEY,
      getSnapshot,
      onPageQuery,
      actions: ACTIONS,
      onPageAction
    }),
    [getSnapshot, onPageQuery, onPageAction]
  );

  useRegisterPageContext(entry);
}

// Snapshot of the SPA widget registry. Computed once per snapshot; the
// registry is module-static so this is cheap.
function catalog() {
  return listWidgets().map((w) => ({
    type: w.type,
    category: w.category,
    title: w.title,
    description: w.description,
    defaultSize: w.defaultSize
  }));
}
