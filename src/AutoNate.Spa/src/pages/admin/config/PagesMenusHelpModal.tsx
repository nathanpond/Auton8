import { useEffect } from "react";
import { usePageTemplates } from "@/hooks/usePageTemplates";

type Props = {
  onClose: () => void;
};

export default function PagesMenusHelpModal({ onClose }: Props) {
  const { data: templates = [], isLoading } = usePageTemplates();

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const sortedTemplates = [...templates].sort((a, b) => a.name.localeCompare(b.name));

  return (
    <>
      <div className="modal fade show d-block" tabIndex={-1} role="dialog" aria-modal="true">
        <div className="modal-dialog modal-xl modal-dialog-scrollable">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">How Pages / Menus works</h5>
              <button type="button" className="btn-close" aria-label="Close" onClick={onClose} />
            </div>
            <div className="modal-body">
              <p>
                Every navigation surface on the site is a <strong>menu</strong>. A menu
                is a tree of <strong>menu items</strong>; each item is one of a few
                types and either links to a URL or organizes its siblings. Reorder
                items by dragging, edit any item by clicking the pencil, delete any
                item with the trash icon.
              </p>

              <hr />

              <h6>1. The five system menus</h6>
              <ul>
                <li>
                  <strong>Main Menu</strong> — the horizontal nav across the top of
                  every page.
                </li>
                <li>
                  <strong>Icon Menu</strong> — the icon strip on the right side of the
                  top bar. Each top-level item is an icon; <code>group</code> items
                  open a dropdown.
                </li>
                <li>
                  <strong>User Menu</strong> — the dropdown that opens beside the
                  signed-in user's name.
                </li>
                <li>
                  <strong>Site Configuration</strong> — the left-hand sidebar shown
                  inside the Site Configuration area.
                </li>
                <li>
                  <strong>Standalone Pages</strong> — a hidden container. Items here
                  are URL-reachable but never appear in any visible nav. Use it to
                  expose a page template by URL without taking up nav space.
                </li>
              </ul>
              <p>
                The five system menus can't be deleted (the lock icon on their tab
                marks them), but their <em>contents</em> are fully editable — you can
                add, remove, reorder, and retype anything inside.
              </p>

              <hr />

              <h6>2. Menu item types</h6>
              <dl className="row mb-0">
                <dt className="col-sm-3">Group</dt>
                <dd className="col-sm-9">
                  Header that contains child items. Renders as a dropdown in the main
                  and icon menus; renders as a collapsible section in the Site
                  Configuration sidebar.
                </dd>

                <dt className="col-sm-3">Template</dt>
                <dd className="col-sm-9">
                  Mounts a built-in <strong>page template</strong> at a URL. Pick the
                  template from the dropdown — its component renders at the chosen
                  path. Leave the path field blank to use the template's default URL.
                  <em> See the template catalog below.</em>
                </dd>

                <dt className="col-sm-3">Route</dt>
                <dd className="col-sm-9">
                  Navigates to a hardcoded route in the SPA (e.g.{" "}
                  <code>/records/CAR</code>, <code>/workflow</code>). Set an
                  <em> alias URL</em> to make the menu point at a friendlier path
                  (e.g. <code>/cars</code>) that renders the same target component.
                </dd>

                <dt className="col-sm-3">Page</dt>
                <dd className="col-sm-9">
                  Defines a brand new URL with custom content. Use HTML for
                  static markup, or JSX to write a full React component
                  (hooks, state, API calls) — inline, no rebuild required.
                </dd>

                <dt className="col-sm-3">Link</dt>
                <dd className="col-sm-9">
                  An external URL. Optionally opens in a new tab.
                </dd>

                <dt className="col-sm-3">Action</dt>
                <dd className="col-sm-9">
                  Triggers a built-in action. Today this is just <code>logout</code>{" "}
                  (POST <code>/account/logout</code>) used by the user menu.
                </dd>

                <dt className="col-sm-3">Separator</dt>
                <dd className="col-sm-9">
                  A divider line. Only renders inside vertical menus (dropdowns and
                  the sidebar) — top-level separators are skipped.
                </dd>
              </dl>

              <hr />

              <h6>3. Permissions on items</h6>
              <p>
                Each item has an optional <code>permission_required</code> in
                <code> kind.action</code> form (e.g. <code>siteconfig.edit</code>).
                When set, the item is hidden from any user who doesn't have that
                permission. The check is performed by the backend when serving the
                menu tree — so an admin removing a user's access can hide entire
                sections of the nav for that user without touching this page.
              </p>

              <hr />

              <h6>4. Page templates catalog</h6>
              <p>
                Page templates are the built-in screens that ship with the
                application. A template is only reachable when an admin places it on
                a menu (any menu — including <strong>Standalone Pages</strong>). The
                same template can be mounted at multiple paths on different menus.
              </p>

              {isLoading ? (
                <div className="text-muted small">Loading templates…</div>
              ) : sortedTemplates.length === 0 ? (
                <div className="text-muted small">
                  No page templates are registered.
                </div>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm align-middle">
                    <thead>
                      <tr>
                        <th style={{ width: "20%" }}>Template</th>
                        <th style={{ width: "30%" }}>Default URL</th>
                        <th>Description</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sortedTemplates.map((t) => (
                        <tr key={t.key}>
                          <td>{t.name}</td>
                          <td>
                            <code>{t.defaultPath}</code>
                          </td>
                          <td className="text-muted">
                            {t.description ?? <em>(no description)</em>}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <hr />

              <h6>5. Tips</h6>
              <ul>
                <li>
                  <strong>Drag</strong> the grip handle on the left of any row to
                  reorder. Drag right to nest under the row above; drag left to
                  un-nest.
                </li>
                <li>
                  <strong>Reordering is staged.</strong> Click <em>Save order</em>{" "}
                  in the panel header to persist; <em>Cancel</em> to revert.
                </li>
                <li>
                  <strong>Editing a single item</strong> (icon, type, path,
                  permission) saves immediately when you click <em>Apply</em> in the
                  edit modal.
                </li>
                <li>
                  <strong>Hidden items</strong> (the eye/visibility flag) stay in the
                  tree but don't render for users — handy for prepping a menu before
                  going live with it.
                </li>
              </ul>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-primary" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" onClick={onClose} />
    </>
  );
}
