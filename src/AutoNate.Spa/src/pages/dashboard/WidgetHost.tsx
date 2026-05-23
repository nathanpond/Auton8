import { Component, ErrorInfo, ReactNode, useMemo } from "react";
import { ActionIcon, Alert, Group, Tooltip } from "@mantine/core";
import type { DashboardWidget } from "@/api/dashboards";
import { getWidget, mergeWidgetConfig } from "@/widgets";

type Props = {
  widget: DashboardWidget;
  dashboardId: string;
  isEditable: boolean;
  onConfigure: () => void;
  onRemove: () => void;
};

// Frame around a registry-resolved widget. Owns the drag handle, the gear /
// trash buttons, and an ErrorBoundary so a bad widget config can't crash the
// whole dashboard.
export function WidgetHost({ widget, dashboardId, isEditable, onConfigure, onRemove }: Props) {
  const definition = getWidget(widget.widgetType);
  const title = widget.title ?? definition?.title ?? widget.widgetType;
  // Merge persisted config over the widget's defaults so configs predating
  // a schema addition still render (and don't blow up on `value.newField.x`).
  // The merge runs once per change to either side; the resulting object is
  // referentially stable across re-renders that don't touch the inputs.
  const mergedConfig = useMemo(
    () =>
      definition
        ? mergeWidgetConfig(definition.defaultConfig, widget.config)
        : widget.config,
    [definition, widget.config]
  );

  return (
    <div className="widget-frame">
      <div className="widget-frame-header">
        <div className="widget-drag-handle" title={isEditable ? "Drag to move" : undefined}>
          {title}
        </div>
        {isEditable ? (
          <Group gap={4} className="widget-frame-actions">
            <Tooltip label="Configure widget">
              <ActionIcon
                variant="subtle"
                size="sm"
                aria-label="Configure widget"
                onClick={onConfigure}
              >
                <i className="fa fa-gear" />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Remove widget">
              <ActionIcon
                variant="subtle"
                size="sm"
                color="red"
                aria-label="Remove widget"
                onClick={onRemove}
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </Tooltip>
          </Group>
        ) : null}
      </div>
      <div className="widget-frame-body">
        {definition ? (
          <WidgetErrorBoundary widgetType={widget.widgetType}>
            <definition.Component
              config={mergedConfig as never}
              title={widget.title}
              widgetId={widget.id}
              dashboardId={dashboardId}
            />
          </WidgetErrorBoundary>
        ) : (
          <Alert color="yellow" variant="light" m="sm">
            Unknown widget type: <code>{widget.widgetType}</code>
          </Alert>
        )}
      </div>
    </div>
  );
}

type ErrorBoundaryProps = { widgetType: string; children: ReactNode };
type ErrorBoundaryState = { error: Error | null };

class WidgetErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error(`Widget '${this.props.widgetType}' crashed`, error, info);
  }

  render() {
    if (this.state.error) {
      return (
        <Alert color="red" variant="light" m="sm" title="Widget crashed">
          {this.state.error.message}
        </Alert>
      );
    }
    return this.props.children;
  }
}
