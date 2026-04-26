/**
 * workflow.js was originally written for Blazor JS-interop. It expects a
 * `dotNetRef` object with `invokeMethodAsync(methodName, ...args)`. We keep
 * workflow.js framework-agnostic by adapting a plain callback map into that
 * shape. Method names mirror the [JSInvokable] C# methods that used to exist.
 */
export type WorkflowCallbacks = {
  NotifyDiagramChanged?: () => void | Promise<void>;
  NotifySelectionChanged?: (element: unknown) => void | Promise<void>;
  // Single-task or single-assignee completion from the diagram context menu.
  CompleteTaskFromContextMenu?: (
    activityId: string,
    activityName: string | null,
    taskId: string
  ) => void | Promise<void>;
  // Multi-instance complete-all from the diagram context menu.
  CompleteAllTasksFromContextMenu?: (
    activityId: string,
    activityName: string | null,
    taskIds: string[]
  ) => void | Promise<void>;
};

export type DotNetLikeRef = {
  invokeMethodAsync: (methodName: string, ...args: unknown[]) => Promise<void>;
};

export function createDotNetAdapter(callbacks: WorkflowCallbacks): DotNetLikeRef {
  return {
    invokeMethodAsync: async (methodName, ...args) => {
      const handler = (callbacks as Record<string, unknown>)[methodName];
      if (typeof handler === "function") {
        await (handler as (...handlerArgs: unknown[]) => unknown)(...args);
      }
    }
  };
}
