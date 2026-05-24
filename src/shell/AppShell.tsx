import { type ReactNode, useEffect, useState } from "react";
import { Link, NavLink, useLocation } from "react-router-dom";

import { useSession } from "../auth/SessionContext";
import { visibleNavItems } from "./NavConfig";
import { UserMenu } from "./UserMenu";

import "./shell.css";

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps): JSX.Element {
  const { session } = useSession();
  const navItems = visibleNavItems(session);
  const location = useLocation();
  const [navOpen, setNavOpen] = useState(false);

  // biome-ignore lint/correctness/useExhaustiveDependencies: dependency triggers re-run on route change.
  useEffect(() => {
    setNavOpen(false);
  }, [location.pathname]);

  return (
    <div className="hc-shell" data-nav-open={navOpen ? "true" : "false"}>
      <a className="hc-skip-link" href="#hc-main">
        Skip to main content
      </a>

      <header className="hc-topbar">
        <button
          type="button"
          className="hc-topbar__nav-toggle"
          aria-label={navOpen ? "Close navigation" : "Open navigation"}
          aria-expanded={navOpen}
          aria-controls="hc-sidenav"
          onClick={() => setNavOpen((prev) => !prev)}
          data-testid="nav-toggle"
        >
          <span aria-hidden="true">{navOpen ? "✕" : "☰"}</span>
        </button>
        <Link to="/" className="hc-topbar__brand" data-testid="brand-home">
          <span className="hc-topbar__brand-mark" aria-hidden="true">
            ◇
          </span>
          <span className="hc-topbar__brand-text">Honua Console</span>
        </Link>
        <div className="hc-topbar__spacer" aria-hidden="true" />
        <div className="hc-topbar__search-slot" aria-hidden="true">
          {/* Reserved for global search (#12). */}
        </div>
        <UserMenu />
      </header>

      <div className="hc-shell__body">
        <nav id="hc-sidenav" className="hc-sidenav" aria-label="Primary" data-state={navOpen ? "open" : "closed"}>
          <ul className="hc-sidenav__list">
            {navItems.map((item) => (
              <li key={item.id} className="hc-sidenav__item">
                <NavLink
                  to={item.to}
                  end={item.to === "/"}
                  className={({ isActive }) =>
                    isActive ? "hc-sidenav__link hc-sidenav__link--active" : "hc-sidenav__link"
                  }
                  title={item.description}
                  data-testid={`nav-${item.id}`}
                >
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
          {session.status === "authenticated" && (
            <div className="hc-sidenav__footer">
              <span className="hc-sidenav__workspace-label">Workspace</span>
              <span className="hc-sidenav__workspace-name">{session.workspace.name}</span>
            </div>
          )}
        </nav>

        <main id="hc-main" className="hc-main" tabIndex={-1}>
          {children}
        </main>
      </div>

      {navOpen && (
        <button
          type="button"
          className="hc-shell__scrim"
          aria-label="Close navigation"
          onClick={() => setNavOpen(false)}
        />
      )}
    </div>
  );
}
