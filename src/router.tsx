import { Suspense, lazy } from "react";
import { Route, Routes } from "react-router-dom";

import { ProtectedRoute } from "./auth/ProtectedRoute";
import { AppShell } from "./shell/AppShell";
import { ErrorBoundary } from "./shell/ErrorBoundary";
import { LoadingShell } from "./shell/LoadingShell";

// Eager: shell + landing route. Lazy: every other top-level route, so the
// catalog/maps/data startup paths only pay for their own bundle weight.
import Home from "./routes/Home";
import SignIn from "./routes/SignIn";
import SignedOut from "./routes/SignedOut";

const Catalog = lazy(() => import("./routes/Catalog"));
const CatalogItem = lazy(() => import("./routes/CatalogItem"));
const Maps = lazy(() => import("./routes/Maps"));
const EmbedMap = lazy(() => import("./routes/EmbedMap"));
const Data = lazy(() => import("./routes/Data"));
const Groups = lazy(() => import("./routes/Groups"));
const Public = lazy(() => import("./routes/Public"));
const OpenDataItem = lazy(() => import("./routes/OpenDataItem"));
const AuthCallback = lazy(() => import("./routes/AuthCallback"));
const NotFound = lazy(() => import("./routes/NotFound"));

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

function PublicShell({ children }: { children: React.ReactNode }): JSX.Element {
  // Public routes still render inside the shell so brand and nav stay
  // consistent; sign-in surfaces in the user menu when no session is present.
  return (
    <AppShell>
      <ErrorBoundary>
        <Suspense fallback={<LoadingShell label="Loading view" />}>{children}</Suspense>
      </ErrorBoundary>
    </AppShell>
  );
}

function EmbedShell({ children }: { children: React.ReactNode }): JSX.Element {
  // Embed routes do not render the AppShell — they are intended to be loaded
  // inside a third-party iframe with their own chrome controls. Token auth
  // lives in the URL fragment; see embed/auth.ts.
  return (
    <ErrorBoundary>
      <Suspense fallback={<LoadingShell label="Loading embed" />}>{children}</Suspense>
    </ErrorBoundary>
  );
}

export function AppRoutes(): JSX.Element {
  return (
    <Routes>
      {/* System routes — anonymous */}
      <Route path="/auth/signin" element={<SignIn />} />
      <Route path="/auth/signed-out" element={<SignedOut />} />
      <Route path="/auth/callback" element={<AuthCallback />} />

      {/* Share group — anonymous open-data + embed */}
      <Route
        path="/share/public"
        element={
          <PublicShell>
            <Public />
          </PublicShell>
        }
      />
      <Route
        path="/share/public/items/:idOrSlug"
        element={
          <PublicShell>
            <OpenDataItem />
          </PublicShell>
        }
      />
      <Route
        path="/share/embed/maps/:mapId"
        element={
          <EmbedShell>
            <EmbedMap />
          </EmbedShell>
        }
      />

      {/* Legacy Portal-path compatibility aliases. Same components, so the URL
          fragment (embed token) and query string (chrome/legend/zoom/extent
          params) survive untouched. New URLs emitted by Console emit Console
          IA paths; legacy paths remain valid for embed snippets in the wild. */}
      <Route
        path="/public"
        element={
          <PublicShell>
            <Public />
          </PublicShell>
        }
      />
      <Route
        path="/public/items/:idOrSlug"
        element={
          <PublicShell>
            <OpenDataItem />
          </PublicShell>
        }
      />
      <Route
        path="/embed/maps/:mapId"
        element={
          <EmbedShell>
            <EmbedMap />
          </EmbedShell>
        }
      />

      {/* Home */}
      <Route
        path="/"
        element={
          <ProtectedShell>
            <Home />
          </ProtectedShell>
        }
      />

      {/* Catalog group — session required */}
      <Route
        path="/catalog"
        element={
          <ProtectedShell>
            <Catalog />
          </ProtectedShell>
        }
      />
      <Route
        path="/catalog/maps"
        element={
          <ProtectedShell>
            <Maps />
          </ProtectedShell>
        }
      />
      <Route
        path="/catalog/maps/:mapId"
        element={
          <ProtectedShell>
            <Maps />
          </ProtectedShell>
        }
      />
      {/* CatalogItem (`/catalog/:idOrSlug`) lives AFTER the more specific
          `/catalog/maps` and `/catalog/maps/:mapId` so the saved-maps surface
          wins for those paths. */}
      <Route
        path="/catalog/:idOrSlug"
        element={
          <ProtectedShell>
            <CatalogItem />
          </ProtectedShell>
        }
      />

      {/* Legacy Portal /maps paths — compat aliases */}
      <Route
        path="/maps"
        element={
          <ProtectedShell>
            <Maps />
          </ProtectedShell>
        }
      />
      <Route
        path="/maps/:mapId"
        element={
          <ProtectedShell>
            <Maps />
          </ProtectedShell>
        }
      />

      {/* Builder-side placeholders (Catalog group) */}
      <Route
        path="/data"
        element={
          <ProtectedShell>
            <Data />
          </ProtectedShell>
        }
      />
      <Route
        path="/groups"
        element={
          <ProtectedShell>
            <Groups />
          </ProtectedShell>
        }
      />

      <Route
        path="*"
        element={
          <PublicShell>
            <NotFound />
          </PublicShell>
        }
      />
    </Routes>
  );
}
