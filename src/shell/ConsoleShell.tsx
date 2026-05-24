import type { ReactNode } from "react";

import "./shell.css";

export type ConsoleArea = "studio" | "catalog" | "operate" | "share";

const AREAS: ReadonlyArray<{ id: ConsoleArea; label: string; description: string }> = [
  { id: "studio", label: "Studio", description: "Build maps, apps, workflows, reports" },
  { id: "catalog", label: "Catalog", description: "Find content, services, lineage" },
  { id: "operate", label: "Operate", description: "Jobs, services, health, identity" },
  { id: "share", label: "Share", description: "Links, embeds, open data" },
];

interface ConsoleShellProps {
  activeArea: ConsoleArea;
  children: ReactNode;
}

export function ConsoleShell({ activeArea, children }: ConsoleShellProps): JSX.Element {
  return (
    <div className="console-shell">
      <aside className="console-sidebar" aria-label="Console areas">
        <div className="console-brand">
          <span className="console-brand-mark" aria-hidden="true">
            H
          </span>
          <div>
            <strong>Honua Console</strong>
            <span>Unified runtime</span>
          </div>
        </div>
        <nav className="console-nav">
          {AREAS.map((area) => (
            <a
              aria-current={area.id === activeArea ? "page" : undefined}
              className={area.id === activeArea ? "console-nav-item active" : "console-nav-item"}
              href={`/${area.id}`}
              key={area.id}
            >
              <span>{area.label}</span>
              <small>{area.description}</small>
            </a>
          ))}
        </nav>
      </aside>
      <main className="console-main">{children}</main>
    </div>
  );
}
