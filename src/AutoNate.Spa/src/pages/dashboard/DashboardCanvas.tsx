import { useMemo, useRef, useState } from "react";
import {
  ResponsiveGridLayout,
  useContainerWidth,
  type Breakpoint,
  type Layout,
  type LayoutItem
} from "react-grid-layout";
import "react-grid-layout/css/styles.css";
import "react-resizable/css/styles.css";
import type { DashboardWidget } from "@/api/dashboards";
import { WidgetHost } from "./WidgetHost";

const COLS = { lg: 12, md: 10, sm: 6, xs: 4, xxs: 2 } as const;
const BREAKPOINTS = { lg: 1200, md: 996, sm: 768, xs: 480, xxs: 0 } as const;
const ROW_HEIGHT = 60;
// Persisted layout is canonical lg (12 cols). Smaller breakpoints
// auto-repack for display only — editing is gated to lg so the user
// can't accidentally save a 4-col-wide squashed layout back as the
// canonical one. (Schema stores one set of x/y/w/h per widget, not one
// per breakpoint, so per-breakpoint editing would corrupt the lg view.)
const EDITABLE_BREAKPOINT: Breakpoint = "lg";

type Props = {
  dashboardId: string;
  widgets: DashboardWidget[];
  isEditable: boolean;
  onLayoutChange: (positions: { widgetId: string; gridX: number; gridY: number; gridW: number; gridH: number }[]) => void;
  onConfigureWidget: (widget: DashboardWidget) => void;
  onRemoveWidget: (widget: DashboardWidget) => void;
};

export function DashboardCanvas({
  dashboardId,
  widgets,
  isEditable,
  onLayoutChange,
  onConfigureWidget,
  onRemoveWidget
}: Props) {
  const layout = useMemo<LayoutItem[]>(
    () =>
      widgets.map((w) => ({
        i: w.id,
        x: w.gridX,
        y: w.gridY,
        w: w.gridW,
        h: w.gridH,
        minW: 2,
        minH: 2
      })),
    [widgets]
  );

  // Coalesce drag/resize end events so a fast user doesn't fire a save
  // for every intermediate position. `onDragStop` and `onResizeStop`
  // (unlike `onLayoutChange`) only fire from explicit user interaction,
  // so window resize can never reach this path.
  const lastSavedRef = useRef<string>(JSON.stringify(layout));
  const saveTimerRef = useRef<number | null>(null);

  // Track the active responsive breakpoint so drag/resize stay enabled
  // only at lg. Initial value matches RGL's default measurement on first
  // mount (≥1200 → lg); onBreakpointChange flips it when the user
  // resizes past a threshold.
  const [currentBreakpoint, setCurrentBreakpoint] = useState<Breakpoint>("lg");
  const canEdit = isEditable && currentBreakpoint === EDITABLE_BREAKPOINT;

  const { width, containerRef, mounted } = useContainerWidth({ measureBeforeMount: false });

  const persistLayout = (current: Layout) => {
    // Defence in depth: drag/resize handlers are already disabled
    // outside the editable breakpoint, so this guard should never
    // trigger — but if RGL ever fires the callback unexpectedly it
    // would silently corrupt the canonical lg layout. Skip it.
    if (!canEdit) return;
    const snapshot = JSON.stringify(
      current.map((l) => ({ i: l.i, x: l.x, y: l.y, w: l.w, h: l.h }))
    );
    if (snapshot === lastSavedRef.current) return;
    if (saveTimerRef.current) window.clearTimeout(saveTimerRef.current);
    saveTimerRef.current = window.setTimeout(() => {
      lastSavedRef.current = snapshot;
      onLayoutChange(
        current.map((l) => ({
          widgetId: l.i,
          gridX: l.x,
          gridY: l.y,
          gridW: l.w,
          gridH: l.h
        }))
      );
    }, 400);
  };

  return (
    <div className="dashboard-canvas" ref={containerRef}>
      {mounted ? (
        <ResponsiveGridLayout
          width={width}
          layouts={{ lg: layout, md: layout, sm: layout, xs: layout, xxs: layout }}
          cols={COLS}
          breakpoints={BREAKPOINTS}
          rowHeight={ROW_HEIGHT}
          dragConfig={{
            enabled: canEdit,
            handle: ".widget-drag-handle"
          }}
          resizeConfig={{ enabled: canEdit }}
          onBreakpointChange={(bp) => setCurrentBreakpoint(bp as Breakpoint)}
          onDragStop={persistLayout}
          onResizeStop={persistLayout}
          margin={[12, 12]}
        >
          {widgets.map((w) => (
            <div key={w.id}>
              <WidgetHost
                widget={w}
                dashboardId={dashboardId}
                isEditable={isEditable}
                onConfigure={() => onConfigureWidget(w)}
                onRemove={() => onRemoveWidget(w)}
              />
            </div>
          ))}
        </ResponsiveGridLayout>
      ) : null}
    </div>
  );
}
