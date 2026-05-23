import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";

import { createFixtureDriver } from "./fixtureDriver";
import { sanitizeReturnTo } from "./returnTo";
import type { Session, SessionDriver } from "./types";
import { createWhoamiDriver } from "./whoamiDriver";

interface SessionContextValue {
  session: Session;
  refresh: () => Promise<void>;
  signIn: (returnTo?: string) => Promise<void>;
  signOut: () => Promise<void>;
  driverName: string;
}

const SessionContext = createContext<SessionContextValue | undefined>(undefined);

interface SessionProviderProps {
  driver?: SessionDriver;
  children: React.ReactNode;
}

function resolveDefaultDriver(): SessionDriver {
  const name = (import.meta.env.VITE_SESSION_DRIVER as string | undefined)?.toLowerCase() ?? "fixture";
  if (name === "whoami") {
    const url = (import.meta.env.VITE_WHOAMI_URL as string | undefined) ?? "/api/console/whoami";
    return createWhoamiDriver(url);
  }
  return createFixtureDriver();
}

export function SessionProvider({ driver, children }: SessionProviderProps): JSX.Element {
  const activeDriver = useMemo(() => driver ?? resolveDefaultDriver(), [driver]);
  const [session, setSession] = useState<Session>({ status: "loading" });

  const refresh = useCallback(async () => {
    setSession({ status: "loading" });
    try {
      const next = await activeDriver.probe();
      setSession(next);
    } catch (error) {
      const message = error instanceof Error ? error.message : "session probe failed";
      setSession({ status: "error", message });
    }
  }, [activeDriver]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const signIn = useCallback(
    async (returnTo?: string) => {
      const intent = returnTo ?? `${window.location.pathname}${window.location.search}${window.location.hash}`;
      await activeDriver.signIn(sanitizeReturnTo(intent));
    },
    [activeDriver],
  );

  const signOut = useCallback(async () => {
    await activeDriver.signOut();
  }, [activeDriver]);

  const value = useMemo<SessionContextValue>(
    () => ({ session, refresh, signIn, signOut, driverName: activeDriver.name }),
    [session, refresh, signIn, signOut, activeDriver.name],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const ctx = useContext(SessionContext);
  if (!ctx) {
    throw new Error("useSession must be used within a SessionProvider");
  }
  return ctx;
}
