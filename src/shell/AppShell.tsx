import { NavLink, Outlet } from "react-router-dom";

export function AppShell(): JSX.Element {
  return (
    <div className="shell">
      <aside className="shell__sidebar" aria-label="Primary">
        <a className="shell__brand" href="/studio">
          Honua Console
        </a>
        <nav className="shell__nav">
          <NavLink to="/studio">Studio</NavLink>
          <NavLink to="/catalog/console-map-operations">Catalog</NavLink>
          <NavLink to="/share/console-map-operations">Share</NavLink>
          <span aria-disabled="true">Operate</span>
        </nav>
      </aside>
      <main className="shell__main">
        <Outlet />
      </main>
    </div>
  );
}
