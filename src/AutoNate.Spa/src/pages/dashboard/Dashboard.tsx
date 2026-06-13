import { useEffect, useMemo, useRef, useState } from "react";
import { Alert, Box, Button, Center, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { useLocation } from "react-router-dom";
import { useTemplateConfig } from "@/pages/dynamic-page/TemplateConfigContext";
import ConfirmModal from "@/components/ConfirmModal";
import { DashboardCanvas } from "./DashboardCanvas";
import { DashboardSelector } from "./DashboardSelector";
import { DeleteDashboardConfirm } from "./DeleteDashboardConfirm";
import { RenameDashboardModal } from "./RenameDashboardModal";
import { WidgetConfigDrawer } from "./WidgetConfigDrawer";
import { WidgetPickerModal } from "./WidgetPickerModal";
import {
  useAddWidget,
  useCreateDashboard,
  useDashboard,
  useDashboards,
  useDeleteDashboard,
  useRemoveWidget,
  useReplaceLayout,
  useUpdateDashboard,
  useUpdateWidget
} from "@/hooks/useDashboards";
import type { DashboardWidget } from "@/api/dashboards";
import { getWidget, type WidgetDefinition } from "@/widgets";
import { useDashboardPagePageContext } from "./useDashboardPagePageContext";
import "./dashboardStyles.css";

// Per-mount config schema. `isUserConfigurable=false` flips the page into
// fully-locked mode: no selector, no toolbar, no per-widget gear — just
// the admin-supplied defaultLayout.widgets read-only.
type DashboardTemplateConfig = {
  isUserConfigurable?: boolean;
  defaultLayout?: {
    widgets?: Array<{
      widgetType: string;
      title?: string | null;
      config?: Record<string, unknown>;
      gridX?: number;
      gridY?: number;
      gridW?: number;
      gridH?: number;
    }>;
  };
};

export default function Dashboard() {
  const mountConfig = useTemplateConfig<DashboardTemplateConfig | null>(null);
  const isUserConfigurable = mountConfig?.isUserConfigurable !== false;
  const location = useLocation();

  if (!isUserConfigurable) {
    return <LockedDashboard config={mountConfig ?? {}} />;
  }
  return <ConfigurableDashboard mountPath={location.pathname} />;
}

// ---- Locked mode --------------------------------------------------------

function LockedDashboard({ config }: { config: DashboardTemplateConfig }) {
  const widgets: DashboardWidget[] = useMemo(() => {
    const list = config.defaultLayout?.widgets ?? [];
    return list.map((w, i) => ({
      id: `locked-${i}`,
      dashboardId: "locked",
      widgetType: w.widgetType,
      title: w.title ?? null,
      config: w.config ?? {},
      gridX: w.gridX ?? 0,
      gridY: w.gridY ?? i * 3,
      gridW: w.gridW ?? 6,
      gridH: w.gridH ?? 3,
      sortOrder: i,
      createdAtUtc: "",
      updatedAtUtc: ""
    }));
  }, [config]);

  return (
    <Stack className="dashboard-page" p="md">
      {widgets.length === 0 ? (
        <Alert color="yellow" variant="light">
          This dashboard has no default widgets configured.
        </Alert>
      ) : (
        <DashboardCanvas
          dashboardId="locked"
          widgets={widgets}
          isEditable={false}
          onLayoutChange={() => undefined}
          onConfigureWidget={() => undefined}
          onRemoveWidget={() => undefined}
        />
      )}
    </Stack>
  );
}

// ---- Editable mode ------------------------------------------------------

function ConfigurableDashboard({ mountPath }: { mountPath: string }) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [configWidget, setConfigWidget] = useState<DashboardWidget | null>(null);
  const [renameOpen, setRenameOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [widgetToRemove, setWidgetToRemove] = useState<DashboardWidget | null>(null);

  const dashboardsQuery = useDashboards();
  const createDashboard = useCreateDashboard();

  // Auto-pick the first dashboard once the list loads. If the list is empty
  // and we haven't already kicked off a create, create a "My Dashboard" so
  // the user sees something instead of an empty selector. The ref gates the
  // create so list refetches don't spawn duplicates.
  const autoCreatedRef = useRef(false);
  useEffect(() => {
    if (!dashboardsQuery.data) return;
    if (selectedId && dashboardsQuery.data.find((d) => d.id === selectedId)) return;
    if (dashboardsQuery.data.length > 0) {
      setSelectedId(dashboardsQuery.data[0].id);
      return;
    }
    if (autoCreatedRef.current || createDashboard.isPending) return;
    autoCreatedRef.current = true;
    createDashboard.mutate(
      { name: "My Dashboard", fromMountPath: mountPath },
      {
        onSuccess: (created) => setSelectedId(created.id)
      }
    );
  }, [dashboardsQuery.data, selectedId, createDashboard, mountPath]);

  const dashboardQuery = useDashboard(selectedId);
  const updateDashboard = useUpdateDashboard(selectedId ?? "");
  const deleteDashboard = useDeleteDashboard();
  const addWidget = useAddWidget(selectedId ?? "");
  const updateWidget = useUpdateWidget(selectedId ?? "");
  const removeWidget = useRemoveWidget(selectedId ?? "");
  const replaceLayout = useReplaceLayout(selectedId ?? "");

  if (dashboardsQuery.isLoading) {
    return (
      <Center p="xl">
        <Loader />
      </Center>
    );
  }
  if (dashboardsQuery.isError) {
    return (
      <Box p="md">
        <Alert color="red">Failed to load dashboards.</Alert>
      </Box>
    );
  }

  const dashboards = dashboardsQuery.data ?? [];
  const dashboard = dashboardQuery.data?.dashboard ?? null;
  const widgets = dashboardQuery.data?.widgets ?? [];

  const handleAddWidget = (definition: WidgetDefinition) => {
    setPickerOpen(false);
    if (!selectedId) return;
    // Drop new widgets at the bottom-left so they don't overlap existing
    // ones. RGL will reflow if the spot is taken.
    const maxY = widgets.length > 0
      ? Math.max(...widgets.map((w) => w.gridY + w.gridH))
      : 0;
    addWidget.mutate(
      {
        widgetType: definition.type,
        title: null,
        config: definition.defaultConfig as Record<string, unknown>,
        gridX: 0,
        gridY: maxY,
        gridW: definition.defaultSize.w,
        gridH: definition.defaultSize.h
      },
      {
        onSuccess: (created) => setConfigWidget(created)
      }
    );
  };

  const handleConfigureSave = (next: { title: string | null; config: Record<string, unknown> }) => {
    if (!configWidget) return;
    updateWidget.mutate(
      { widgetId: configWidget.id, request: { title: next.title, config: next.config } },
      { onSuccess: () => setConfigWidget(null) }
    );
  };

  const handleRemoveWidget = (widget: DashboardWidget) => {
    setWidgetToRemove(widget);
  };

  const handleLayoutChange = (positions: { widgetId: string; gridX: number; gridY: number; gridW: number; gridH: number }[]) => {
    replaceLayout.mutate(positions);
  };

  const handleCreateDashboard = () => {
    const name = window.prompt("Name your new dashboard:", "New dashboard");
    if (!name || !name.trim()) return;
    createDashboard.mutate(
      { name: name.trim() },
      { onSuccess: (created) => setSelectedId(created.id) }
    );
  };

  // Page-context provider — exposes the live dashboard, widgets, and
  // modal state to the chatbot. Mutating actions reuse the same handlers
  // the canvas + toolbar use, so the in-memory state stays consistent.
  useDashboardPagePageContext({
    dashboards,
    selectedId,
    dashboard: dashboardQuery.data ?? null,
    widgets,
    configWidgetId: configWidget?.id ?? null,
    pickerOpen,
    renameOpen,
    deleteOpen,
    selectDashboard: setSelectedId,
    openPicker: () => setPickerOpen(true),
    closePicker: () => setPickerOpen(false),
    openRename: () => setRenameOpen(true),
    openDelete: () => setDeleteOpen(true),
    openWidgetConfig: (widgetId) => {
      const w = widgets.find((x) => x.id === widgetId);
      if (w) setConfigWidget(w);
    },
    closeWidgetConfig: () => setConfigWidget(null),
    removeWidget: (widgetId) => {
      const w = widgets.find((x) => x.id === widgetId);
      if (w) setWidgetToRemove(w);
    },
    repositionWidget: (widgetId, grid) => {
      const w = widgets.find((x) => x.id === widgetId);
      if (!w) return;
      // Mirror DashboardCanvas's onLayoutChange — send the whole layout
      // (every widget) so server-side ReplaceLayoutAsync sees the move
      // applied to one widget while leaving the others at their current
      // positions.
      const positions = widgets.map((other) =>
        other.id === widgetId
          ? { widgetId: other.id, ...grid }
          : { widgetId: other.id, gridX: other.gridX, gridY: other.gridY, gridW: other.gridW, gridH: other.gridH }
      );
      replaceLayout.mutate(positions);
    },
    addWidgetOfType: (widgetType) => {
      const def = getWidget(widgetType);
      if (def) handleAddWidget(def);
    }
  });

  return (
    <Stack className="dashboard-page" p="md">
      <Group className="dashboard-toolbar" justify="space-between">
        <Group gap="sm" align="center">
          <Title order={4}>Dashboard</Title>
          <DashboardSelector
            dashboards={dashboards}
            selectedId={selectedId}
            onSelect={setSelectedId}
            onCreate={handleCreateDashboard}
            onRename={() => setRenameOpen(true)}
            onDelete={() => setDeleteOpen(true)}
          />
        </Group>
        <Button
          leftSection={<i className="fa fa-plus" />}
          onClick={() => setPickerOpen(true)}
          disabled={!selectedId}
        >
          Add widget
        </Button>
      </Group>

      {dashboardQuery.isLoading ? (
        <Center p="xl">
          <Loader />
        </Center>
      ) : !dashboard ? (
        <Center p="xl">
          <Text c="dimmed">Select a dashboard.</Text>
        </Center>
      ) : widgets.length === 0 ? (
        <div className="dashboard-empty">
          <i className="fa fa-table-cells-large fa-2x" />
          <Text>This dashboard is empty.</Text>
          <Button variant="default" onClick={() => setPickerOpen(true)}>
            Add your first widget
          </Button>
        </div>
      ) : (
        <DashboardCanvas
          dashboardId={dashboard.id}
          widgets={widgets}
          isEditable
          onLayoutChange={handleLayoutChange}
          onConfigureWidget={setConfigWidget}
          onRemoveWidget={handleRemoveWidget}
        />
      )}

      {pickerOpen ? (
        <WidgetPickerModal onSelect={handleAddWidget} onCancel={() => setPickerOpen(false)} />
      ) : null}

      <WidgetConfigDrawer
        opened={Boolean(configWidget)}
        widget={configWidget}
        onClose={() => setConfigWidget(null)}
        onSave={handleConfigureSave}
      />

      <RenameDashboardModal
        opened={renameOpen}
        initialName={dashboard?.name ?? ""}
        onCancel={() => setRenameOpen(false)}
        onSave={(name) => {
          if (!selectedId) return;
          updateDashboard.mutate(
            { name },
            { onSuccess: () => setRenameOpen(false) }
          );
        }}
      />

      <DeleteDashboardConfirm
        opened={deleteOpen}
        name={dashboard?.name ?? ""}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={() => {
          if (!selectedId) return;
          deleteDashboard.mutate(selectedId, {
            onSuccess: () => {
              setDeleteOpen(false);
              setSelectedId(null);
            }
          });
        }}
      />

      {widgetToRemove ? (
        <ConfirmModal
          title="Remove widget?"
          message={
            <p style={{ margin: 0 }}>
              Remove <strong>{widgetToRemove.title ?? widgetToRemove.widgetType}</strong> from
              this dashboard?
            </p>
          }
          confirmLabel="Remove"
          cancelLabel="Cancel"
          variant="danger"
          busy={removeWidget.isPending}
          onConfirm={() =>
            removeWidget.mutate(widgetToRemove.id, {
              onSuccess: () => setWidgetToRemove(null)
            })
          }
          onCancel={() => setWidgetToRemove(null)}
        />
      ) : null}
    </Stack>
  );
}
