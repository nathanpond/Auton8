import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addWidget,
  createDashboard,
  CreateDashboardRequest,
  CreateWidgetRequest,
  Dashboard,
  DashboardWithWidgets,
  deleteDashboard,
  getDashboard,
  LayoutPositionDto,
  listDashboards,
  removeWidget,
  replaceLayout,
  updateDashboard,
  UpdateDashboardRequest,
  updateWidget,
  UpdateWidgetRequest
} from "@/api/dashboards";

export const DASHBOARDS_QUERY_KEY = ["dashboards"] as const;
export const DASHBOARD_QUERY_KEY = (id: string) => ["dashboards", id] as const;

export function useDashboards() {
  return useQuery<Dashboard[]>({
    queryKey: DASHBOARDS_QUERY_KEY,
    queryFn: ({ signal }) => listDashboards(signal)
  });
}

export function useDashboard(id: string | null) {
  return useQuery<DashboardWithWidgets | null>({
    queryKey: DASHBOARD_QUERY_KEY(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getDashboard(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useCreateDashboard() {
  const qc = useQueryClient();
  return useMutation<Dashboard, Error, CreateDashboardRequest>({
    mutationFn: createDashboard,
    onSuccess: (created) => {
      qc.setQueryData<Dashboard[]>(DASHBOARDS_QUERY_KEY, (old) =>
        old ? [...old.filter((dashboard) => dashboard.id !== created.id), created] : [created]
      );
      void qc.invalidateQueries({ queryKey: DASHBOARDS_QUERY_KEY });
      qc.setQueryData(DASHBOARD_QUERY_KEY(created.id), {
        dashboard: created,
        widgets: []
      } satisfies DashboardWithWidgets);
    }
  });
}

export function useUpdateDashboard(id: string) {
  const qc = useQueryClient();
  return useMutation<Dashboard, Error, UpdateDashboardRequest>({
    mutationFn: (req) => updateDashboard(id, req),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: DASHBOARDS_QUERY_KEY });
      qc.setQueryData<DashboardWithWidgets | null>(
        DASHBOARD_QUERY_KEY(id),
        (prev) => (prev ? { ...prev, dashboard: updated } : prev)
      );
    }
  });
}

export function useDeleteDashboard() {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (id) => deleteDashboard(id),
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: DASHBOARDS_QUERY_KEY });
      qc.removeQueries({ queryKey: DASHBOARD_QUERY_KEY(id) });
    }
  });
}

export function useAddWidget(dashboardId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateWidgetRequest) => addWidget(dashboardId, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: DASHBOARD_QUERY_KEY(dashboardId) })
  });
}

export function useUpdateWidget(dashboardId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { widgetId: string; request: UpdateWidgetRequest }) =>
      updateWidget(dashboardId, vars.widgetId, vars.request),
    onSuccess: () => qc.invalidateQueries({ queryKey: DASHBOARD_QUERY_KEY(dashboardId) })
  });
}

export function useRemoveWidget(dashboardId: string) {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (widgetId) => removeWidget(dashboardId, widgetId),
    onSuccess: () => qc.invalidateQueries({ queryKey: DASHBOARD_QUERY_KEY(dashboardId) })
  });
}

export function useReplaceLayout(dashboardId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (positions: LayoutPositionDto[]) => replaceLayout(dashboardId, positions),
    onSuccess: () => qc.invalidateQueries({ queryKey: DASHBOARD_QUERY_KEY(dashboardId) })
  });
}
