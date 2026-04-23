import { useEffect, useRef, useState } from "react";
import {
  createDotNetAdapter,
  WorkflowCallbacks
} from "@/lib/bpmn/dotNetAdapter";
import { ensureBpmnJsLoaded } from "@/lib/bpmn/loader";
import * as workflow from "@/lib/bpmn/workflow.js";

export type ReadonlyViewerHandle = unknown;

export type UseBpmnReadonlyViewerOptions = {
  xml: string | null;
  completedActivityIds: readonly string[];
  currentActivityIds: readonly string[];
  callbacks?: Pick<WorkflowCallbacks, "CompleteTaskFromContextMenu">;
  enableContextMenu?: boolean;
};

export type UseBpmnReadonlyViewerReturn = {
  containerRef: (element: HTMLDivElement | null) => void;
  loading: boolean;
  error: Error | null;
};

export function useBpmnReadonlyViewer(
  options: UseBpmnReadonlyViewerOptions
): UseBpmnReadonlyViewerReturn {
  const { xml, completedActivityIds, currentActivityIds, callbacks, enableContextMenu } = options;
  const [container, setContainer] = useState<HTMLDivElement | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const viewerRef = useRef<{ dispose?: () => void } | null>(null);
  const currentXmlRef = useRef<string | null>(null);
  const callbacksRef = useRef<UseBpmnReadonlyViewerOptions["callbacks"]>(callbacks);
  callbacksRef.current = callbacks;

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
            workflow.enableCurrentStepContextMenu(created, dotNetLike);
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
