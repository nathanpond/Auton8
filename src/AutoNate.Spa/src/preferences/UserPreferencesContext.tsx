import {
  createContext,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState
} from "react";

export type ChatbotWindowMode = "overlay" | "fill";

export type UserPreferencesValue = {
  isModalOpen: boolean;
  openModal: () => void;
  closeModal: () => void;
  chatbotWindowMode: ChatbotWindowMode;
  chatbotOverHeader: boolean;
  setChatbotWindowMode: (mode: ChatbotWindowMode) => void;
  setChatbotOverHeader: (value: boolean) => void;
};

const STORAGE_WINDOW_MODE = "autonate.prefs.chatbotWindowMode";
const STORAGE_OVER_HEADER = "autonate.prefs.chatbotOverHeader";

const Context = createContext<UserPreferencesValue | null>(null);

function readStoredMode(): ChatbotWindowMode {
  if (typeof window === "undefined") return "overlay";
  const v = window.localStorage.getItem(STORAGE_WINDOW_MODE);
  return v === "fill" ? "fill" : "overlay";
}

function readStoredOverHeader(): boolean {
  if (typeof window === "undefined") return true;
  const v = window.localStorage.getItem(STORAGE_OVER_HEADER);
  // Default to true so first-time users match the AgentSidebar's existing
  // behavior (top: 0, on top of the header).
  return v === null ? true : v === "true";
}

export function UserPreferencesProvider({ children }: { children: ReactNode }) {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [chatbotWindowMode, setChatbotWindowModeState] =
    useState<ChatbotWindowMode>(readStoredMode);
  const [chatbotOverHeader, setChatbotOverHeaderState] = useState<boolean>(
    readStoredOverHeader
  );

  useEffect(() => {
    if (typeof window !== "undefined") {
      window.localStorage.setItem(STORAGE_WINDOW_MODE, chatbotWindowMode);
    }
  }, [chatbotWindowMode]);

  useEffect(() => {
    if (typeof window !== "undefined") {
      window.localStorage.setItem(
        STORAGE_OVER_HEADER,
        chatbotOverHeader ? "true" : "false"
      );
    }
  }, [chatbotOverHeader]);

  const openModal = useCallback(() => setIsModalOpen(true), []);
  const closeModal = useCallback(() => setIsModalOpen(false), []);
  const setChatbotWindowMode = useCallback(
    (mode: ChatbotWindowMode) => setChatbotWindowModeState(mode),
    []
  );
  const setChatbotOverHeader = useCallback(
    (value: boolean) => setChatbotOverHeaderState(value),
    []
  );

  const value = useMemo<UserPreferencesValue>(
    () => ({
      isModalOpen,
      openModal,
      closeModal,
      chatbotWindowMode,
      chatbotOverHeader,
      setChatbotWindowMode,
      setChatbotOverHeader
    }),
    [
      isModalOpen,
      openModal,
      closeModal,
      chatbotWindowMode,
      chatbotOverHeader,
      setChatbotWindowMode,
      setChatbotOverHeader
    ]
  );

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

// Mirrors AgentSidebarContext: returns inert defaults when used outside the
// provider (e.g. AuthShell) so trigger components can render anywhere.
export function useUserPreferences(): UserPreferencesValue {
  const ctx = useContext(Context);
  if (ctx) return ctx;
  return {
    isModalOpen: false,
    openModal: () => {},
    closeModal: () => {},
    chatbotWindowMode: "overlay",
    chatbotOverHeader: true,
    setChatbotWindowMode: () => {},
    setChatbotOverHeader: () => {}
  };
}
