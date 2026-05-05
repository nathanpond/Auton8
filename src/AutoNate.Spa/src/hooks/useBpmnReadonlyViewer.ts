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
  // ISO 8601 string. Forwarded into the change-due-date modal so it can
  // pre-fill the date input. Null when the task has no due date.
  dueDate?: string | null;
};

export type ContextMenuOptions = {
  // Called on every right-click. Returning false suppresses the override-only
  // task actions (Complete, Reassign, Change Due Date) on current-activity
  // nodes. Move Execution Here is gated separately via getCanMoveState.
  getCanOverride: () => boolean;
  // Called on every right-click on a non-current activity. Returning true
  // unlocks the "Move Execution Here" entry. The React layer also factors in
  // whether the run is still in flight before returning true.
  getCanMoveState: () => boolean;
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

export type UserTaskHoverInfo = {
  title: string;
  rows: Array<{ label: string; value: string }>;
};

export type HoverTooltipOptions = {
  // Called on every UserTask hover. Return null to suppress (e.g. while data
  // is still loading). bpmn carries the raw flowable:assignee/flowable:dueDate
  // attributes from the BPMN model so the resolver can fall back to design-
  // time values for tasks that haven't been instantiated yet.
  getInfo: (
    activityId: string,
    activityName: string | null,
    bpmn: { assignee: string | null; dueDate: string | null }
  ) => UserTaskHoverInfo | null;
};

export type UseBpmnReadonlyViewerOptions = {
  xml: string | null;
  completedActivityIds: readonly string[];
  currentActivityIds: readonly string[];
  // Activities that were halted in flight when the execution was cancelled.
  // Optional — empty/undefined means no cancellation overlay is drawn.
  cancelledActivityIds?: readonly string[];
  // Activities that produced a job.execution.failed event for this run.
  // Optional — empty/undefined means no failure overlay is drawn.
  failedActivityIds?: readonly string[];
  callbacks?: Pick<
    WorkflowCallbacks,
    | "CompleteTaskFromContextMenu"
    | "CompleteAllTasksFromContextMenu"
    | "ReassignTaskFromContextMenu"
    | "ChangeDueDateFromContextMenu"
    | "MoveExecutionHereFromContextMenu"
  >;
  enableContextMenu?: boolean;
  contextMenu?: ContextMenuOptions;
  enableHoverTooltip?: boolean;
  hoverTooltip?: HoverTooltipOptions;
};

export type UseBpmnReadonlyViewerReturn = {
  containerRef: (element: HTMLDivElement | null) => void;
  loading: boolean;
  error: Error | null;
};

export function useBpmnReadonlyViewer(
  options: UseBpmnReadonlyViewerOptions
): UseBpmnReadonlyViewerReturn {
  const {
    xml,
    completedActivityIds,
    currentActivityIds,
    cancelledActivityIds,
    failedActivityIds,
    callbacks,
    enableContextMenu,
    contextMenu,
    enableHoverTooltip,
    hoverTooltip
  } = options;
  const [container, setContainer] = useState<HTMLDivElement | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const viewerRef = useRef<{ dispose?: () => void } | null>(null);
  const currentXmlRef = useRef<string | null>(null);
  // Kept current on every render so the async mount effect always seeds the
  // tooltip with the latest value even if the prop changed while BPMN-JS was
  // still loading.
  const failedActivityIdsRef = useRef<readonly string[] | undefined>(failedActivityIds);
  failedActivityIdsRef.current = failedActivityIds;
  const callbacksRef = useRef<UseBpmnReadonlyViewerOptions["callbacks"]>(callbacks);
  callbacksRef.current = callbacks;

  // Refs let workflow.js read the latest values on each contextmenu event
  // without the React side rebuilding the viewer when permissions or task data
  // change. The thunks closed over below always read .current.
  const contextMenuRef = useRef<ContextMenuOptions | undefined>(contextMenu);
  contextMenuRef.current = contextMenu;
  const hoverTooltipRef = useRef<HoverTooltipOptions | undefined>(hoverTooltip);
  hoverTooltipRef.current = hoverTooltip;

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
              getCanMoveState: () => contextMenuRef.current?.getCanMoveState() ?? false,
              getActiveTasksAtActivity: (activityId: string, activityName: string | null) =>
                contextMenuRef.current?.getActiveTasksAtActivity(activityId, activityName) ?? [],
              getCompletedAssignees: (activityId: string) =>
                contextMenuRef.current?.getCompletedAssignees(activityId) ?? Promise.resolve([])
            });
          }

          if (enableHoverTooltip) {
            workflow.enableUserTaskHoverTooltip(created, {
              getInfo: (
                activityId: string,
                activityName: string | null,
                bpmn: { assignee: string | null; dueDate: string | null }
              ) => hoverTooltipRef.current?.getInfo(activityId, activityName, bpmn) ?? null,
              failedActivityIds: failedActivityIdsRef.current ?? []
            });
            // Flush the freshest value through the handle. Above we passed the
            // ref-captured failedActivityIds, which may be stale if the parent
            // re-rendered while createReadonlyViewer was awaiting BPMN-JS load.
            // The effect below depends on viewerRef.current.getHoverTooltip(); now
            // that the handle exists, push the latest set explicitly.
            (created as
              | { getHoverTooltip?: () => { setFailedActivityIds?: (ids: readonly string[]) => void } | null }
              | null)
              ?.getHoverTooltip?.()
              ?.setFailedActivityIds?.(failedActivityIdsRef.current ?? []);
          }
        }

        workflow.highlightExecutionState(
          viewerRef.current,
          completedActivityIds,
          currentActivityIds,
          cancelledActivityIds ?? [],
          failedActivityIds ?? []
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
  }, [container, xml, enableContextMenu, enableHoverTooltip]);

  // Re-highlight on activity changes without rebuilding the viewer.
  useEffect(() => {
    if (!viewerRef.current) {
      return;
    }

    try {
      workflow.highlightExecutionState(
        viewerRef.current,
        completedActivityIds,
        currentActivityIds,
        cancelledActivityIds ?? [],
        failedActivityIds ?? []
      );
    } catch {
      // Re-highlight can fail if called after dispose; not fatal.
    }
  }, [completedActivityIds, currentActivityIds, cancelledActivityIds, failedActivityIds]);

  // The hover tooltip captures failedActivityIds at viewer-creation time.
  // Push subsequent updates through the setFailedActivityIds hook installed
  // by workflow.js so a hover after retry/recovery sees the latest set.
  useEffect(() => {
    const handle = viewerRef.current as
      | { getHoverTooltip?: () => { setFailedActivityIds?: (ids: readonly string[]) => void } | null }
      | null;
    handle?.getHoverTooltip?.()?.setFailedActivityIds?.(failedActivityIds ?? []);
  }, [failedActivityIds]);

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
