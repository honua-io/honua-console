import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

import {
  SessionClient,
  type CapabilityBundle,
  type CapabilityName,
  type EntitlementName,
  type SessionStatus,
  createEmptyBundle,
} from "../sdk/session";
import { emitConsoleSmoke } from "../telemetry/smoke";

interface SessionContextValue {
  readonly status: SessionStatus;
  readonly refresh: () => Promise<void>;
  readonly bundle: CapabilityBundle;
}

const SessionContext = createContext<SessionContextValue | undefined>(undefined);

export interface SessionProviderProps {
  readonly client?: SessionClient;
  readonly children: ReactNode;
}

export function SessionProvider({ client, children }: SessionProviderProps): JSX.Element {
  const clientRef = useRef<SessionClient>(client ?? new SessionClient());
  const [status, setStatus] = useState<SessionStatus>({ kind: "loading" });

  const refresh = useMemo(
    () => async () => {
      setStatus({ kind: "loading" });
      const started = performance.now();
      const result = await clientRef.current.bootstrap();
      setStatus(result.status);
      emitConsoleSmoke({
        surface: "session.bootstrap",
        sdkSubpath: "session",
        status:
          result.status.kind === "authenticated"
            ? "ok"
            : result.status.kind === "anonymous"
              ? "unauthorized"
              : result.status.kind === "error"
                ? "unsupported"
                : "pending-binding",
        durationMs: Math.round(performance.now() - started),
        ...(result.fellBackEndpoints.length > 0
          ? { detail: { fellBackEndpoints: result.fellBackEndpoints } }
          : {}),
      });
    },
    [],
  );

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<SessionContextValue>(
    () => ({
      status,
      refresh,
      bundle:
        status.kind === "authenticated" ? status.bundle : createEmptyBundle(),
    }),
    [status, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("useSession must be used within a SessionProvider");
  return ctx;
}

export function useCapability(name: CapabilityName | string): boolean {
  const { bundle } = useSession();
  return bundle.capabilities.has(name as CapabilityName);
}

export function useEntitlement(name: EntitlementName | string): boolean {
  const { bundle } = useSession();
  return bundle.entitlements.has(name as EntitlementName);
}
