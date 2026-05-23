import { Link } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState";

export default function Home(): JSX.Element {
  return (
    <div className="hc-page">
      <h1 className="hc-page__title">Honua Console</h1>
      <p>
        Unified web shell for Studio, Catalog, Operate, and Share. The Studio workflow area is wired below; other areas
        port in subsequent migration tickets.
      </p>
      <EmptyState
        title="Try Honua Studio"
        description="Run the Studio proof flow: prompt to clarification to spec/plan to apply to preview to edit to publish."
        primaryAction={
          <Link to="/studio/proof" className="hc-btn hc-btn--primary" data-testid="home-studio-proof">
            Open Studio proof
          </Link>
        }
      />
    </div>
  );
}
