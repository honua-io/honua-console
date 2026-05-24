import {
  useCallback,
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
import { resolveHonuaBaseUrl } from "../config/honua";
import { emitConsoleSmoke } from "../telemetry/smoke";

interface SessionContextValue {
  readonly status: SessionStatus;
  readonly refresh: () => Promise<void>;
  readonly bundle: CapabilityBundle;
}

const SessionContext = createContext<SessionContextValue | undefined>(undefined);

export interface SessionProviderProps {
  readonly baseUrl?: string;
  readonly client?: SessionClient;
  readonly children: ReactNode;
}

export function SessionProvider({ baseUrl, client, children }: SessionProviderProps): JSX.Element {
  const sessionClient = useMemo<SessionClient>(
    () => client ?? new SessionClient({ baseUrl: resolveHonuaBaseUrl(baseUrl) }),
    [baseUrl, client],
  );
  const [status, setStatus] = useState<SessionStatus>({ kind: "loading" });
  const refreshSeq = useRef(0);
  const mounted = useRef(true);

  useEffect(
    () => {
      mounted.current = true;
      return () => {
        mounted.current = false;
        refreshSeq.current += 1;
      };
    },
    [],
  );

  const refresh = useCallback(
    async () => {
      const seq = refreshSeq.current + 1;
      refreshSeq.current = seq;
      setStatus({ kind: "loading" });
      const started = performance.now();
      const result = await sessionClient.bootstrap().catch((error: unknown) => ({
        status: {
          kind: "error" as const,
          message: error instanceof Error ? error.message : "session bootstrap failed",
        },
        fellBackEndpoints: [],
      }));
      if (!mounted.current || refreshSeq.current !== seq) return;
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
    [sessionClient],
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
