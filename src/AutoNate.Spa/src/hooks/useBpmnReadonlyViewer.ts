import { useEffect, useRef, useState } from "react";
import {
  createDotNetAdapter,
  WorkflowCallbacks
} from "@/lib/bpmn/dotNetAdapter";
import { ensureBpmnJsLoaded } from "@/lib/bpmn/loader";
import * as workflow from "@/lib/bpmn/workflow.js";

export type ReadonlyViewerHandle = unknown;

export type ContextMenuActiveTask = {
  id: string;
  assignee: string | null;
};

export type ContextMenuOptions = {
  // Called on every right-click. Returning false suppresses the menu entirely.
  getCanOverride: () => boolean;
  // Returns the active runtime tasks at a BPMN activity. Empty array → no menu.
  // activityName is the BPMN element's display label, used as a fallback when
  // taskDefinitionKey doesn't match (some Flowable deployments key tasks
  // differently than the XML id).
  getActiveTasksAtActivity: (
    activityId: string,
    activityName: string | null
  ) => ContextMenuActiveTask[];
  // Lazily fetched on submenu open. Drives the disabled state of completed
  // entries in "Complete Task For…".
  getCompletedAssignees: (activityId: string) => Promise<string[]>;
};

export type UseBpmnReadonlyViewerOptions = {
  xml: string | null;
  completedActivityIds: readonly string[];
  currentActivityIds: readonly string[];
  callbacks?: Pick<
    WorkflowCallbacks,
    "CompleteTaskFromContextMenu" | "CompleteAllTasksFromContextMenu"
  >;
  enableContextMenu?: boolean;
  contextMenu?: ContextMenuOptions;
};

export type UseBpmnReadonlyViewerReturn = {
  containerRef: (element: HTMLDivElement | null) => void;
  loading: boolean;
  error: Error | null;
};

export function useBpmnReadonlyViewer(
  options: UseBpmnReadonlyViewerOptions
): UseBpmnReadonlyViewerReturn {
  const { xml, completedActivityIds, currentActivityIds, callbacks, enableContextMenu, contextMenu } = options;
  const [container, setContainer] = useState<HTMLDivElement | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const viewerRef = useRef<{ dispose?: () => void } | null>(null);
  const currentXmlRef = useRef<string | null>(null);
  const callbacksRef = useRef<UseBpmnReadonlyViewerOptions["callbacks"]>(callbacks);
  callbacksRef.current = callbacks;

  // Refs let workflow.js read the latest values on each contextmenu event
  // without the React side rebuilding the viewer when permissions or task data
  // change. The thunks closed over below always read .current.
  const contextMenuRef = useRef<ContextMenuOptions | undefined>(contextMenu);
  contextMenuRef.current = contextMenu;

  // Mount + unmount on container / xml-identity change.
  useEffect(() => {
    if (!container || xml === null) {
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);

    const run = async () => {
      try {
        await ensureBpmnJsLoaded();

        if (viewerRef.current && currentXmlRef.current !== xml) {
          await workflow.loadReadonlyDiagram(viewerRef.current, xml);
          currentXmlRef.current = xml;
        } else if (!viewerRef.current) {
          const created = await workflow.createReadonlyViewer(container, xml);
          if (cancelled) {
            created?.dispose?.();
            return;
          }
          viewerRef.current = created;
          currentXmlRef.current = xml;

          if (enableContextMenu && callbacksRef.current) {
            const dotNetLike = createDotNetAdapter(callbacksRef.current);
            workflow.enableCurrentStepContextMenu(created, dotNetLike, {
              getCanOverride: () => contextMenuRef.current?.getCanOverride() ?? true,
              getActiveTasksAtActivity: (activityId: string, activityName: string | null) =>
                contextMenuRef.current?.getActiveTasksAtActivity(activityId, activityName) ?? [],
              getCompletedAssignees: (activityId: string) =>
                contextMenuRef.current?.getCompletedAssignees(activityId) ?? Promise.resolve([])
            });
          }
        }

        workflow.highlightExecutionState(
          viewerRef.current,
          completedActivityIds,
          currentActivityIds
        );
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err : new Error(String(err)));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    run();

    return () => {
      cancelled = true;
    };
  }, [container, xml, enableContextMenu]);

  // Re-highlight on activity changes without rebuilding the viewer.
  useEffect(() => {
    if (!viewerRef.current) {
      return;
    }

    try {
      workflow.highlightExecutionState(
        viewerRef.current,
        completedActivityIds,
        currentActivityIds
      );
    } catch {
      // Re-highlight can fail if called after dispose; not fatal.
    }
  }, [completedActivityIds, currentActivityIds]);

  // Dispose on unmount.
  useEffect(() => {
    return () => {
      if (viewerRef.current) {
        try {
          viewerRef.current.dispose?.();
        } catch {
          // Best-effort dispose.
        }
        viewerRef.current = null;
        currentXmlRef.current = null;
      }
    };
  }, []);

  return { containerRef: setContainer, loading, error };
}
