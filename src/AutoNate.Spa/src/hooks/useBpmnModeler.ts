import { useEffect, useRef, useState } from "react";
import {
  createDotNetAdapter,
  WorkflowCallbacks
} from "@/lib/bpmn/dotNetAdapter";
import { ensureBpmnJsLoaded } from "@/lib/bpmn/loader";
import * as workflow from "@/lib/bpmn/workflow.js";

export type ModelerHandle = unknown;

export type UseBpmnModelerOptions = {
  xml: string | null;
  callbacks?: WorkflowCallbacks;
};

export type UseBpmnModelerReturn = {
  containerRef: (element: HTMLDivElement | null) => void;
  handle: ModelerHandle | null;
  loading: boolean;
  error: Error | null;
};

export function useBpmnModeler({ xml, callbacks }: UseBpmnModelerOptions): UseBpmnModelerReturn {
  const [container, setContainer] = useState<HTMLDivElement | null>(null);
  const [handle, setHandle] = useState<ModelerHandle | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const callbacksRef = useRef<WorkflowCallbacks>(callbacks ?? {});
  callbacksRef.current = callbacks ?? {};

  useEffect(() => {
    if (!container || xml === null) {
      return;
    }

    let cancelled = false;
    let localHandle: { dispose?: () => void } | null = null;
    setLoading(true);
    setError(null);

    const run = async () => {
      try {
        await ensureBpmnJsLoaded();
        const dotNetLike = createDotNetAdapter(callbacksRef.current);
        const created = await workflow.createModeler(container, xml, dotNetLike);
        if (cancelled) {
          created?.dispose?.();
          return;
        }
        localHandle = created;
        setHandle(created);
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
      setHandle(null);
      if (localHandle) {
        try {
          workflow.disposeModeler(localHandle);
        } catch {
          // Best-effort dispose; nothing useful we can do with a late failure.
        }
      }
    };
    // xml on purpose: re-creating modeler when XML identity changes is the simplest correct behavior
    // (callers can memoize xml to avoid churn). Container identity triggers re-mount too.
  }, [container, xml]);

  return { containerRef: setContainer, handle, loading, error };
}
