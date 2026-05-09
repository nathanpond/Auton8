import { createContext, useCallback, useContext, useEffect, useRef, useSyncExternalStore } from "react";
import {
  PageContextProviderEntry,
  PageQueryRequest,
  PageQueryResult,
  PageSnapshot
} from "./types";

// Registry-internal entry: the public PageContextProviderEntry plus a
// per-registration nonce so we can identify which one to remove during
// unregister. Last-mounted-per-pageKey wins.
type Registration = PageContextProviderEntry & { _nonce: number };

type RegistryHandle = {
  // Add a new registration. Returns a disposer.
  register: (entry: PageContextProviderEntry) => () => void;

  // Read the active provider's snapshot for a given pageKey. Returns null
  // when no provider is registered or its getSnapshot returned null. Safe
  // to call from a submit handler — purely synchronous.
  getActiveSnapshot: (pageKey: string) => PageSnapshot | null;

  // Dispatch a backend-issued query to the active provider. Returns a
  // failure result if no provider is registered or the provider doesn't
  // implement onPageQuery.
  dispatchPageQuery: (pageKey: string, request: PageQueryRequest) => Promise<PageQueryResult>;

  // Subscribe to the active provider's identity changing (mount/unmount).
  // Used by useActivePageSummary to know when to recompute.
  subscribe: (listener: () => void) => () => void;
};

const PageContextCtx = createContext<RegistryHandle | null>(null);

// Provider component. Lives high in the tree (AppShell). Holds all
// registrations in a ref so updating the active provider does NOT
// re-render consumers — only the few that explicitly subscribe.
export function PageContextRegistryProvider({ children }: { children: React.ReactNode }) {
  // pageKey → stack of registrations. Top of stack = active.
  const stacksRef = useRef<Map<string, Registration[]>>(new Map());
  const listenersRef = useRef<Set<() => void>>(new Set());
  const nonceRef = useRef(0);

  const notify = useCallback(() => {
    for (const listener of listenersRef.current) {
      try { listener(); } catch { /* one bad listener shouldn't break others */ }
    }
  }, []);

  const register = useCallback((entry: PageContextProviderEntry): () => void => {
    const nonce = ++nonceRef.current;
    const reg: Registration = { ...entry, _nonce: nonce };
    let stack = stacksRef.current.get(entry.pageKey);
    if (!stack) {
      stack = [];
      stacksRef.current.set(entry.pageKey, stack);
    }
    stack.push(reg);
    notify();
    return () => {
      const s = stacksRef.current.get(entry.pageKey);
      if (!s) return;
      const idx = s.findIndex((r) => r._nonce === nonce);
      if (idx >= 0) s.splice(idx, 1);
      if (s.length === 0) stacksRef.current.delete(entry.pageKey);
      notify();
    };
  }, [notify]);

  const getActiveSnapshot = useCallback((pageKey: string): PageSnapshot | null => {
    const stack = stacksRef.current.get(pageKey);
    if (!stack || stack.length === 0) return null;
    const top = stack[stack.length - 1];
    try {
      return top.getSnapshot();
    } catch (err) {
      console.warn("page-context: getSnapshot threw", err);
      return null;
    }
  }, []);

  const dispatchPageQuery = useCallback(async (pageKey: string, request: PageQueryRequest): Promise<PageQueryResult> => {
    const stack = stacksRef.current.get(pageKey);
    if (!stack || stack.length === 0) {
      return { ok: false, error: "page_unreachable", message: "No page provider is registered." };
    }
    const top = stack[stack.length - 1];
    if (!top.onPageQuery) {
      return { ok: false, error: "unsupported", message: "Active page does not handle queries." };
    }
    try {
      return await top.onPageQuery(request);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return { ok: false, error: "handler_threw", message };
    }
  }, []);

  const subscribe = useCallback((listener: () => void) => {
    listenersRef.current.add(listener);
    return () => { listenersRef.current.delete(listener); };
  }, []);

  const handle: RegistryHandle = {
    register,
    getActiveSnapshot,
    dispatchPageQuery,
    subscribe
  };

  return <PageContextCtx.Provider value={handle}>{children}</PageContextCtx.Provider>;
}

function useRegistry(): RegistryHandle {
  const ctx = useContext(PageContextCtx);
  if (!ctx) {
    throw new Error("PageContextRegistryProvider is missing from the tree.");
  }
  return ctx;
}

// Pages call this once on mount to register their provider. The entry's
// fields should be stable across re-renders (use useCallback for
// getSnapshot / onPageQuery) — re-registering on every render would churn
// the active-provider stack.
export function useRegisterPageContext(entry: PageContextProviderEntry): void {
  const registry = useRegistry();
  useEffect(() => {
    return registry.register(entry);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [registry, entry.pageKey, entry.getSnapshot, entry.onPageQuery]);
}

// Synchronous accessor for the chat send path. Returns the freshest
// snapshot for the given page or null if no provider is registered.
export function usePageContextRegistry(): RegistryHandle {
  return useRegistry();
}

// Re-renders the caller when the active provider for the current page key
// changes (mount/unmount). Reads a fresh summary on each render.
export function useActivePageSummary(pageKey: string): string | null {
  const registry = useRegistry();
  return useSyncExternalStore(
    registry.subscribe,
    () => registry.getActiveSnapshot(pageKey)?.summary ?? null,
    () => null
  );
}
