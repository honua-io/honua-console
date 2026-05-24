import { Link } from "react-router-dom";

import { AREA_DESCRIPTORS, CONSOLE_AREAS } from "../areas";
import { BUILD_INFO } from "../build-info";

export default function Home(): JSX.Element {
  return (
    <main style={{ padding: "32px", maxWidth: "880px", margin: "0 auto" }}>
      <header>
        <h1>Honua Console</h1>
        <p>Unified surface for Studio, Catalog, Operate, and Share.</p>
      </header>
      <nav aria-label="Console areas">
        <ul style={{ listStyle: "none", padding: 0, display: "grid", gap: "16px", gridTemplateColumns: "1fr 1fr" }}>
          {CONSOLE_AREAS.map((id) => {
            const area = AREA_DESCRIPTORS[id];
            return (
              <li key={id} style={{ border: "1px solid #d6d9e3", borderRadius: "10px", padding: "16px" }}>
                <Link to={area.path}>
                  <strong>{area.label}</strong>
                </Link>
                <p style={{ margin: "8px 0 0 0", color: "#5a6275" }}>{area.summary}</p>
              </li>
            );
          })}
        </ul>
      </nav>
      <footer style={{ marginTop: "32px", color: "#80869a", fontSize: "12px" }} data-build-info>
        Build {BUILD_INFO.version} ({BUILD_INFO.shortCommit}) — {BUILD_INFO.builtAt}
      </footer>
    </main>
  );
}
