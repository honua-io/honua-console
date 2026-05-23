import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Suspense, useCallback, useMemo, useState } from "react";
import { BrowserRouter, Navigate, Route, Routes, useNavigate, useSearchParams } from "react-router-dom";

import { OperatorRoute } from "./auth/OperatorRoute";
import { ProtectedRoute } from "./auth/ProtectedRoute";
import { SessionProvider, useSession } from "./auth/SessionContext";
import { FIXTURE_PRESETS, consumeReturnTo, setFixtureSession } from "./auth/fixtureDriver";
import { sanitizeReturnTo } from "./auth/returnTo";
import type { SessionDriver } from "./auth/types";
import { AppShell } from "./shell/AppShell";
import { EmptyState } from "./shell/EmptyState";
import { ErrorBoundary } from "./shell/ErrorBoundary";
import { LoadingShell } from "./shell/LoadingShell";

interface AppProps {
  Router?: React.ComponentType<{ children: React.ReactNode }>;
  sessionDriver?: SessionDriver;
}

const FIXTURE_SIGN_IN_CHOICES = [
  {
    id: "builder",
    label: "Continue as builder",
    description: "Studio, Catalog, and Share access for local porting work.",
  },
  {
    id: "operator",
    label: "Continue as operator",
    description: "Builder access plus Operate navigation and legacy admin link-back.",
  },
  {
    id: "admin",
    label: "Continue as admin",
    description: "Full fixture scope for RBAC and transition-path smoke checks.",
  },
] as const;

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
    <ProtectedRoute>
      <AppShell>
        <ErrorBoundary>
          <Suspense fallback={<LoadingShell label="Loading operator view" />}>
            <OperatorRoute>{children}</OperatorRoute>
          </Suspense>
        </ErrorBoundary>
      </AppShell>
    </ProtectedRoute>
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
  const { driverName, refresh, signIn } = useSession();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [pendingChoice, setPendingChoice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const resolveReturnTo = useCallback(
    () => sanitizeReturnTo(searchParams.get("returnTo") ?? consumeReturnTo() ?? "/"),
    [searchParams],
  );

  const handleFixtureSignIn = useCallback(
    async (preset: keyof typeof FIXTURE_PRESETS) => {
      setPendingChoice(preset);
      setError(null);
      try {
        setFixtureSession(preset);
        await refresh();
        navigate(resolveReturnTo(), { replace: true });
      } catch (caught) {
        const message = caught instanceof Error ? caught.message : "Fixture sign-in failed.";
        setError(message);
      } finally {
        setPendingChoice(null);
      }
    },
    [navigate, refresh, resolveReturnTo],
  );

  const handleServerSignIn = useCallback(async () => {
    setPendingChoice("server");
    setError(null);
    try {
      await signIn(resolveReturnTo());
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "Sign-in redirect failed.";
      setError(message);
      setPendingChoice(null);
    }
  }, [resolveReturnTo, signIn]);

  return (
    <div className="hc-auth-page">
      <section className="hc-auth-card">
        <h1 className="hc-auth-card__title">Sign in to Honua Console</h1>
        <p className="hc-auth-card__lede">
          Fixture sign-in is provided by the session driver until the shared server whoami contract lands.
        </p>
        {driverName === "fixture" ? (
          <ul className="hc-auth-card__presets" aria-label="Fixture sign-in presets">
            {FIXTURE_SIGN_IN_CHOICES.map((choice) => {
              const preset = FIXTURE_PRESETS[choice.id];
              return (
                <li key={choice.id} className="hc-auth-card__preset">
                  <button
                    type="button"
                    className="hc-btn hc-btn--primary"
                    onClick={() => {
                      void handleFixtureSignIn(choice.id);
                    }}
                    disabled={pendingChoice !== null}
                  >
                    {pendingChoice === choice.id ? "Signing in..." : choice.label}
                  </button>
                  <span className="hc-auth-card__preset-meta">
                    <strong>{preset.user.displayName}</strong>
                    <span>{choice.description}</span>
                  </span>
                </li>
              );
            })}
          </ul>
        ) : (
          <div className="hc-auth-card__actions">
            <button
              type="button"
              className="hc-btn hc-btn--primary"
              onClick={() => {
                void handleServerSignIn();
              }}
              disabled={pendingChoice !== null}
            >
              {pendingChoice === "server" ? "Redirecting..." : "Continue"}
            </button>
          </div>
        )}
        {error && <p className="hc-auth-card__error">{error}</p>}
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
