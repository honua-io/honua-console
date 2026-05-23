import { Link } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState";

export default function NotFound(): JSX.Element {
  return (
    <EmptyState
      title="Page not found"
      description="The URL you opened does not match a Console route."
      primaryAction={
        <Link to="/" className="hc-btn hc-btn--primary">
          Back to home
        </Link>
      }
    />
  );
}
