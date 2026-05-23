import { Link } from "react-router-dom";

export default function SignedOut(): JSX.Element {
  return (
    <div className="hc-page">
      <h1 className="hc-page__title">You're signed out</h1>
      <p>Sign back in to return to Honua Console.</p>
      <Link to="/auth/signin" className="hc-btn hc-btn--primary">
        Sign in
      </Link>
    </div>
  );
}
