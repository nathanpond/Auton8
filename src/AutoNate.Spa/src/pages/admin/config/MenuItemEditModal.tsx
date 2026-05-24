import { useEffect, useMemo, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { html } from "@codemirror/lang-html";
import { javascript } from "@codemirror/lang-javascript";
import {
  Anchor,
  Box,
  Button,
  Grid,
  Group,
  Input,
  Modal,
  Select,
  Switch,
  Text,
  Textarea,
  TextInput
} from "@mantine/core";
import { MenuItem, MenuItemType } from "@/types/menus";
import IconPicker from "@/components/IconPicker";
import { findIcon } from "@/lib/faIcons";
import { usePageTemplates } from "@/hooks/usePageTemplates";
import { usePages } from "@/hooks/usePages";
import { anchorPathForTemplateKey, findCollidingAppRoute } from "@/routes/appRoutes";
import TemplatePickerModal from "./TemplatePickerModal";
// Same modal chrome as the generic CardPicker (the .tp-* class prefix is
// "template picker" — the picker started here and got generalised; the CSS
// lives next to the generic component now).
import "@/components/picker/CardPickerModal.css";

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

const MONOSPACE = { fontFamily: "var(--mantine-font-family-monospace)" } as const;

export default function MenuItemEditModal({ item, onSave, onCancel }: Props) {
  const [draft, setDraft] = useState<MenuItem>(item);
  const [iconQuery, setIconQuery] = useState<string>(() => extractIconName(item.icon));
  const [pickerOpen, setPickerOpen] = useState(false);
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

    const checkPathDoesNotShadowOrCollide = (
      key: string,
      value: string,
      opts?: { templateKey?: string }
    ) => {
      const collide = findCollidingAppRoute(value, opts);
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
      const templateKey = cfgStr("templateKey");
      if (!templateKey) e.templateKey = "Required.";
      const p = cfgStr("path");
      if (!p) e.path = "Required.";
      else if (checkPathFormat("path", p))
        checkPathDoesNotShadowOrCollide("path", p, { templateKey });
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft, pages]);

  const hasErrors = Object.keys(errors).length > 0;
  const isPage = draft.itemType === "page";
  const isSeparator = draft.itemType === "separator";
  const topColSpan = isPage ? { base: 12, md: 3 } : { base: 12, md: 6 };

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
    persist(isPage ? { keepOpen: true } : undefined);
  };

  const applyTemplatePick = (templateKey: string) => {
    // If the new template is hard-routed at a canonical URL in APP_ROUTES
    // (e.g. configFeatures → /admin/config/features), snap the path field to
    // that URL — leaving the OLD template's anchor path here would make the
    // built-in static route render the OLD template, ignoring the new
    // templateKey. Only auto-overwrite when the current path is the previous
    // template's canonical anchor (or empty); admins who deliberately mounted
    // the template at a non-canonical path keep their value.
    setDraft((d) => {
      const prev = (d.config ?? {}) as Record<string, unknown>;
      const currentPath = typeof prev.path === "string" ? prev.path.trim() : "";
      const prevTemplateKey = typeof prev.templateKey === "string" ? prev.templateKey : "";
      const newAnchor = anchorPathForTemplateKey(templateKey);
      const prevAnchor = prevTemplateKey ? anchorPathForTemplateKey(prevTemplateKey) : null;
      const safeToOverwrite = currentPath === "" || currentPath === prevAnchor;
      const nextPath = safeToOverwrite && newAnchor ? newAnchor : currentPath;
      return {
        ...d,
        config: { ...prev, templateKey, path: nextPath }
      };
    });
  };

  return (
    <>
      <Modal
        opened
        onClose={onCancel}
        title="Edit menu item"
        fullScreen
        styles={{ body: { display: "flex", flexDirection: "column", minHeight: 0 } }}
      >
        <Box
          component="form"
          onSubmit={submit}
          noValidate
          style={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}
        >
          <Box
            style={{
              flex: 1,
              overflowY: isPage ? "hidden" : "auto",
              display: isPage ? "flex" : undefined,
              flexDirection: isPage ? "column" : undefined,
              gap: isPage ? "0.75rem" : undefined,
              minHeight: 0
            }}
          >
            <Grid style={isPage ? { flex: "0 0 auto", margin: 0 } : undefined}>
              <Grid.Col span={isSeparator ? 12 : topColSpan}>
                <Select
                  label="Item type"
                  value={draft.itemType}
                  onChange={(v) => v && setDraft({ ...draft, itemType: v as MenuItemType })}
                  data={ITEM_TYPES.map((t) => ({ value: t.value, label: t.label }))}
                  allowDeselect={false}
                  description={
                    !isPage
                      ? ITEM_TYPES.find((t) => t.value === draft.itemType)?.description
                      : undefined
                  }
                />
              </Grid.Col>

              {!isSeparator && (
                <Grid.Col span={topColSpan}>
                  <TextInput
                    label="Display name"
                    required
                    value={draft.displayName}
                    onChange={(e) => setDraft({ ...draft, displayName: e.currentTarget.value })}
                    error={errors.displayName}
                  />
                </Grid.Col>
              )}
              {!isSeparator && (
                <Grid.Col span={topColSpan}>
                  <Input.Wrapper label="Icon">
                    <IconPicker
                      value={iconQuery}
                      onChange={handleIconChange}
                      placeholder="Search icons (e.g. star)"
                    />
                  </Input.Wrapper>
                </Grid.Col>
              )}

              {!isSeparator && (
                <Grid.Col span={topColSpan}>
                  <TextInput
                    label="Permission required"
                    placeholder="kind.action (e.g. siteconfig.edit)"
                    value={draft.permissionRequired ?? ""}
                    onChange={(e) =>
                      setDraft({ ...draft, permissionRequired: e.currentTarget.value || null })
                    }
                    styles={{ input: MONOSPACE }}
                    description={
                      !isPage
                        ? "Optional — when set, the item is hidden from users without this permission."
                        : undefined
                    }
                  />
                </Grid.Col>
              )}

              {draft.itemType === "group" && (
                <>
                  <Grid.Col span={12}>
                    <Select
                      label="Dynamic children source"
                      value={String(config.dynamicChildren ?? "")}
                      onChange={(v) =>
                        setConfigField("dynamicChildren", v === "" || v === null ? undefined : v)
                      }
                      data={DYNAMIC_CHILDREN_OPTIONS}
                      description="Appends auto-generated children at runtime (e.g. one entry per record type)."
                      allowDeselect={false}
                    />
                  </Grid.Col>
                  <Grid.Col span={12}>
                    <Switch
                      id="group-starts-expanded"
                      label="Starts expanded"
                      checked={Boolean(config.startsExpanded)}
                      onChange={(e) =>
                        setConfigField(
                          "startsExpanded",
                          e.currentTarget.checked ? true : undefined
                        )
                      }
                      description="When checked, the group opens by default. Otherwise it starts collapsed."
                    />
                  </Grid.Col>
                </>
              )}

              {draft.itemType === "route" && (
                <>
                  <Grid.Col span={{ base: 12, md: 6 }}>
                    <TextInput
                      label="Route path (target)"
                      required
                      placeholder="/records/CAR"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.currentTarget.value)}
                      styles={{ input: MONOSPACE }}
                      error={errors.path}
                      description={!errors.path ? "An existing app route (e.g. /admin/roles)." : undefined}
                    />
                  </Grid.Col>
                  <Grid.Col span={{ base: 12, md: 6 }}>
                    <TextInput
                      label="Alias URL (optional)"
                      placeholder="/cars"
                      value={String(config.aliasPath ?? "")}
                      onChange={(e) =>
                        setConfigField(
                          "aliasPath",
                          e.currentTarget.value === "" ? undefined : e.currentTarget.value
                        )
                      }
                      styles={{ input: MONOSPACE }}
                      error={errors.aliasPath}
                      description={
                        !errors.aliasPath
                          ? "If set, the menu links to this URL and renders the target route's content underneath."
                          : undefined
                      }
                    />
                  </Grid.Col>
                </>
              )}

              {draft.itemType === "link" && (
                <>
                  <Grid.Col span={{ base: 12, md: 9 }}>
                    <TextInput
                      label="URL"
                      required
                      type="url"
                      placeholder="https://example.com"
                      value={String(config.href ?? "")}
                      onChange={(e) => setConfigField("href", e.currentTarget.value)}
                      styles={{ input: MONOSPACE }}
                      error={errors.href}
                    />
                  </Grid.Col>
                  <Grid.Col span={{ base: 12, md: 3 }} style={{ display: "flex", alignItems: "flex-end" }}>
                    <Switch
                      id="link-new-tab"
                      label="Open in new tab"
                      checked={Boolean(config.openInNewTab)}
                      onChange={(e) => setConfigField("openInNewTab", e.currentTarget.checked)}
                    />
                  </Grid.Col>
                </>
              )}

              {isPage && (
                <>
                  <Grid.Col span={{ base: 12, md: 9 }}>
                    <TextInput
                      label="Page path"
                      required
                      placeholder="/cars/gallery"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.currentTarget.value)}
                      styles={{ input: MONOSPACE }}
                      error={errors.path}
                    />
                  </Grid.Col>
                  <Grid.Col span={{ base: 12, md: 3 }}>
                    <Select
                      label="Content type"
                      value={String(config.contentType ?? "html")}
                      onChange={(v) => v && setConfigField("contentType", v)}
                      data={[
                        { value: "html", label: "HTML" },
                        { value: "jsx", label: "JSX (full React component)" }
                      ]}
                      allowDeselect={false}
                    />
                  </Grid.Col>
                </>
              )}

              {draft.itemType === "template" && (() => {
                const selected =
                  typeof config.templateKey === "string" && config.templateKey
                    ? pageTemplates.find((t) => t.key === config.templateKey) ?? null
                    : null;
                return (
                  <>
                    <Grid.Col span={{ base: 12, md: 6 }}>
                      <Input.Wrapper label="Page template" required error={errors.templateKey}>
                        {selected ? (
                          <Box
                            className={`tp-selected-card-condensed${
                              errors.templateKey ? " border-danger" : ""
                            }`}
                          >
                            <Box className="tp-selected-condensed-main">
                              <Text component="span" className="tp-selected-title">
                                {selected.name}
                              </Text>
                              {selected.category && (
                                <Text component="span" className="tp-pill">
                                  {selected.category}
                                </Text>
                              )}
                            </Box>
                            <Anchor
                              component="button"
                              type="button"
                              size="sm"
                              ml="auto"
                              onClick={() => setPickerOpen(true)}
                            >
                              Change…
                            </Anchor>
                          </Box>
                        ) : (
                          <Button
                            type="button"
                            variant="default"
                            fullWidth
                            leftSection={<i className="fa fa-th-large" />}
                            onClick={() => setPickerOpen(true)}
                          >
                            Choose a page template…
                          </Button>
                        )}
                        {!errors.templateKey && selected?.description && (
                          <Text size="xs" c="dimmed" mt={4}>
                            {selected.description}
                          </Text>
                        )}
                      </Input.Wrapper>
                    </Grid.Col>
                    <Grid.Col span={{ base: 12, md: 6 }}>
                      <TextInput
                        label="URL path"
                        required
                        placeholder="/path"
                        value={String(config.path ?? "")}
                        onChange={(e) => setConfigField("path", e.currentTarget.value)}
                        styles={{ input: MONOSPACE }}
                        error={errors.path}
                        description={
                          !errors.path
                            ? "The URL where this template will be mounted on this menu item."
                            : undefined
                        }
                      />
                    </Grid.Col>
                    {selected?.key === "dashboard" && (
                      <DashboardTemplateExtras
                        config={config}
                        setConfigField={setConfigField}
                      />
                    )}
                  </>
                );
              })()}

              {draft.itemType === "action" && (
                <Grid.Col span={12}>
                  <Select
                    label="Action"
                    value={String(config.action ?? "logout")}
                    onChange={(v) => v && setConfigField("action", v)}
                    data={ACTION_OPTIONS}
                    allowDeselect={false}
                  />
                </Grid.Col>
              )}
            </Grid>

            {isPage && (
              <Box
                style={{
                  flex: 1,
                  display: "flex",
                  flexDirection: "column",
                  minHeight: 0
                }}
              >
                <Input.Wrapper label="Content" required error={errors.content}>
                  <Box
                    style={{
                      flex: 1,
                      minHeight: 430,
                      border: errors.content
                        ? "1px solid var(--mantine-color-red-filled)"
                        : "1px solid var(--mantine-color-default-border)",
                      borderRadius: "var(--mantine-radius-default)",
                      overflow: "hidden",
                      display: "flex",
                      flexDirection: "column"
                    }}
                  >
                    <CodeMirror
                      value={String(config.content ?? "")}
                      onChange={(v) => setConfigField("content", v)}
                      height="100%"
                      minHeight="430px"
                      style={{ height: "100%", flex: 1 }}
                      autoFocus={false}
                      placeholder={
                        String(config.contentType ?? "html") === "jsx"
                          ? "function Page() {\n  const [name, setName] = useState('');\n  return <div>Hello {name}</div>;\n}"
                          : '<div class="alert alert-info">Hello world</div>\n<script>console.log(\'runs on mount\');</script>'
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
                  </Box>
                </Input.Wrapper>
              </Box>
            )}
          </Box>

          <Group justify="flex-end" gap="xs" mt="md">
            <Button variant="default" onClick={onCancel}>
              Cancel
            </Button>
            {isPage ? (
              <>
                <Button
                  variant="outline"
                  disabled={hasErrors}
                  onClick={() => persist({ keepOpen: true })}
                >
                  Save
                </Button>
                <Button disabled={hasErrors} onClick={() => persist()}>
                  Save and Close
                </Button>
              </>
            ) : (
              <Button type="submit" disabled={hasErrors}>
                Apply
              </Button>
            )}
          </Group>
        </Box>
      </Modal>
      {pickerOpen && (
        <TemplatePickerModal
          templates={pageTemplates}
          selectedKey={typeof config.templateKey === "string" ? config.templateKey : null}
          onSelect={(template) => {
            applyTemplatePick(template.key);
            setPickerOpen(false);
          }}
          onCancel={() => setPickerOpen(false)}
        />
      )}
    </>
  );
}

// Per-mount extras shown when the dashboard template is selected. The
// Switch flips `isUserConfigurable` (default true); when off, the textarea
// lets the admin paste a defaultLayout.widgets JSON array that the locked
// view renders read-only. A richer mini-canvas editor is a future
// iteration — v1 keeps it as JSON so admins can configure the locked
// layout without us shipping a second canvas.
function DashboardTemplateExtras({
  config,
  setConfigField
}: {
  config: Record<string, unknown>;
  setConfigField: (key: string, value: unknown) => void;
}) {
  const rawValue = config.isUserConfigurable;
  const isUserConfigurable = rawValue === undefined ? true : Boolean(rawValue);
  const defaultLayout = config.defaultLayout;
  const [layoutText, setLayoutText] = useState(() =>
    defaultLayout ? JSON.stringify(defaultLayout, null, 2) : ""
  );
  const [layoutError, setLayoutError] = useState<string | null>(null);

  // Keep the textarea in sync if config flips externally (e.g. picker
  // re-selects the template).
  useEffect(() => {
    setLayoutText(defaultLayout ? JSON.stringify(defaultLayout, null, 2) : "");
    setLayoutError(null);
  }, [defaultLayout]);

  const commitLayout = (text: string) => {
    setLayoutText(text);
    if (!text.trim()) {
      setLayoutError(null);
      setConfigField("defaultLayout", undefined);
      return;
    }
    try {
      const parsed = JSON.parse(text);
      setLayoutError(null);
      setConfigField("defaultLayout", parsed);
    } catch (e) {
      setLayoutError((e as Error).message);
    }
  };

  return (
    <>
      <Grid.Col span={12}>
        <Switch
          label="User-configurable"
          description={
            isUserConfigurable
              ? "Each viewer manages their own dashboards on this mount."
              : "Viewers see only the locked default layout below — no selector, no editing."
          }
          checked={isUserConfigurable}
          onChange={(e) =>
            setConfigField(
              "isUserConfigurable",
              e.currentTarget.checked ? undefined : false
            )
          }
        />
      </Grid.Col>
      {!isUserConfigurable && (
        <Grid.Col span={12}>
          <Textarea
            label="Default layout (JSON)"
            description={`Shape: { "widgets": [{ "widgetType": "data-table", "title": "...", "config": {...}, "gridX": 0, "gridY": 0, "gridW": 6, "gridH": 4 }] }`}
            placeholder='{ "widgets": [] }'
            value={layoutText}
            onChange={(e) => commitLayout(e.currentTarget.value)}
            error={layoutError}
            autosize
            minRows={4}
            maxRows={14}
            styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
          />
        </Grid.Col>
      )}
    </>
  );
}
