import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, describe, expect, it } from "vitest";

import { ProtectedRoute } from "../auth/ProtectedRoute";
import { SessionProvider, useSession } from "../auth/SessionContext";
import { createFixtureDriver } from "../auth/fixtureDriver";
import type { Session, SessionDriver } from "../auth/types";
import SignIn from "./SignIn";

const FIXTURE_STORAGE_KEY = "honua.console.fixture-session";

describe("SignIn route auth driver handling", () => {
  afterEach(() => {
    window.sessionStorage.clear();
  });

  it("delegates unauthenticated whoami redirects to the active sign-in driver", async () => {
    const driver = new RecordingSessionDriver("whoami", { status: "unauthenticated" });

    renderAuthRoutes(driver, "/studio/proof");

    await waitFor(() => {
      expect(driver.signInCalls).toEqual(["/studio/proof"]);
    });
    expect(driver.probeCalls).toBe(1);
    expect(screen.getByTestId("test-location")).toHaveTextContent("/auth/signin?returnTo=%2Fstudio%2Fproof");
    expect(window.sessionStorage.getItem(FIXTURE_STORAGE_KEY)).toBeNull();
    expect(screen.queryByText("Protected Studio")).not.toBeInTheDocument();
  });

  it("keeps the fixture preset sign-in path and returns to the requested route", async () => {
    renderAuthRoutes(createFixtureDriver(), "/auth/signin?preset=operator&returnTo=/catalog");

    await waitFor(() => {
      expect(screen.getByTestId("test-location")).toHaveTextContent("/catalog");
    });
    expect(screen.getByTestId("target-session")).toHaveTextContent("Owen Park");
  });
});

class RecordingSessionDriver implements SessionDriver {
  readonly signInCalls: string[] = [];
  probeCalls = 0;

  constructor(
    readonly name: string,
    private readonly probeSession: Session,
  ) {}

  async probe(): Promise<Session> {
    this.probeCalls += 1;
    return this.probeSession;
  }

  async signIn(returnTo: string): Promise<void> {
    this.signInCalls.push(returnTo);
  }

  async signOut(): Promise<void> {}
}

function renderAuthRoutes(driver: SessionDriver, initialEntry: string): void {
  render(
    <SessionProvider driver={driver}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <LocationProbe />
        <Routes>
          <Route path="/auth/signin" element={<SignIn />} />
          <Route
            path="/studio/proof"
            element={
              <ProtectedRoute>
                <span>Protected Studio</span>
              </ProtectedRoute>
            }
          />
          <Route path="/catalog" element={<TargetSession />} />
        </Routes>
      </MemoryRouter>
    </SessionProvider>,
  );
}

function LocationProbe(): JSX.Element {
  const location = useLocation();
  return (
    <span data-testid="test-location">
      {location.pathname}
      {location.search}
      {location.hash}
    </span>
  );
}

function TargetSession(): JSX.Element {
  const { session } = useSession();
  return (
    <span data-testid="target-session">
      {session.status === "authenticated" ? session.user.displayName : session.status}
    </span>
  );
}
