import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Suspense, useMemo } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";

import { OperatorRoute } from "./auth/OperatorRoute";
import { ProtectedRoute } from "./auth/ProtectedRoute";
import { SessionProvider } from "./auth/SessionContext";
import type { SessionDriver } from "./auth/types";
import { AppShell } from "./shell/AppShell";
import { EmptyState } from "./shell/EmptyState";
import { ErrorBoundary } from "./shell/ErrorBoundary";
import { LoadingShell } from "./shell/LoadingShell";

interface AppProps {
  Router?: React.ComponentType<{ children: React.ReactNode }>;
  sessionDriver?: SessionDriver;
}

function ShellRoute({ children }: { children: React.ReactNode }): JSX.Element {
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

function OperatorShellRoute({ children }: { children: React.ReactNode }): JSX.Element {
  return (
    <OperatorRoute>
      <AppShell>
        <ErrorBoundary>
          <Suspense fallback={<LoadingShell label="Loading operator view" />}>{children}</Suspense>
        </ErrorBoundary>
      </AppShell>
    </OperatorRoute>
  );
}

function PlaceholderPage({
  title,
  description,
  waitingFor,
}: {
  title: string;
  description: string;
  waitingFor: string;
}): JSX.Element {
  return (
    <div className="hc-page">
      <header className="hc-page__header">
        <h1 className="hc-page__title">{title}</h1>
        <p className="hc-page__subtitle">{description}</p>
      </header>
      <EmptyState
        title="Ready for porting"
        description="This route is intentionally minimal while the Console contracts and migration plan settle."
      >
        Waiting for {waitingFor}.
      </EmptyState>
    </div>
  );
}

function SignInPage(): JSX.Element {
  return (
    <div className="hc-auth-page">
      <section className="hc-auth-card">
        <h1 className="hc-auth-card__title">Sign in to Honua Console</h1>
        <p className="hc-auth-card__lede">
          Fixture sign-in is provided by the session driver until the shared server whoami contract lands.
        </p>
      </section>
    </div>
  );
}

function SignedOutPage(): JSX.Element {
  return (
    <div className="hc-auth-page">
      <section className="hc-auth-card">
        <h1 className="hc-auth-card__title">Signed out</h1>
        <p className="hc-auth-card__lede">Your local Console session has ended.</p>
      </section>
    </div>
  );
}

export function AppRoutes(): JSX.Element {
  return (
    <Routes>
      <Route path="/auth/signin" element={<SignInPage />} />
      <Route path="/auth/signed-out" element={<SignedOutPage />} />
      <Route path="/auth/callback" element={<Navigate to="/" replace />} />
      <Route
        path="/"
        element={
          <ShellRoute>
            <PlaceholderPage
              title="Honua Console"
              description="Unified shell for Studio, Catalog, Operate, and Share."
              waitingFor="honua-console#3 route map"
            />
          </ShellRoute>
        }
      />
      <Route
        path="/studio"
        element={
          <ShellRoute>
            <PlaceholderPage
              title="Studio"
              description="AI-assisted spatial query, analysis, maps, dashboards, reports, and apps."
              waitingFor="honua-console#5 Studio port"
            />
          </ShellRoute>
        }
      />
      <Route
        path="/catalog"
        element={
          <ShellRoute>
            <PlaceholderPage
              title="Catalog"
              description="Data, layers, services, saved maps, dashboards, reports, generated apps, and metadata."
              waitingFor="honua-console#4 Portal port"
            />
          </ShellRoute>
        }
      />
      <Route
        path="/operate"
        element={
          <OperatorShellRoute>
            <PlaceholderPage
              title="Operate"
              description="Publishing, service configuration, identity, connectors, observability, and runtime administration."
              waitingFor="honua-console#6 legacy Admin transition"
            />
          </OperatorShellRoute>
        }
      />
      <Route
        path="/share"
        element={
          <ShellRoute>
            <PlaceholderPage
              title="Share"
              description="Public links, embeds, open-data pages, exports, and external publishing flows."
              waitingFor="honua-console#4 and #7 sharing contracts"
            />
          </ShellRoute>
        }
      />
      <Route
        path="*"
        element={
          <ShellRoute>
            <EmptyState title="View not found" description="This Console route is not available yet." />
          </ShellRoute>
        }
      />
    </Routes>
  );
}

export function App({ Router = BrowserRouter, sessionDriver }: AppProps = {}): JSX.Element {
  const queryClient = useMemo(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
    [],
  );

  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider driver={sessionDriver}>
        <Router>
          <AppRoutes />
        </Router>
      </SessionProvider>
    </QueryClientProvider>
  );
}

export default App;
