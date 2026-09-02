import {
  createContext,
  ReactNode,
  useCallback,
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
    // The title is owned by useRouteDocumentTitle (archived-18) — it needs the site
    // name *and* the route. Setting it here too would race that effect and
    // flatten every page back to the bare site name on any appearance change.
  }, [effectiveAppearance]);

  // Stable callbacks so consumers (e.g. the SiteAppearance admin page) can
  // include them in useEffect dep arrays without re-firing the effect on
  // every render — which previously caused a setPreviewAppearance loop.
  const setPreviewAppearance = useCallback((appearance: SiteAppearance | null) => {
    setPreviewAppearanceState(appearance ? coerceSiteAppearance(appearance) : null);
  }, []);
  const clearPreviewAppearance = useCallback(() => setPreviewAppearanceState(null), []);

  const value = useMemo<SiteAppearanceContextValue>(
    () => ({
      savedAppearance,
      effectiveAppearance,
      isLoading,
      setPreviewAppearance,
      clearPreviewAppearance
    }),
    [
      effectiveAppearance,
      isLoading,
      savedAppearance,
      setPreviewAppearance,
      clearPreviewAppearance
    ]
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
