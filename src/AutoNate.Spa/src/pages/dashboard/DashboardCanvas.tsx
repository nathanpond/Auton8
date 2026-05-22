import { useMemo, useRef } from "react";
import {
  ResponsiveGridLayout,
  useContainerWidth,
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

  // Coalesce drag/resize ends so a fast user doesn't fire a save for every
  // intermediate layout change. RGL also fires onLayoutChange during init
  // with the unchanged layout — the deep-equal guard suppresses that.
  const lastSavedRef = useRef<string>(JSON.stringify(layout));
  const saveTimerRef = useRef<number | null>(null);

  const { width, containerRef, mounted } = useContainerWidth({ measureBeforeMount: false });

  const handleLayoutChange = (current: Layout) => {
    const snapshot = JSON.stringify(
      current.map((l) => ({ i: l.i, x: l.x, y: l.y, w: l.w, h: l.h }))
    );
    if (snapshot === lastSavedRef.current) return;
    if (!isEditable) return;
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
            enabled: isEditable,
            handle: ".widget-drag-handle"
          }}
          resizeConfig={{ enabled: isEditable }}
          onLayoutChange={handleLayoutChange}
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
