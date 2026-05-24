import { Link } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState";

export default function NotFound(): JSX.Element {
  return (
    <div className="hc-page">
      <EmptyState
        title="That page doesn't exist"
        description="The link may be out of date, or the item may have been removed. Head back to the workspace home to keep going."
        primaryAction={
          <Link to="/" className="hc-btn hc-btn--primary">
            Back to home
          </Link>
        }
      />
    </div>
  );
}
