import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from "react";

const STORAGE_KEY = "autonate.agent.open";

type AgentSidebarValue = {
  isOpen: boolean;
  open: () => void;
  close: () => void;
  toggle: () => void;
};

const Context = createContext<AgentSidebarValue | null>(null);

export function AgentSidebarProvider({ children }: { children: ReactNode }) {
  const [isOpen, setIsOpen] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    return window.localStorage.getItem(STORAGE_KEY) === "true";
  });

  useEffect(() => {
    if (typeof window !== "undefined") {
      window.localStorage.setItem(STORAGE_KEY, isOpen ? "true" : "false");
    }
  }, [isOpen]);

  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);
  const toggle = useCallback(() => setIsOpen((o) => !o), []);

  return <Context.Provider value={{ isOpen, open, close, toggle }}>{children}</Context.Provider>;
}

// Returns a no-op fallback when the provider isn't mounted (e.g. during the
// pre-auth login screen, which uses AuthShell instead of AppShell). That
// keeps the trigger button safe to render anywhere without crashing.
export function useAgentSidebar(): AgentSidebarValue {
  const ctx = useContext(Context);
  if (ctx) return ctx;
  return {
    isOpen: false,
    open: () => {},
    close: () => {},
    toggle: () => {}
  };
}
