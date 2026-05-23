import { sanitizeReturnTo } from "./returnTo";
import type { AuthenticatedSession, Session, SessionDriver } from "./types";

const STORAGE_KEY = "honua.console.fixture-session";
const RETURN_TO_KEY = "honua.console.fixture-return-to";

/**
 * Preset fixture sessions. Selectable from the sign-in screen so developers
 * and smoke tests can switch between builder and operator nav variants without
 * standing up an auth server. Real OIDC wiring lands when honua-server#1162
 * delivers `/portal/whoami` and honua-console#7 swaps the default driver.
 */
export const FIXTURE_PRESETS: Record<string, AuthenticatedSession> = {
  builder: {
    status: "authenticated",
    user: {
      id: "u-builder",
      displayName: "Mira Chen",
      email: "mira@demo.honua.example",
    },
    workspace: { id: "w-acme", name: "Acme Geospatial" },
    scopes: ["member"],
  },
  operator: {
    status: "authenticated",
    user: {
      id: "u-operator",
      displayName: "Owen Park",
      email: "owen@demo.honua.example",
    },
    workspace: { id: "w-acme", name: "Acme Geospatial" },
    scopes: ["member", "operator"],
  },
  admin: {
    status: "authenticated",
    user: {
      id: "u-admin",
      displayName: "Ada Lin",
      email: "ada@demo.honua.example",
    },
    workspace: { id: "w-acme", name: "Acme Geospatial" },
    scopes: ["member", "operator", "admin"],
  },
};

function readStorage(): AuthenticatedSession | null {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as AuthenticatedSession;
    if (parsed?.status === "authenticated" && parsed.user && parsed.workspace) {
      return parsed;
    }
  } catch {
    // Ignore corrupted fixture state.
  }
  return null;
}

function writeStorage(session: AuthenticatedSession | null): void {
  try {
    if (session) {
      window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } else {
      window.sessionStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    // Storage unavailable (private mode, etc.); fixture session is best-effort.
  }
}

function readEnvFixture(): AuthenticatedSession | null {
  const raw = (import.meta.env.VITE_FAKE_SESSION as string | undefined)?.trim();
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as AuthenticatedSession;
    if (parsed?.status === "authenticated" && parsed.user && parsed.workspace) {
      return parsed;
    }
  } catch {
    console.warn("VITE_FAKE_SESSION is not valid JSON; ignoring.");
  }
  return null;
}

export function setFixtureSession(presetOrSession: keyof typeof FIXTURE_PRESETS | AuthenticatedSession): void {
  const session = typeof presetOrSession === "string" ? FIXTURE_PRESETS[presetOrSession] : presetOrSession;
  if (!session) {
    throw new Error(`Unknown fixture preset: ${String(presetOrSession)}`);
  }
  writeStorage(session);
}

export function consumeReturnTo(): string | null {
  try {
    const value = window.sessionStorage.getItem(RETURN_TO_KEY);
    if (value) {
      window.sessionStorage.removeItem(RETURN_TO_KEY);
    }
    return value ? sanitizeReturnTo(value) : null;
  } catch {
    return null;
  }
}

export function createFixtureDriver(): SessionDriver {
  return {
    name: "fixture",
    async probe(): Promise<Session> {
      const stored = readStorage();
      if (stored) return stored;
      const seeded = readEnvFixture();
      if (seeded) {
        writeStorage(seeded);
        return seeded;
      }
      return { status: "unauthenticated" };
    },
    async signIn(returnTo: string): Promise<void> {
      const safeReturnTo = sanitizeReturnTo(returnTo);
      try {
        window.sessionStorage.setItem(RETURN_TO_KEY, safeReturnTo);
      } catch {
        // Storage unavailable; the sign-in page falls back to the home route.
      }
      window.location.assign("/auth/signin");
    },
    async signOut(): Promise<void> {
      writeStorage(null);
      window.location.assign("/auth/signed-out");
    },
  };
}
