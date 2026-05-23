import { Suspense, lazy } from "react";
import { Route, Routes } from "react-router-dom";

import { ProtectedRoute } from "./auth/ProtectedRoute";
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

function ProtectedShell({ children }: { children: React.ReactNode }): JSX.Element {
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

export function AppRoutes(): JSX.Element {
  return (
    <Routes>
      <Route path="/auth/signin" element={<SignIn />} />
      <Route path="/auth/signed-out" element={<SignedOut />} />

      <Route
        path="/"
        element={
          <ProtectedShell>
            <Home />
          </ProtectedShell>
        }
      />
      <Route
        path="/studio/proof"
        element={
          <ProtectedShell>
            <StudioProof />
          </ProtectedShell>
        }
      />
      <Route
        path="/studio/apps/:itemId/preview"
        element={
          <ProtectedShell>
            <StudioGeneratedAppPreview />
          </ProtectedShell>
        }
      />
      <Route
        path="*"
        element={
          <ProtectedShell>
            <NotFound />
          </ProtectedShell>
        }
      />
    </Routes>
  );
}
