import { useMemo } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { useMe } from "@/hooks/useMe";
import { useRecordTypes } from "@/hooks/useRecordTypes";

export default function NavMenu() {
  const location = useLocation();
  const { data: me } = useMe();
  const { data: recordTypes = [] } = useRecordTypes(false);
  const activeTypes = recordTypes.filter((t) => !t.isArchived);

  const currentPath = useMemo(
    () => location.pathname.split("?")[0].replace(/^\/+|\/+$/g, ""),
    [location.pathname]
  );

  const currentTopPath = currentPath.split("/")[0] ?? "";

  const displayName = useMemo(() => {
    if (!me || me.authenticated !== true) return "Adam Schwartz";
    const full = `${me.firstName ?? ""} ${me.lastName ?? ""}`.trim();
    return full || "Adam Schwartz";
  }, [me]);

  const menuItemClass = (href: string) =>
    currentPath === href ? "menu-item active" : "menu-item";

  const groupClass = (...members: string[]) =>
    members.includes(currentPath)
      ? "menu-item has-sub active expand"
      : "menu-item has-sub";

  return (
    <div id="top-menu" className="app-top-menu" data-bs-theme="dark">
      <div className="menu">
        <div className="menu-item menu-brand-item">
          <NavLink to="/home" className="menu-link menu-brand-link">
            <div className="menu-icon">
              <i className="fa fa-robot"></i>
            </div>
            <div className="menu-text">
              <b>Auto</b> Nate
            </div>
          </NavLink>
        </div>

        <div className={groupClass("home")}>
          <a href="#" className="menu-link" onClick={preventDefault}>
            <div className="menu-icon">
              <i className="fa fa-house"></i>
            </div>
            <div className="menu-text">Dashboard</div>
            <div className="menu-caret"></div>
          </a>
          <div className="menu-submenu">
            <div className={menuItemClass("home")}>
              <NavLink to="/home" className="menu-link">
                <div className="menu-text">Home</div>
              </NavLink>
            </div>
          </div>
        </div>

        <div
          className={
            currentTopPath === "record-types" ||
            currentTopPath === "records" ||
            currentTopPath === "record-edge-types"
              ? "menu-item has-sub active expand"
              : "menu-item has-sub"
          }
        >
          <a href="#" className="menu-link" onClick={preventDefault}>
            <div className="menu-icon">
              <i className="fa fa-database"></i>
            </div>
            <div className="menu-text">Records</div>
            <div className="menu-caret"></div>
          </a>
          <div className="menu-submenu">
            <div className={currentTopPath === "record-types" ? "menu-item active" : "menu-item"}>
              <NavLink to="/record-types" className="menu-link">
                <div className="menu-text">Record Types</div>
              </NavLink>
            </div>
            <div className={currentTopPath === "record-edge-types" ? "menu-item active" : "menu-item"}>
              <NavLink to="/record-edge-types" className="menu-link">
                <div className="menu-text">Edge Types</div>
              </NavLink>
            </div>
            {activeTypes.map((t) => (
              <div key={t.id} className={menuItemClass(`records/${t.shortCode}`)}>
                <NavLink to={`/records/${t.shortCode}`} className="menu-link">
                  <div className="menu-text">
                    <code className="me-2">{t.shortCode}</code>
                    {t.name}
                  </div>
                </NavLink>
              </div>
            ))}
          </div>
        </div>

        <div className={groupClass("workflow", "workflow-executions", "bus-watcher")}>
          <a href="#" className="menu-link" onClick={preventDefault}>
            <div className="menu-icon">
              <i className="fa fa-diagram-project"></i>
            </div>
            <div className="menu-text">Workflows</div>
            <div className="menu-caret"></div>
          </a>
          <div className="menu-submenu">
            <div className={menuItemClass("workflow")}>
              <NavLink to="/workflow" className="menu-link">
                <div className="menu-text">Workflow Studio</div>
              </NavLink>
            </div>
            <div className={menuItemClass("workflow-executions")}>
              <NavLink to="/workflow-executions" className="menu-link">
                <div className="menu-text">Workflow Executions</div>
              </NavLink>
            </div>
            <div className={menuItemClass("bus-watcher")}>
              <NavLink to="/bus-watcher" className="menu-link">
                <div className="menu-text">Bus Watcher</div>
              </NavLink>
            </div>
          </div>
        </div>

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

        <div className="menu-item ms-auto dropdown">
          <a
            href="#"
            className="menu-link dropdown-toggle d-flex align-items-center"
            data-bs-toggle="dropdown"
            aria-expanded="false"
            title="Settings"
            onClick={preventDefault}
          >
            <div className="menu-icon">
              <i className="fa fa-gear"></i>
            </div>
          </a>
          <div className="dropdown-menu dropdown-menu-end me-1">
            <NavLink className="dropdown-item" to="/manage-users">
              <i className="fa fa-users me-2"></i>Manage Users
            </NavLink>
            <NavLink className="dropdown-item" to="/admin/roles">
              <i className="fa fa-user-shield me-2"></i>Roles &amp; Permissions
            </NavLink>
            <NavLink className="dropdown-item" to="/admin/groups">
              <i className="fa fa-people-group me-2"></i>Groups
            </NavLink>
            <NavLink className="dropdown-item" to="/admin/grants">
              <i className="fa fa-key me-2"></i>Permissions
            </NavLink>
            <NavLink className="dropdown-item" to="/admin/hierarchy">
              <i className="fa fa-sitemap me-2"></i>Hierarchy
            </NavLink>
            <NavLink className="dropdown-item" to="/admin/explain">
              <i className="fa fa-magnifying-glass me-2"></i>Effective Permissions
            </NavLink>
          </div>
        </div>

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
            <NavLink className="dropdown-item" to="/user-profile">
              <i className="fa fa-user me-2"></i>User Profile
            </NavLink>
            <div className="dropdown-divider"></div>
            <form action="/account/logout" method="post">
              <button type="submit" className="dropdown-item">
                <i className="fa fa-right-from-bracket me-2"></i>Logout
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}

function preventDefault(e: React.MouseEvent<HTMLAnchorElement>) {
  e.preventDefault();
}
