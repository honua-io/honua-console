import { Link } from "react-router-dom";

import "./auth.css";

export default function SignedOut(): JSX.Element {
  return (
    <main className="hc-auth-page" id="hc-main" tabIndex={-1}>
      <div className="hc-auth-card">
        <h1 className="hc-auth-card__title">You're signed out</h1>
        <p className="hc-auth-card__lede">Public items below remain available without an account.</p>
        <div className="hc-auth-card__actions">
          <Link to="/auth/signin" className="hc-btn hc-btn--primary">
            Sign back in
          </Link>
          <Link to="/public" className="hc-btn">
            Browse public items
          </Link>
        </div>
      </div>
    </main>
  );
}
