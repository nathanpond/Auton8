import { useEffect, useRef } from "react";
import { useLocation } from "react-router-dom";

// Moves focus to the main content region on route change.
//
// In a server-rendered app a navigation resets focus to the document; in an
// SPA it does not, so focus stayed on whichever nav link was just activated.
// A screen reader therefore announced nothing at all on navigation and Tab
// resumed in the header rather than in the page the user had just asked for —
// WCAG 2.4.3 (Focus Order), 508 §502 (#15).
//
// The target is the same `#content` wrapper the skip link points at, which
// already carries tabIndex={-1} for exactly this reason.
export function useRouteFocus(): void {
  const { pathname } = useLocation();
  const isFirstRender = useRef(true);

  useEffect(() => {
    // Not on first paint: the user has just loaded the page and the browser's
    // own initial focus is correct. Stealing it here would also fight the
    // autofocus on any landing form.
    if (isFirstRender.current) {
      isFirstRender.current = false;
      return;
    }

    const content = document.getElementById("content");
    if (!content) return;

    // preventScroll because focusing a container would otherwise jump the
    // viewport; route changes already start at the top.
    content.focus({ preventScroll: true });
  }, [pathname]);
}
