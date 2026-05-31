import { ReactNode, useMemo } from "react";
import { Link, NavLink, useLocation } from "react-router-dom";
import { Avatar, Box, Code, Group, Menu, UnstyledButton } from "@mantine/core";
import {
  HEADER_ACTIVE_BG,
  HEADER_ACTIVE_FG,
  HEADER_BG,
  HEADER_FG,
  HEADER_HOVER_BG,
  applyHeaderHover,
  clearHeaderHover,
  headerIconButtonStyle
} from "@/shell/headerStyles";
import { useMe } from "@/hooks/useMe";
import { usePublicMenu } from "@/hooks/useMenus";
import { usePageTemplates } from "@/hooks/usePageTemplates";
import { useNotificationLiveUpdates } from "@/hooks/useNotifications";
import { SITE_SETTING_KEYS, usePublicSiteSettings } from "@/hooks/useSiteSettings";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { MenuItem } from "@/types/menus";
import { PageTemplateInfo } from "@/api/pageTemplates";
import { resolveItemPath } from "@/menus/resolveItemPath";
import { reportMenuRenderFailure } from "@/api/systemIssues";
import SiteBrand from "@/components/SiteBrand";
import NotificationBell from "@/components/notifications/NotificationBell";
import AgentChatTrigger from "@/agent/AgentChatTrigger";
import { useUserPreferences } from "@/preferences/UserPreferencesContext";

// ---------------------------------------------------------------------------
// Menu link `href` trust model (acceptable risk)
// ---------------------------------------------------------------------------
// Every `item.itemType === "link"` render site in this file passes
// `item.config.href` to an `<a href>` without a local-URL check. That is
// intentional: admins use the Manage Menus UI to wire up external
// destinations (Confluence pages, Grafana dashboards, Slack invite links,
// vendor portals) and a local-URL gate would break the feature.
//
// Threat model: the only writer is a user with Menu.Edit permission. A
// compromised admin account can already grant roles, edit forms, install
// plugins, and create dynamic pages — pointing a menu item at a phishing
// site is strictly smaller. The risk is accepted; do not add a local-URL
// validator without first removing the external-link product capability.
// (See /audit security 2026-05-23 for the audit that ratified this.)
// ---------------------------------------------------------------------------

// Tiny helper used by the silent-drop sites in IconMenuItem / DropdownEntry /
// UserDropdownEntry. Reporting at the moment we drop the item is what makes
// "the SPA can't render this" surface as a System Issue even when the row
// was modified directly in the DB after the app started — no detector tick,
// no operator save, just the SPA noticing at render time.
function dropAndReport(itemId: string): null {
  reportMenuRenderFailure(itemId);
  return null;
}

type RecordTypeChild = { key: string; path: string; label: string; shortCode: string };

export default function NavMenu() {
  const location = useLocation();
  const { data: me } = useMe();
  const { data: mainMenu } = usePublicMenu("main");
  const { data: iconMenu } = usePublicMenu("icon");
  const { data: userMenu } = usePublicMenu("user");
  const { data: recordTypes = [] } = useRecordTypes(false);
  const { data: pageTemplates } = usePageTemplates();
  const { effectiveAppearance } = useSiteAppearance();
  const { openModal: openPreferences } = useUserPreferences();

  const publicSettings = usePublicSiteSettings();
  const notificationsEnabled = publicSettings.getBool(
    SITE_SETTING_KEYS.notificationsHeaderEnabled
  );

  // Subscribe once at the shell level so notification badge stays current
  // whatever page the user is on. Skip when notifications are admin-disabled
  // so we don't open a websocket for a feature the user can't see.
  useNotificationLiveUpdates(me?.authenticated === true && notificationsEnabled);

  const recordTypeChildren: RecordTypeChild[] = useMemo(
    () =>
      recordTypes
        .filter((t) => !t.isArchived)
        .map((t) => ({
          key: t.id,
          path: `/records/${t.shortCode}`,
          label: t.name,
          shortCode: t.shortCode
        })),
    [recordTypes]
  );

  const fullPath = location.pathname;

  const displayName = useMemo(() => {
    if (!me || me.authenticated !== true) return "Adam Schwartz";
    const full = `${me.firstName ?? ""} ${me.lastName ?? ""}`.trim();
    return full || "Adam Schwartz";
  }, [me]);

  const isActiveLeaf = (item: MenuItem): boolean => {
    const path = pathOf(item, pageTemplates);
    if (path && fullPath === path) return true;
    return (item.children ?? []).some(isActiveLeaf);
  };

  return (
    <Box
      id="top-menu"
      h="100%"
      bg={HEADER_BG}
      c={HEADER_FG}
      px="md"
      style={{
        display: "flex",
        alignItems: "stretch",
        borderBottom: "1px solid rgba(255,255,255,0.08)"
      }}
    >
      <Box
        style={{
          flex: 1,
          display: "flex",
          alignItems: "center",
          overflowX: "auto",
          // Mantine's <ScrollArea> uses a buggy useMergedRef pattern that
          // infinite-loops on rapid parent re-renders (e.g. SiteAppearance
          // live preview). Plain overflow-x is enough for a horizontal
          // top bar; we don't need the styled scrollbars.
          scrollbarWidth: "thin"
        }}
      >
        <Group gap={4} wrap="nowrap" align="center" h="100%" w="100%">
          <UnstyledButton
            component={Link}
            to="/home"
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 8,
              padding: "0 12px",
              height: 40,
              color: HEADER_ACTIVE_FG,
              fontWeight: 600,
              textDecoration: "none"
            }}
          >
            <SiteBrand
              appearance={effectiveAppearance}
              style={{ display: "inline-flex", alignItems: "center", gap: 8 }}
              iconClassName=""
              textClassName=""
              imageClassName=""
            />
          </UnstyledButton>

          {(mainMenu?.items ?? []).map((item) => (
            <TopItem
              key={item.id}
              item={item}
              fullPath={fullPath}
              recordTypeChildren={recordTypeChildren}
              isActiveLeaf={isActiveLeaf}
              templates={pageTemplates}
            />
          ))}

          <Box style={{ flex: 1 }} />

          {(iconMenu?.items ?? []).map((item) => (
            <IconMenuTopItem key={item.id} item={item} templates={pageTemplates} />
          ))}

          {me?.authenticated === true && notificationsEnabled && <NotificationBell />}

          {me?.authenticated === true && <AgentChatTrigger />}

          <UserMenu
            displayName={displayName}
            userMenu={userMenu?.items ?? []}
            templates={pageTemplates}
            onOpenPreferences={openPreferences}
          />
        </Group>
      </Box>
    </Box>
  );
}

// One main-menu top-level item. Group items become a Mantine dropdown; leaf
// items render as a single navigation link.
function TopItem({
  item,
  fullPath,
  recordTypeChildren,
  isActiveLeaf,
  templates
}: {
  item: MenuItem;
  fullPath: string;
  recordTypeChildren: RecordTypeChild[];
  isActiveLeaf: (item: MenuItem) => boolean;
  templates: PageTemplateInfo[] | undefined;
}) {
  // Top main nav is horizontal and ignores separators by design.
  if (item.itemType === "separator") return null;

  if (item.itemType === "group") {
    const dynamicChildren =
      item.config?.dynamicChildren === "recordTypes" ? recordTypeChildren : [];
    const groupActive =
      (item.children ?? []).some(isActiveLeaf) ||
      dynamicChildren.some((c) => fullPath === c.path);
    return (
      <Menu trigger="hover" position="bottom-start" openDelay={80} closeDelay={150} shadow="md">
        <Menu.Target>
          <NavButton active={groupActive}>
            {item.icon && (
              <i className={item.icon} aria-hidden style={{ marginRight: 8 }} />
            )}
            {item.displayName}
            <i className="fa fa-angle-down" aria-hidden style={{ marginLeft: 6, fontSize: 11 }} />
          </NavButton>
        </Menu.Target>
        <Menu.Dropdown>
          {(item.children ?? []).map((child) => (
            <SubmenuEntry
              key={child.id}
              item={child}
              fullPath={fullPath}
              templates={templates}
            />
          ))}
          {dynamicChildren.length > 0 && (item.children ?? []).length > 0 && <Menu.Divider />}
          {dynamicChildren.map((c) => (
            <Menu.Item
              key={c.key}
              component={NavLink}
              to={c.path}
              data-active={fullPath === c.path ? "true" : undefined}
            >
              <Group gap="xs" wrap="nowrap">
                <Code>{c.shortCode}</Code>
                <span>{c.label}</span>
              </Group>
            </Menu.Item>
          ))}
        </Menu.Dropdown>
      </Menu>
    );
  }

  // Top-level non-group items render as a single nav link.
  return <ItemAnchor item={item} active={isActiveLeaf(item)} templates={templates} />;
}

function SubmenuEntry({
  item,
  fullPath,
  templates
}: {
  item: MenuItem;
  fullPath: string;
  templates: PageTemplateInfo[] | undefined;
}) {
  if (item.itemType === "separator") {
    return <Menu.Divider />;
  }
  const path = pathOf(item, templates);
  const active = path != null && fullPath === path;
  if (item.itemType === "link") {
    // Admin-controlled href, intentionally not local-URL-gated; see the
    // "Menu link `href` trust model" comment near the top of this file.
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    if (!href) return reportUnconfigured(item, "missing href");
    return (
      <Menu.Item
        component="a"
        href={href}
        target={newTab ? "_blank" : undefined}
        rel={newTab ? "noopener noreferrer" : undefined}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  if (item.itemType === "route" || item.itemType === "page" || item.itemType === "template") {
    if (!path) return reportUnconfigured(item, "missing path");
    return (
      <Menu.Item
        component={NavLink}
        to={path}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
        data-active={active ? "true" : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  return reportUnconfigured(item, `unknown type '${item.itemType}'`);
}

function ItemAnchor({
  item,
  active,
  templates
}: {
  item: MenuItem;
  active: boolean;
  templates: PageTemplateInfo[] | undefined;
}) {
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    if (!href) {
      reportMenuRenderFailure(item.id);
      return (
        <NavButton active={false} title="Misconfigured: missing href">
          <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
          {item.displayName}
        </NavButton>
      );
    }
    return (
      <NavButton
        component="a"
        href={href}
        target={newTab ? "_blank" : undefined}
        rel={newTab ? "noopener noreferrer" : undefined}
        active={false}
      >
        {item.icon && <i className={item.icon} style={{ marginRight: 8 }} />}
        {item.displayName}
      </NavButton>
    );
  }
  if (item.itemType === "route" || item.itemType === "page" || item.itemType === "template") {
    const path = pathOf(item, templates);
    if (!path) {
      reportMenuRenderFailure(item.id);
      return (
        <NavButton active={false} title="Misconfigured: missing path">
          <i className="fa fa-triangle-exclamation" style={{ marginRight: 6 }} />
          {item.displayName}
        </NavButton>
      );
    }
    return (
      <NavButton component={NavLink} to={path} active={active}>
        {item.icon && <i className={item.icon} style={{ marginRight: 8 }} />}
        {item.displayName}
      </NavButton>
    );
  }
  return reportUnconfigured(item, `unknown type '${item.itemType}'`);
}

// Top-bar nav link styled like a header tab. Active state gets the
// SiteAppearance "topMenuLinkActive*" tokens via CSS vars.
type NavButtonProps = {
  active: boolean;
  children: ReactNode;
  component?: React.ElementType;
  title?: string;
  href?: string;
  to?: string;
  target?: string;
  rel?: string;
};

function NavButton({ active, children, component, ...rest }: NavButtonProps) {
  const style: React.CSSProperties = {
    display: "inline-flex",
    alignItems: "center",
    height: 40,
    padding: "0 12px",
    borderRadius: 4,
    color: active ? HEADER_ACTIVE_FG : HEADER_FG,
    background: active ? HEADER_ACTIVE_BG : "transparent",
    fontSize: 14,
    whiteSpace: "nowrap",
    textDecoration: "none",
    border: 0,
    cursor: "pointer",
    transition: "background 120ms ease, color 120ms ease"
  };
  const onMouseEnter = (e: React.MouseEvent<HTMLElement>) => {
    if (!active) e.currentTarget.style.background = HEADER_HOVER_BG;
  };
  const onMouseLeave = (e: React.MouseEvent<HTMLElement>) => {
    if (!active) e.currentTarget.style.background = "transparent";
  };
  const Component = (component ?? "button") as React.ElementType;
  return (
    <Component
      style={style}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      {...rest}
    >
      {children}
    </Component>
  );
}

// Renders one top-level item in the icon strip. Group items become an
// icon-only dropdown; route/page/link items become a single icon-only link.
// Separators are skipped at the top level (they only make sense inside
// dropdowns).
function IconMenuTopItem({
  item,
  templates
}: {
  item: MenuItem;
  templates: PageTemplateInfo[] | undefined;
}) {
  if (item.itemType === "separator") return null;

  if (item.itemType === "group") {
    const icon = item.icon ?? "fa fa-circle";
    return (
      <Menu position="bottom-end" shadow="md" zIndex={1100}>
        <Menu.Target>
          <UnstyledButton
            title={item.displayName}
            aria-label={item.displayName}
            style={headerIconButtonStyle}
            onMouseEnter={applyHeaderHover}
            onMouseLeave={clearHeaderHover}
          >
            <i className={icon} />
          </UnstyledButton>
        </Menu.Target>
        <Menu.Dropdown>
          {(item.children ?? []).map((child) => (
            <DropdownEntry key={child.id} item={child} templates={templates} />
          ))}
        </Menu.Dropdown>
      </Menu>
    );
  }

  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    if (!href) return dropAndReport(item.id);
    return (
      <UnstyledButton
        component="a"
        href={href}
        target={newTab ? "_blank" : undefined}
        rel={newTab ? "noopener noreferrer" : undefined}
        title={item.displayName}
        aria-label={item.displayName}
        style={headerIconButtonStyle}
        onMouseEnter={applyHeaderHover}
        onMouseLeave={clearHeaderHover}
      >
        <i className={item.icon ?? "fa fa-link"} />
      </UnstyledButton>
    );
  }
  if (item.itemType === "route" || item.itemType === "page" || item.itemType === "template") {
    const path = pathOf(item, templates);
    if (!path) return dropAndReport(item.id);
    return (
      <UnstyledButton
        component={NavLink}
        to={path}
        title={item.displayName}
        aria-label={item.displayName}
        style={headerIconButtonStyle}
        onMouseEnter={applyHeaderHover}
        onMouseLeave={clearHeaderHover}
      >
        <i className={item.icon ?? "fa fa-link"} />
      </UnstyledButton>
    );
  }
  return null;
}

function DropdownEntry({
  item,
  templates
}: {
  item: MenuItem;
  templates: PageTemplateInfo[] | undefined;
}) {
  if (item.itemType === "separator") {
    return <Menu.Divider />;
  }
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    return (
      <Menu.Item
        component="a"
        href={href ?? "#"}
        target={newTab ? "_blank" : undefined}
        rel={newTab ? "noopener noreferrer" : undefined}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  if (item.itemType === "route" || item.itemType === "page" || item.itemType === "template") {
    const path = pathOf(item, templates);
    if (!path) return dropAndReport(item.id);
    return (
      <Menu.Item
        component={NavLink}
        to={path}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  return null;
}

function UserMenu({
  displayName,
  userMenu,
  templates,
  onOpenPreferences
}: {
  displayName: string;
  userMenu: MenuItem[];
  templates: PageTemplateInfo[] | undefined;
  onOpenPreferences: () => void;
}) {
  return (
    <Menu position="bottom-end" shadow="md" width={220} zIndex={1100}>
      <Menu.Target>
        <UnstyledButton
          aria-label={`Open user menu for ${displayName}`}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: 8,
            padding: "0 12px",
            height: 40,
            color: HEADER_FG,
            background: "transparent",
            border: 0,
            borderRadius: 4,
            cursor: "pointer",
            transition: "background 120ms ease, color 120ms ease"
          }}
          onMouseEnter={applyHeaderHover}
          onMouseLeave={clearHeaderHover}
        >
          <Avatar size={28} radius="xl" color="gray">
            <i className="fa fa-user" aria-hidden="true" />
          </Avatar>
          <Box
            component="span"
            visibleFrom="md"
            style={{ fontSize: 14, lineHeight: 1, color: "inherit" }}
          >
            {displayName}
          </Box>
          <i className="fa fa-angle-down" aria-hidden="true" style={{ fontSize: 11, opacity: 0.7 }} />
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Item leftSection={<i className="fa fa-gear" />} onClick={onOpenPreferences}>
          User Preferences
        </Menu.Item>
        {userMenu.length > 0 && <Menu.Divider />}
        {userMenu.map((item) => (
          <UserDropdownEntry key={item.id} item={item} templates={templates} />
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}

function UserDropdownEntry({
  item,
  templates
}: {
  item: MenuItem;
  templates: PageTemplateInfo[] | undefined;
}) {
  if (item.itemType === "separator") {
    return <Menu.Divider />;
  }
  if (item.itemType === "action") {
    const action = stringFrom(item.config?.action);
    if (action === "logout") {
      return (
        <Menu.Item
          leftSection={item.icon ? <i className={item.icon} /> : undefined}
          onClick={() => {
            // Submit the existing form-based logout endpoint. POST is required;
            // synthesize a hidden form, submit, and discard.
            const form = document.createElement("form");
            form.method = "post";
            form.action = "/account/logout";
            document.body.appendChild(form);
            form.submit();
          }}
        >
          {item.displayName}
        </Menu.Item>
      );
    }
    return null;
  }
  if (item.itemType === "route" || item.itemType === "page" || item.itemType === "template") {
    const path = pathOf(item, templates);
    if (!path) return dropAndReport(item.id);
    return (
      <Menu.Item
        component={NavLink}
        to={path}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    return (
      <Menu.Item
        component="a"
        href={href ?? "#"}
        leftSection={item.icon ? <i className={item.icon} /> : undefined}
      >
        {item.displayName}
      </Menu.Item>
    );
  }
  return null;
}

function reportUnconfigured(item: MenuItem, why: string) {
  reportMenuRenderFailure(item.id);
  return (
    <Menu.Item
      disabled
      leftSection={<i className="fa fa-triangle-exclamation" />}
      title={`Misconfigured: ${why}`}
    >
      {item.displayName}
    </Menu.Item>
  );
}

// `templates` is no longer needed to compute a URL — kept on the signature so
// existing callers don't need to be touched. Templates do not carry default
// URLs; every template menu item owns its own config.path.
function pathOf(item: MenuItem, _templates: PageTemplateInfo[] | undefined): string | null {
  return resolveItemPath(item);
}

function stringFrom(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}
