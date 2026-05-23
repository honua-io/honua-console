import { type ReactNode, Suspense, lazy } from "react";
import { Navigate, Route, Routes } from "react-router-dom";

import { ProtectedRoute } from "./auth/ProtectedRoute";
import AreaPlaceholder from "./routes/AreaPlaceholder";
import { AppShell } from "./shell/AppShell";
import { ErrorBoundary } from "./shell/ErrorBoundary";
import { LoadingShell } from "./shell/LoadingShell";

import Home from "./routes/Home";
import NotFound from "./routes/NotFound";
import SignIn from "./routes/SignIn";
import SignedOut from "./routes/SignedOut";

// Studio area + generated apps: lazy-loaded so Console shell and other areas
// do not pay for Studio bundle weight. See ADR-0001 and design brief.
const StudioProof = lazy(() => import("./routes/StudioProof"));
const StudioGeneratedAppPreview = lazy(() => import("./routes/StudioGeneratedAppPreview"));

function ProtectedShell({ children }: { children: ReactNode }): JSX.Element {
  return (
    <ProtectedRoute>
      <AppShell>
        <ErrorBoundary>
          <Suspense fallback={<LoadingShell label="Loading view" />}>{children}</Suspense>
        </ErrorBoundary>
      </AppShell>
    </ProtectedRoute>
  );
}

export interface ConsoleRoute {
  readonly path: string;
  readonly element: ReactNode;
  readonly protected: boolean;
}

/**
 * Authoritative route table for the Console. Drives `AppRoutes` AND the
 * navigation invariant test, so every NAV_ITEMS target must appear here
 * either as a real page, an intentional redirect to a working subroute, or
 * an `AreaPlaceholder` shell for areas that port in follow-up tickets.
 * Top-level `/catalog`, `/operate`, and `/share` ship placeholders so the
 * primary nav never falls through to the wildcard NotFound.
 */
export const CONSOLE_ROUTES: ReadonlyArray<ConsoleRoute> = [
  { path: "/auth/signin", element: <SignIn />, protected: false },
  { path: "/auth/signed-out", element: <SignedOut />, protected: false },
  { path: "/", element: <Home />, protected: true },
  // `/studio` redirects to the only Studio surface shipped in honua-console#5;
  // unauthenticated users still bounce through ProtectedRoute at /studio/proof.
  { path: "/studio", element: <Navigate to="/studio/proof" replace />, protected: false },
  { path: "/studio/proof", element: <StudioProof />, protected: true },
  { path: "/studio/apps/:itemId/preview", element: <StudioGeneratedAppPreview />, protected: true },
  { path: "/catalog", element: <AreaPlaceholder area="catalog" />, protected: true },
  { path: "/operate", element: <AreaPlaceholder area="operate" />, protected: true },
  { path: "/share", element: <AreaPlaceholder area="share" />, protected: true },
  { path: "*", element: <NotFound />, protected: true },
];

export function AppRoutes(): JSX.Element {
  return (
    <Routes>
      {CONSOLE_ROUTES.map((route) => (
        <Route
          key={route.path}
          path={route.path}
          element={route.protected ? <ProtectedShell>{route.element}</ProtectedShell> : route.element}
        />
      ))}
    </Routes>
  );
}
