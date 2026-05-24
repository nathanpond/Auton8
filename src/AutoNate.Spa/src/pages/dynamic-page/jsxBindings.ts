import { useMemo } from "react";
import { useNavigate } from "react-router-dom";

// Functions exposed to admin-authored JSX via react-jsx-parser's `bindings`.
// Admins reference these by name in expressions, e.g.
//   <button onClick={() => navigate("/cars")}>Cars</button>
//   <button onClick={logout}>Sign out</button>
//
// To register a new binding, add it to the returned object and document it
// in the modal help text in MenuItemEditModal.tsx.
//
// Acceptable risk: `navigate` and `openInNewTab` take URLs straight from
// admin-authored JSX and pass them to react-router / window.open without a
// local-URL gate. The author is necessarily a user with permission to edit
// dynamic pages; the same threat model that ratifies unvalidated menu
// `href` (see NavMenu.tsx for the long form) applies here. /audit security
// 2026-05-23.
export function useJsxBindings(): Record<string, unknown> {
  const navigate = useNavigate();

  return useMemo(
    () => ({
      navigate: (path: string) => navigate(path),
      reload: () => window.location.reload(),
      alert: (message: string) => window.alert(message),
      confirm: (message: string) => window.confirm(message),
      openInNewTab: (url: string) =>
        window.open(url, "_blank", "noopener,noreferrer"),
      logout: () => {
        // Match the existing logout action menu item: form POST to
        // /account/logout (the cookie-auth endpoint), not the JSON API.
        const form = document.createElement("form");
        form.method = "post";
        form.action = "/account/logout";
        document.body.appendChild(form);
        form.submit();
      }
    }),
    [navigate]
  );
}
