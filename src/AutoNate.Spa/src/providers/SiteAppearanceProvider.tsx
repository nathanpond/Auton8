import {
  createContext,
  ReactNode,
  useContext,
  useEffect,
  useMemo,
  useState
} from "react";
import {
  DEFAULT_SITE_APPEARANCE,
  applySiteAppearanceToDocument,
  coerceSiteAppearance
} from "@/lib/siteAppearance";
import { usePublicSiteAppearance } from "@/hooks/useSiteAppearance";
import { SiteAppearance } from "@/types/siteAppearance";

type SiteAppearanceContextValue = {
  savedAppearance: SiteAppearance;
  effectiveAppearance: SiteAppearance;
  isLoading: boolean;
  setPreviewAppearance: (appearance: SiteAppearance | null) => void;
  clearPreviewAppearance: () => void;
};

const SiteAppearanceContext = createContext<SiteAppearanceContextValue | null>(null);

export function SiteAppearanceProvider({ children }: { children: ReactNode }) {
  const { data, isLoading } = usePublicSiteAppearance();
  const [previewAppearance, setPreviewAppearanceState] = useState<SiteAppearance | null>(null);

  const savedAppearance = useMemo(
    () => coerceSiteAppearance(data ?? DEFAULT_SITE_APPEARANCE),
    [data]
  );
  const effectiveAppearance = previewAppearance ?? savedAppearance;

  useEffect(() => {
    applySiteAppearanceToDocument(effectiveAppearance);
    document.title = effectiveAppearance.siteName;
  }, [effectiveAppearance]);

  const value = useMemo<SiteAppearanceContextValue>(
    () => ({
      savedAppearance,
      effectiveAppearance,
      isLoading,
      setPreviewAppearance: (appearance) => {
        setPreviewAppearanceState(appearance ? coerceSiteAppearance(appearance) : null);
      },
      clearPreviewAppearance: () => setPreviewAppearanceState(null)
    }),
    [effectiveAppearance, isLoading, savedAppearance]
  );

  return (
    <SiteAppearanceContext.Provider value={value}>
      {children}
    </SiteAppearanceContext.Provider>
  );
}

export function useSiteAppearance() {
  const context = useContext(SiteAppearanceContext);
  if (!context) {
    throw new Error("useSiteAppearance must be used within SiteAppearanceProvider.");
  }

  return context;
}
