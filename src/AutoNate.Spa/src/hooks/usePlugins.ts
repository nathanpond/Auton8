import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Plugin,
  deletePlugin,
  disablePlugin,
  enablePlugin,
  listPlugins,
  uploadPlugin,
} from "@/api/plugins";

export const PLUGINS_KEY = ["admin", "plugins"] as const;

export function usePlugins() {
  return useQuery<Plugin[]>({
    queryKey: PLUGINS_KEY,
    queryFn: ({ signal }) => listPlugins(signal),
  });
}

export function useUploadPlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => uploadPlugin(file),
    onSuccess: () => qc.invalidateQueries({ queryKey: PLUGINS_KEY }),
  });
}

export function useEnablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => enablePlugin(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PLUGINS_KEY }),
  });
}

export function useDisablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => disablePlugin(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PLUGINS_KEY }),
  });
}

export function useDeletePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deletePlugin(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PLUGINS_KEY }),
  });
}
