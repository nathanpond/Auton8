import { useEffect, useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { usePublicMenu } from "@/hooks/useMenus";
import { MenuItem } from "@/types/menus";
import "./ConfigLayout.css";

export default function ConfigLayout() {
  const { data: menu } = usePublicMenu("site-config");
  const location = useLocation();
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (!menu) return;
    const items = menu.items ?? [];
    setOpenGroups((prev) => {
      const next = { ...prev };
      for (const group of items) {
        if (next[group.id] === undefined) {
          next[group.id] = containsActive(group, location.pathname);
        }
      }
      // Open the first group by default when nothing matches.
      if (items.length > 0 && !Object.values(next).some(Boolean)) {
        next[items[0].id] = true;
      }
      return next;
    });
  }, [menu, location.pathname]);

  const toggleGroup = (id: string) =>
    setOpenGroups((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <div className="config-layout">
      <aside className="config-sidenav">
        <div className="config-sidenav-header">
          <i className="fa fa-sliders me-2" />
          Site Configuration
        </div>
        <nav className="config-sidenav-nav">
          {(menu?.items ?? []).map((group) => {
            if (group.itemType === "separator") {
              return <hr key={group.id} className="config-nav-separator" />;
            }
            const isOpen = openGroups[group.id] ?? false;
            return (
              <div key={group.id} className={`config-nav-group ${isOpen ? "open" : ""}`}>
                <button
                  type="button"
                  className="config-nav-group-header"
                  onClick={() => toggleGroup(group.id)}
                  aria-expanded={isOpen}
                >
                  {group.icon && (
                    <i className={`${group.icon} config-nav-group-icon`} />
                  )}
                  <span className="config-nav-group-label">{group.displayName}</span>
                  <i
                    className={`fa fa-chevron-${isOpen ? "down" : "right"} config-nav-group-caret`}
                  />
                </button>
                {isOpen && (group.children ?? []).length > 0 && (
                  <ul className="config-nav-items">
                    {(group.children ?? []).map((item) =>
                      item.itemType === "separator" ? (
                        <li key={item.id}>
                          <hr className="config-nav-separator" />
                        </li>
                      ) : (
                        <ConfigLeaf key={item.id} item={item} />
                      )
                    )}
                  </ul>
                )}
              </div>
            );
          })}
        </nav>
      </aside>

      <main className="config-content">
        <Outlet />
      </main>
    </div>
  );
}

function ConfigLeaf({ item }: { item: MenuItem }) {
  const aliasPath =
    typeof item.config?.aliasPath === "string" ? (item.config.aliasPath as string) : null;
  const targetPath =
    typeof item.config?.path === "string" ? (item.config.path as string) : null;
  const path = aliasPath ?? targetPath;
  if (!path) return null;
  return (
    <li>
      <NavLink
        to={path}
        end
        className={({ isActive }) =>
          `config-nav-item ${isActive ? "active" : ""}`
        }
      >
        {item.icon && <i className={`${item.icon} config-nav-item-icon`} />}
        <span>{item.displayName}</span>
      </NavLink>
    </li>
  );
}

function containsActive(group: MenuItem, pathname: string): boolean {
  for (const child of group.children ?? []) {
    const path = typeof child.config?.path === "string" ? (child.config.path as string) : null;
    if (path && pathname.startsWith(path)) return true;
    if ((child.children ?? []).length > 0 && containsActive(child, pathname)) return true;
  }
  return false;
}
