import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";

import { LoadingShell } from "../shell/LoadingShell";
import { useSession } from "./SessionContext";

interface ProtectedRouteProps {
  children: ReactNode;
}

/**
 * Gates a route on an authenticated session. While the session is bootstrapping
 * we show the LoadingShell. On an unauthenticated probe we soft-redirect to
 * sign-in with the original URL preserved as `?returnTo=...` so the SignIn
 * route can return the user there once auth completes.
 */
export function ProtectedRoute({ children }: ProtectedRouteProps): JSX.Element {
  const { session } = useSession();
  const location = useLocation();
  const intent = `${location.pathname}${location.search}${location.hash}`;

  if (session.status === "loading") {
    return <LoadingShell label="Loading workspace" />;
  }
  if (session.status === "error") {
    return (
      <div className="hc-page">
        <h1 className="hc-page__title">We couldn't load your session</h1>
        <p>{session.message}</p>
      </div>
    );
  }
  if (session.status === "unauthenticated") {
    const target = `/auth/signin?returnTo=${encodeURIComponent(intent)}`;
    return <Navigate to={target} replace />;
  }
  return <>{children}</>;
}
