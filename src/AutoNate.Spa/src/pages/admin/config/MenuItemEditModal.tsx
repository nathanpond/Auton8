import { useEffect, useMemo, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { html } from "@codemirror/lang-html";
import { javascript } from "@codemirror/lang-javascript";
import { MenuItem, MenuItemType } from "@/types/menus";
import IconPicker from "@/components/IconPicker";
import { findIcon } from "@/lib/faIcons";
import { usePageTemplates } from "@/hooks/usePageTemplates";
import { usePages } from "@/hooks/usePages";
import { findCollidingAppRoute } from "@/routes/appRoutes";

function extractIconName(stored: string | null | undefined): string {
  if (!stored) return "";
  const tokens = stored.split(/\s+/).filter(Boolean);
  for (const t of tokens) {
    if (findIcon(t)) return t.startsWith("fa-") ? t : `fa-${t}`;
  }
  return stored.trim();
}

type Props = {
  item: MenuItem;
  onSave: (next: MenuItem, options?: { keepOpen?: boolean }) => void;
  onCancel: () => void;
};

const ITEM_TYPES: { value: MenuItemType; label: string; description: string }[] = [
  { value: "group", label: "Group", description: "A header that contains child items." },
  { value: "template", label: "Template", description: "Mounts a built-in page template at a URL. Choose the template — its component renders at the chosen path." },
  { value: "route", label: "Route", description: "Navigates to an existing route in the app." },
  { value: "page", label: "Page", description: "Defines a new route with custom HTML/JSX content." },
  { value: "link", label: "Link", description: "Opens an external URL." },
  { value: "action", label: "Action", description: "Triggers a built-in action (e.g. logout)." },
  { value: "separator", label: "Separator", description: "A divider line between menu items (vertical menus only)." }
];

const DYNAMIC_CHILDREN_OPTIONS = [
  { value: "", label: "(none)" },
  { value: "recordTypes", label: "Record Types — list each active record type" }
];

const ACTION_OPTIONS = [{ value: "logout", label: "Logout (POST /account/logout)" }];

export default function MenuItemEditModal({ item, onSave, onCancel }: Props) {
  const [draft, setDraft] = useState<MenuItem>(item);
  const [iconQuery, setIconQuery] = useState<string>(() => extractIconName(item.icon));
  const { data: pageTemplates = [] } = usePageTemplates();
  const { data: pages = [] } = usePages();

  useEffect(() => {
    setDraft(item);
    setIconQuery(extractIconName(item.icon));
  }, [item]);

  const handleIconChange = (next: string) => {
    setIconQuery(next);
    if (!next.trim()) {
      setDraft((d) => ({ ...d, icon: null }));
      return;
    }
    const found = findIcon(next);
    if (found) {
      setDraft((d) => ({ ...d, icon: `fa fa-${found.name}` }));
    } else {
      setDraft((d) => ({ ...d, icon: null }));
    }
  };

  const config = (draft.config ?? {}) as Record<string, unknown>;
  const setConfigField = (key: string, value: unknown) =>
    setDraft((d) => ({ ...d, config: { ...((d.config ?? {}) as Record<string, unknown>), [key]: value } }));

  const cfgStr = (key: string) =>
    typeof config[key] === "string" ? (config[key] as string).trim() : "";

  // All validation lives here. Each entry is a field key (matching the input)
  // mapped to the human-readable error. Submit is blocked while non-empty,
  // and the rendered inputs read this map to apply Bootstrap is-invalid +
  // invalid-feedback styling.
  const errors = useMemo(() => {
    const e: Record<string, string> = {};

    if (draft.itemType !== "separator" && !draft.displayName.trim()) {
      e.displayName = "Required.";
    }

    const checkPathFormat = (key: string, value: string) => {
      if (!value.startsWith("/")) {
        e[key] = "Must start with /.";
        return false;
      }
      return true;
    };

    const checkPathDoesNotShadowOrCollide = (key: string, value: string) => {
      const collide = findCollidingAppRoute(value);
      if (collide) {
        e[key] = `Conflicts with built-in route ${collide}.`;
        return;
      }
      const dup = pages.find((p) => p.path === value && p.id !== draft.id);
      if (dup) {
        e[key] = "Another page already uses this path.";
      }
    };

    if (draft.itemType === "route") {
      const p = cfgStr("path");
      if (!p) e.path = "Required.";
      else checkPathFormat("path", p);
      const a = cfgStr("aliasPath");
      if (a && !a.startsWith("/")) e.aliasPath = "Must start with /.";
    }

    if (draft.itemType === "page") {
      const p = cfgStr("path");
      if (!p) e.path = "Required.";
      else if (checkPathFormat("path", p)) checkPathDoesNotShadowOrCollide("path", p);
      if (!cfgStr("content")) e.content = "Required.";
    }

    if (draft.itemType === "template") {
      if (!cfgStr("templateKey")) e.templateKey = "Required.";
      const p = cfgStr("path");
      if (!p) e.path = "Required.";
      else if (checkPathFormat("path", p)) checkPathDoesNotShadowOrCollide("path", p);
    }

    if (draft.itemType === "link") {
      const h = cfgStr("href");
      if (!h) e.href = "Required.";
      else {
        try {
          new URL(h);
        } catch {
          e.href = "Must be a valid absolute URL (e.g. https://example.com).";
        }
      }
    }

    return e;
    // cfgStr closes over `config`, which is derived from draft.config; depending
    // on draft alone is sufficient to catch every relevant change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft, pages]);

  const hasErrors = Object.keys(errors).length > 0;
  const isPage = draft.itemType === "page";
  const isSeparator = draft.itemType === "separator";
  // Page type uses a flex column layout so the content textarea can grow to
  // fill the dialog. Other types stack form fields normally and let the body
  // scroll if needed.
  const topColClass = isPage ? "col-md-3" : "col-md-6";

  const persist = (options?: { keepOpen?: boolean }) => {
    if (hasErrors) return;

    const trimmedConfig: Record<string, unknown> = { ...config };
    for (const key of ["path", "content", "href", "aliasPath", "templateKey"]) {
      if (typeof trimmedConfig[key] === "string") {
        trimmedConfig[key] = (trimmedConfig[key] as string).trim();
      }
    }

    onSave(
      {
        ...draft,
        displayName: draft.displayName.trim(),
        config: trimmedConfig
      },
      options
    );
  };

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    // For the Page type the form's implicit submit (Enter inside an input)
    // saves without closing so authors keep editing; the explicit "Save and
    // close" button passes keepOpen: false.
    persist(isPage ? { keepOpen: true } : undefined);
  };

  return (
    <div
      className="modal show d-block"
      tabIndex={-1}
      style={{ background: "rgba(0,0,0,0.5)" }}
    >
      <div
        className="modal-dialog"
        style={{
          maxWidth: "none",
          width: "calc(100vw - 30px)",
          height: "calc(100vh - 30px)",
          margin: "15px"
        }}
      >
        <form
          className="modal-content"
          onSubmit={submit}
          noValidate
          style={{ height: "100%", display: "flex", flexDirection: "column" }}
        >
          <div className="modal-header" style={{ flex: "0 0 auto" }}>
            <h5 className="modal-title">Edit menu item</h5>
            <button type="button" className="btn-close" onClick={onCancel} aria-label="Close" />
          </div>

          <div
            className="modal-body"
            style={{
              flex: 1,
              overflowY: isPage ? "hidden" : "auto",
              display: isPage ? "flex" : undefined,
              flexDirection: isPage ? "column" : undefined,
              gap: isPage ? "0.75rem" : undefined,
              minHeight: 0
            }}
          >
            <div
              className="row g-3"
              style={isPage ? { flex: "0 0 auto", margin: 0 } : undefined}
            >
              <div className={isSeparator ? "col-12" : topColClass}>
                <label className="form-label">Item type</label>
                <select
                  className="form-select"
                  value={draft.itemType}
                  onChange={(e) => setDraft({ ...draft, itemType: e.target.value as MenuItemType })}
                >
                  {ITEM_TYPES.map((t) => (
                    <option key={t.value} value={t.value}>
                      {t.label}
                    </option>
                  ))}
                </select>
                {!isPage && (
                  <small className="text-muted">
                    {ITEM_TYPES.find((t) => t.value === draft.itemType)?.description}
                  </small>
                )}
              </div>

              {!isSeparator && (
                <div className={topColClass}>
                  <label className="form-label">
                    Display name <span className="text-danger">*</span>
                  </label>
                  <input
                    className={`form-control${errors.displayName ? " is-invalid" : ""}`}
                    value={draft.displayName}
                    onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
                    required
                  />
                  {errors.displayName && (
                    <div className="invalid-feedback">{errors.displayName}</div>
                  )}
                </div>
              )}
              {!isSeparator && (
                <div className={topColClass}>
                  <label className="form-label">Icon</label>
                  <IconPicker
                    value={iconQuery}
                    onChange={handleIconChange}
                    placeholder="Search icons (e.g. star)"
                  />
                </div>
              )}

              {!isSeparator && (
              <div className={topColClass}>
                <label className="form-label">Permission required</label>
                <input
                  className="form-control font-monospace"
                  placeholder="kind.action (e.g. siteconfig.edit)"
                  value={draft.permissionRequired ?? ""}
                  onChange={(e) =>
                    setDraft({ ...draft, permissionRequired: e.target.value || null })
                  }
                />
                {!isPage && (
                  <small className="text-muted">
                    Optional — when set, the item is hidden from users without this permission.
                  </small>
                )}
              </div>
              )}

              {draft.itemType === "group" && (
                <>
                  <div className="col-12">
                    <label className="form-label">Dynamic children source</label>
                    <select
                      className="form-select"
                      value={String(config.dynamicChildren ?? "")}
                      onChange={(e) =>
                        setConfigField(
                          "dynamicChildren",
                          e.target.value === "" ? undefined : e.target.value
                        )
                      }
                    >
                      {DYNAMIC_CHILDREN_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                    <small className="text-muted">
                      Appends auto-generated children at runtime (e.g. one entry per record type).
                    </small>
                  </div>
                  <div className="col-12">
                    <div className="form-check form-switch">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="group-starts-expanded"
                        checked={Boolean(config.startsExpanded)}
                        onChange={(e) =>
                          setConfigField(
                            "startsExpanded",
                            e.target.checked ? true : undefined
                          )
                        }
                      />
                      <label className="form-check-label" htmlFor="group-starts-expanded">
                        Starts expanded
                      </label>
                    </div>
                    <small className="text-muted">
                      When checked, the group opens by default. Otherwise it starts collapsed.
                    </small>
                  </div>
                </>
              )}

              {draft.itemType === "route" && (
                <>
                  <div className="col-md-6">
                    <label className="form-label">
                      Route path (target) <span className="text-danger">*</span>
                    </label>
                    <input
                      className={`form-control font-monospace${errors.path ? " is-invalid" : ""}`}
                      placeholder="/records/CAR"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.target.value)}
                      required
                    />
                    {errors.path ? (
                      <div className="invalid-feedback">{errors.path}</div>
                    ) : (
                      <small className="text-muted">
                        An existing app route (e.g. <code>/admin/roles</code>).
                      </small>
                    )}
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">Alias URL (optional)</label>
                    <input
                      className={`form-control font-monospace${errors.aliasPath ? " is-invalid" : ""}`}
                      placeholder="/cars"
                      value={String(config.aliasPath ?? "")}
                      onChange={(e) =>
                        setConfigField(
                          "aliasPath",
                          e.target.value === "" ? undefined : e.target.value
                        )
                      }
                    />
                    {errors.aliasPath ? (
                      <div className="invalid-feedback">{errors.aliasPath}</div>
                    ) : (
                      <small className="text-muted">
                        If set, the menu links to this URL and renders the target route's
                        content underneath. Example: alias <code>/cars</code> shows the
                        same view as <code>/records/CAR</code>.
                      </small>
                    )}
                  </div>
                </>
              )}

              {draft.itemType === "link" && (
                <>
                  <div className="col-md-9">
                    <label className="form-label">
                      URL <span className="text-danger">*</span>
                    </label>
                    <input
                      type="url"
                      className={`form-control font-monospace${errors.href ? " is-invalid" : ""}`}
                      placeholder="https://example.com"
                      value={String(config.href ?? "")}
                      onChange={(e) => setConfigField("href", e.target.value)}
                      required
                    />
                    {errors.href && (
                      <div className="invalid-feedback">{errors.href}</div>
                    )}
                  </div>
                  <div className="col-md-3 d-flex align-items-end">
                    <div className="form-check form-switch">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="link-new-tab"
                        checked={Boolean(config.openInNewTab)}
                        onChange={(e) => setConfigField("openInNewTab", e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="link-new-tab">
                        Open in new tab
                      </label>
                    </div>
                  </div>
                </>
              )}

              {isPage && (
                <>
                  <div className="col-md-9">
                    <label className="form-label">
                      Page path <span className="text-danger">*</span>
                    </label>
                    <input
                      className={`form-control font-monospace${errors.path ? " is-invalid" : ""}`}
                      placeholder="/cars/gallery"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.target.value)}
                      required
                    />
                    {errors.path && (
                      <div className="invalid-feedback">{errors.path}</div>
                    )}
                  </div>
                  <div className="col-md-3">
                    <label className="form-label">Content type</label>
                    <select
                      className="form-select"
                      value={String(config.contentType ?? "html")}
                      onChange={(e) => setConfigField("contentType", e.target.value)}
                    >
                      <option value="html">HTML</option>
                      <option value="jsx">JSX (full React component)</option>
                    </select>
                  </div>
                </>
              )}

              {draft.itemType === "template" && (
                <>
                  <div className="col-md-6">
                    <label className="form-label">
                      Page template <span className="text-danger">*</span>
                    </label>
                    <select
                      className={`form-select${errors.templateKey ? " is-invalid" : ""}`}
                      value={String(config.templateKey ?? "")}
                      required
                      onChange={(e) => {
                        const nextKey = e.target.value;
                        setDraft((d) => {
                          const prev = (d.config ?? {}) as Record<string, unknown>;
                          const customPath = typeof prev.path === "string" ? (prev.path as string) : "";
                          const prevKey = typeof prev.templateKey === "string" ? (prev.templateKey as string) : "";
                          const prevDefault = pageTemplates.find((t) => t.key === prevKey)?.defaultPath ?? "";
                          const nextDefault = pageTemplates.find((t) => t.key === nextKey)?.defaultPath ?? "";
                          // Pre-fill the path with the new template's default unless the
                          // admin already set a custom path (i.e. one that didn't match
                          // the previous template's default). Pre-filling makes the
                          // required URL path field a one-click operation in the common
                          // case while still allowing overrides.
                          const next = { ...prev, templateKey: nextKey } as Record<string, unknown>;
                          if (!customPath || customPath === prevDefault) {
                            if (nextDefault) next.path = nextDefault;
                            else delete next.path;
                          }
                          return { ...d, config: next };
                        });
                      }}
                    >
                      <option value="">Select a template…</option>
                      {pageTemplates.map((t) => (
                        <option key={t.key} value={t.key}>
                          {t.name}
                        </option>
                      ))}
                    </select>
                    {errors.templateKey ? (
                      <div className="invalid-feedback">{errors.templateKey}</div>
                    ) : typeof config.templateKey === "string" && config.templateKey ? (
                      <small className="text-muted">
                        {pageTemplates.find((t) => t.key === config.templateKey)?.description ?? ""}
                      </small>
                    ) : (
                      <small className="text-muted">
                        Pick which built-in page template to mount on this menu item.
                      </small>
                    )}
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">
                      URL path <span className="text-danger">*</span>
                    </label>
                    <input
                      className={`form-control font-monospace${errors.path ? " is-invalid" : ""}`}
                      placeholder={
                        pageTemplates.find((t) => t.key === config.templateKey)?.defaultPath ?? "/path"
                      }
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.target.value)}
                      required
                    />
                    {errors.path ? (
                      <div className="invalid-feedback">{errors.path}</div>
                    ) : (
                      <small className="text-muted">
                        Pre-filled from the template's default path when you
                        select one — adjust here if you want a different URL.
                      </small>
                    )}
                  </div>
                </>
              )}

              {draft.itemType === "action" && (
                <div className="col-12">
                  <label className="form-label">Action</label>
                  <select
                    className="form-select"
                    value={String(config.action ?? "logout")}
                    onChange={(e) => setConfigField("action", e.target.value)}
                  >
                    {ACTION_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}

            </div>

            {isPage && (
              <div
                style={{
                  flex: 1,
                  display: "flex",
                  flexDirection: "column",
                  minHeight: 0
                }}
              >
                <label className="form-label">
                  Content <span className="text-danger">*</span>
                </label>
                <div
                  className={errors.content ? "is-invalid" : ""}
                  style={{
                    flex: 1,
                    minHeight: 0,
                    border: "1px solid var(--bs-border-color)",
                    borderRadius: "var(--bs-border-radius)",
                    overflow: "hidden"
                  }}
                >
                  <CodeMirror
                    value={String(config.content ?? "")}
                    onChange={(v) => setConfigField("content", v)}
                    height="100%"
                    style={{ height: "100%" }}
                    autoFocus={false}
                    placeholder={
                      String(config.contentType ?? "html") === "jsx"
                        ? "function Page() {\n" +
                          "  const [name, setName] = useState('');\n" +
                          "  return <div>Hello {name}</div>;\n" +
                          "}"
                        : '<div class="alert alert-info">Hello world</div>\n' +
                          "<script>console.log('runs on mount');</script>"
                    }
                    extensions={[
                      String(config.contentType ?? "html") === "jsx"
                        ? javascript({ jsx: true, typescript: true })
                        : html()
                    ]}
                    basicSetup={{
                      lineNumbers: true,
                      highlightActiveLineGutter: true,
                      highlightSpecialChars: true,
                      history: true,
                      foldGutter: true,
                      drawSelection: true,
                      dropCursor: true,
                      allowMultipleSelections: true,
                      indentOnInput: true,
                      syntaxHighlighting: true,
                      bracketMatching: true,
                      closeBrackets: true,
                      autocompletion: true,
                      rectangularSelection: true,
                      crosshairCursor: true,
                      highlightActiveLine: true,
                      highlightSelectionMatches: true,
                      closeBracketsKeymap: true,
                      defaultKeymap: true,
                      searchKeymap: true,
                      historyKeymap: true,
                      foldKeymap: true,
                      completionKeymap: true,
                      lintKeymap: true
                    }}
                  />
                </div>
                {errors.content && (
                  <div className="invalid-feedback d-block">{errors.content}</div>
                )}
              </div>
            )}

          </div>

          <div className="modal-footer" style={{ flex: "0 0 auto" }}>
            <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>
              Cancel
            </button>
            {isPage ? (
              <>
                <button
                  type="button"
                  className="btn btn-outline-primary"
                  disabled={hasErrors}
                  onClick={() => persist({ keepOpen: true })}
                >
                  Save
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={hasErrors}
                  onClick={() => persist()}
                >
                  Save and Close
                </button>
              </>
            ) : (
              <button type="submit" className="btn btn-primary" disabled={hasErrors}>
                Apply
              </button>
            )}
          </div>
        </form>
      </div>
    </div>
  );
}
