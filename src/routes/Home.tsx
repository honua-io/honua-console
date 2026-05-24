import { Link } from "react-router-dom";

import { useSession } from "../auth/SessionContext";

export default function Home(): JSX.Element {
  const { session } = useSession();
  if (session.status !== "authenticated") {
    // ProtectedRoute should prevent this, but render defensively.
    return <p>Loading workspace…</p>;
  }
  const greeting = session.user.displayName.split(/\s+/)[0] ?? session.user.displayName;
  return (
    <div className="hc-page">
      <header className="hc-page__header">
        <h1 className="hc-page__title">Welcome back, {greeting}</h1>
        <p className="hc-page__subtitle">
          You're signed in to <strong>{session.workspace.name}</strong>. Pick a section to get started.
        </p>
      </header>

      <section className="hc-page__grid" aria-label="Workspace shortcuts">
        <Link to="/catalog" className="hc-card" data-testid="home-card-catalog">
          <h2 className="hc-card__title">Browse the catalog</h2>
          <p className="hc-card__description">Discover services, layers, and documents your workspace has published.</p>
          <span className="hc-card__cta">Open catalog →</span>
        </Link>

        <Link to="/maps" className="hc-card" data-testid="home-card-maps">
          <h2 className="hc-card__title">Open a saved map</h2>
          <p className="hc-card__description">Resume a saved web map or create a new one from a catalog layer.</p>
          <span className="hc-card__cta">Go to maps →</span>
        </Link>

        <Link to="/public" className="hc-card" data-testid="home-card-public">
          <h2 className="hc-card__title">See public datasets</h2>
          <p className="hc-card__description">Open the workspace's public-facing items and downloadable open data.</p>
          <span className="hc-card__cta">Open public →</span>
        </Link>
      </section>
    </div>
  );
}
