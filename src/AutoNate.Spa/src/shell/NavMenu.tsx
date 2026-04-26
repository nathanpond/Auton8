import { useMemo } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { useMe } from "@/hooks/useMe";
import { usePublicMenu } from "@/hooks/useMenus";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";
import { useRecordTypes } from "@/hooks/useRecordTypes";
import { MenuItem } from "@/types/menus";
import SiteBrand from "@/components/SiteBrand";

type RecordTypeChild = { key: string; path: string; label: string; shortCode: string };

export default function NavMenu() {
  const location = useLocation();
  const { data: me } = useMe();
  const { data: mainMenu } = usePublicMenu("main");
  const { data: iconMenu } = usePublicMenu("icon");
  const { data: userMenu } = usePublicMenu("user");
  const { data: recordTypes = [] } = useRecordTypes(false);
  const { effectiveAppearance } = useSiteAppearance();

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

  const currentPath = useMemo(
    () => location.pathname.split("?")[0].replace(/^\/+|\/+$/g, ""),
    [location.pathname]
  );
  const fullPath = location.pathname;

  const displayName = useMemo(() => {
    if (!me || me.authenticated !== true) return "Adam Schwartz";
    const full = `${me.firstName ?? ""} ${me.lastName ?? ""}`.trim();
    return full || "Adam Schwartz";
  }, [me]);

  const isActiveLeaf = (item: MenuItem): boolean => {
    const path = pathOf(item);
    if (path && fullPath === path) return true;
    return (item.children ?? []).some(isActiveLeaf);
  };

  const renderTopItem = (item: MenuItem) => {
    // Top main nav is horizontal and ignores separators by design.
    if (item.itemType === "separator") return null;
    if (item.itemType === "group") {
      const dynamicChildren =
        item.config?.dynamicChildren === "recordTypes" ? recordTypeChildren : [];
      const groupActive =
        (item.children ?? []).some(isActiveLeaf) ||
        dynamicChildren.some((c) => fullPath === c.path);
      return (
        <div
          key={item.id}
          className={
            groupActive ? "menu-item has-sub active expand" : "menu-item has-sub"
          }
        >
          <a href="#" className="menu-link" onClick={preventDefault}>
            {item.icon && (
              <div className="menu-icon">
                <i className={item.icon}></i>
              </div>
            )}
            <div className="menu-text">{item.displayName}</div>
            <div className="menu-caret"></div>
          </a>
          <div className="menu-submenu">
            {(item.children ?? []).map(renderSubmenuItem)}
            {dynamicChildren.map((c) => (
              <div
                key={c.key}
                className={
                  fullPath === c.path ? "menu-item active" : "menu-item"
                }
              >
                <NavLink to={c.path} className="menu-link">
                  <div className="menu-text">
                    <code className="me-2">{c.shortCode}</code>
                    {c.label}
                  </div>
                </NavLink>
              </div>
            ))}
          </div>
        </div>
      );
    }
    // Top-level non-group items render as a single menu-item link.
    return (
      <div
        key={item.id}
        className={isActiveLeaf(item) ? "menu-item active" : "menu-item"}
      >
        {renderItemAnchor(item, "menu-link")}
      </div>
    );
  };

  const renderSubmenuItem = (item: MenuItem) => {
    // Inside a main-menu submenu (a dropdown), separators render as a thin
    // divider line between items.
    if (item.itemType === "separator") {
      return <div key={item.id} className="menu-item menu-submenu-separator" />;
    }
    const path = pathOf(item);
    return (
      <div
        key={item.id}
        className={path && fullPath === path ? "menu-item active" : "menu-item"}
      >
        {renderItemAnchor(item, "menu-link")}
      </div>
    );
  };

  const renderItemAnchor = (item: MenuItem, className: string) => {
    if (item.itemType === "link") {
      const href = stringFrom(item.config?.href);
      const newTab = Boolean(item.config?.openInNewTab);
      if (!href) return renderUnconfigured(item, className, "missing href");
      return (
        <a
          href={href}
          className={className}
          target={newTab ? "_blank" : undefined}
          rel={newTab ? "noopener noreferrer" : undefined}
        >
          {item.icon && (
            <div className="menu-icon">
              <i className={item.icon}></i>
            </div>
          )}
          <div className="menu-text">{item.displayName}</div>
        </a>
      );
    }
    if (item.itemType === "route" || item.itemType === "page") {
      const linkPath = pathOf(item);
      if (!linkPath) return renderUnconfigured(item, className, "missing path");
      return (
        <NavLink to={linkPath} className={className}>
          {item.icon && (
            <div className="menu-icon">
              <i className={item.icon}></i>
            </div>
          )}
          <div className="menu-text">{item.displayName}</div>
        </NavLink>
      );
    }
    return renderUnconfigured(item, className, `unknown type '${item.itemType}'`);
  };

  const renderUnconfigured = (item: MenuItem, className: string, why: string) => (
    <a
      href="#"
      className={className}
      title={`Misconfigured: ${why}`}
      onClick={preventDefault}
    >
      {item.icon ? (
        <div className="menu-icon">
          <i className={item.icon}></i>
        </div>
      ) : (
        <div className="menu-icon">
          <i className="fa fa-triangle-exclamation text-warning"></i>
        </div>
      )}
      <div className="menu-text">{item.displayName}</div>
    </a>
  );

  return (
    <div id="top-menu" className="app-top-menu" data-bs-theme="dark">
      <div className="menu">
        <div className="menu-item menu-brand-item">
          <NavLink to="/home" className="menu-link menu-brand-link">
            <SiteBrand
              appearance={effectiveAppearance}
              className="d-inline-flex align-items-center gap-2"
              iconClassName="menu-icon"
              textClassName="menu-text"
              imageClassName="menu-brand-image"
            />
          </NavLink>
        </div>

        {(mainMenu?.items ?? []).map(renderTopItem)}

        <div className="menu-item menu-control menu-control-start">
          <a href="#" className="menu-link" data-toggle="app-top-menu-prev" onClick={preventDefault}>
            <i className="fa fa-angle-left"></i>
          </a>
        </div>

        <div className="menu-item menu-control menu-control-end">
          <a href="#" className="menu-link" data-toggle="app-top-menu-next" onClick={preventDefault}>
            <i className="fa fa-angle-right"></i>
          </a>
        </div>

        {(iconMenu?.items ?? []).map((item, idx) => (
          <IconMenuTopItem
            key={item.id}
            item={item}
            isFirst={idx === 0}
            currentPath={currentPath}
          />
        ))}

        <div className="menu-item dropdown">
          <a
            href="#"
            className="menu-link dropdown-toggle d-flex align-items-center gap-2"
            data-bs-toggle="dropdown"
            aria-expanded="false"
            onClick={preventDefault}
          >
            <div className="image image-icon bg-gray-800 text-gray-600">
              <i className="fa fa-user"></i>
            </div>
            <span>
              <span className="d-none d-md-inline">{displayName}</span>
              <b className="caret"></b>
            </span>
          </a>
          <div className="dropdown-menu dropdown-menu-end me-1">
            {(userMenu?.items ?? []).map((item) => (
              <UserDropdownEntry key={item.id} item={item} />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// Renders one top-level item in the icon strip. Group items become an
// icon-only dropdown; route/page/link items become a single icon-only link.
// Separators are skipped at the top level (they only make sense inside
// dropdowns).
function IconMenuTopItem({
  item,
  isFirst,
  currentPath
}: {
  item: MenuItem;
  isFirst: boolean;
  currentPath: string;
}) {
  if (item.itemType === "separator") return null;

  const wrapperClass = isFirst ? "menu-item ms-auto" : "menu-item";

  if (item.itemType === "group") {
    const icon = item.icon ?? "fa fa-circle";
    return (
      <div className={`${wrapperClass} dropdown`}>
        <a
          href="#"
          className="menu-link dropdown-toggle d-flex align-items-center"
          data-bs-toggle="dropdown"
          aria-expanded="false"
          title={item.displayName}
          onClick={preventDefault}
        >
          <div className="menu-icon">
            <i className={icon}></i>
          </div>
        </a>
        <div className="dropdown-menu dropdown-menu-end me-1">
          {(item.children ?? []).map((child) => (
            <DropdownEntry key={child.id} item={child} currentPath={currentPath} />
          ))}
        </div>
      </div>
    );
  }

  // Single-icon link (no dropdown)
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    if (!href) return null;
    return (
      <div className={wrapperClass}>
        <a
          href={href}
          className="menu-link"
          title={item.displayName}
          target={newTab ? "_blank" : undefined}
          rel={newTab ? "noopener noreferrer" : undefined}
        >
          <div className="menu-icon">
            <i className={item.icon ?? "fa fa-link"}></i>
          </div>
        </a>
      </div>
    );
  }
  if (item.itemType === "route" || item.itemType === "page") {
    const path = pathOf(item);
    if (!path) return null;
    return (
      <div className={wrapperClass}>
        <NavLink to={path} className="menu-link" title={item.displayName}>
          <div className="menu-icon">
            <i className={item.icon ?? "fa fa-link"}></i>
          </div>
        </NavLink>
      </div>
    );
  }
  return null;
}

function DropdownEntry({ item, currentPath: _ }: { item: MenuItem; currentPath: string }) {
  // Inside an icon menu dropdown, separators render as a horizontal divider.
  if (item.itemType === "separator") {
    return <div className="dropdown-divider" />;
  }
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    const newTab = Boolean(item.config?.openInNewTab);
    return (
      <a
        href={href ?? "#"}
        className="dropdown-item"
        target={newTab ? "_blank" : undefined}
        rel={newTab ? "noopener noreferrer" : undefined}
      >
        {item.icon && <i className={`${item.icon} me-2`} />}
        {item.displayName}
      </a>
    );
  }
  if (item.itemType === "route" || item.itemType === "page") {
    const path = pathOf(item);
    if (!path) return null;
    return (
      <NavLink className="dropdown-item" to={path}>
        {item.icon && <i className={`${item.icon} me-2`} />}
        {item.displayName}
      </NavLink>
    );
  }
  return null;
}

function UserDropdownEntry({ item }: { item: MenuItem }) {
  if (item.itemType === "separator") {
    return <div className="dropdown-divider" />;
  }
  if (item.itemType === "action") {
    const action = stringFrom(item.config?.action);
    if (action === "logout") {
      return (
        <form action="/account/logout" method="post">
          <button type="submit" className="dropdown-item">
            {item.icon && <i className={`${item.icon} me-2`} />}
            {item.displayName}
          </button>
        </form>
      );
    }
    return null;
  }
  if (item.itemType === "route" || item.itemType === "page") {
    const path = pathOf(item);
    if (!path) return null;
    return (
      <NavLink className="dropdown-item" to={path}>
        {item.icon && <i className={`${item.icon} me-2`} />}
        {item.displayName}
      </NavLink>
    );
  }
  if (item.itemType === "link") {
    const href = stringFrom(item.config?.href);
    return (
      <a className="dropdown-item" href={href ?? "#"}>
        {item.icon && <i className={`${item.icon} me-2`} />}
        {item.displayName}
      </a>
    );
  }
  return null;
}

function pathOf(item: MenuItem): string | null {
  if (item.itemType === "route") {
    // Prefer alias URL when set so the menu/link points at the alias.
    return stringFrom(item.config?.aliasPath) ?? stringFrom(item.config?.path);
  }
  if (item.itemType === "page") {
    return stringFrom(item.config?.path);
  }
  return null;
}

function stringFrom(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

function preventDefault(e: React.MouseEvent<HTMLAnchorElement>) {
  e.preventDefault();
}
