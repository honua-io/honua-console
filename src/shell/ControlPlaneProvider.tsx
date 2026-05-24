import { createContext, useContext, useMemo, type ReactNode } from "react";

import {
  HonuaClient,
  HonuaControlPlaneClient,
  createHonuaControlPlane,
} from "../sdk/control-plane";
import { resolveHonuaBaseUrl } from "../config/honua";

const ControlPlaneContext = createContext<HonuaControlPlaneClient | undefined>(undefined);

const includeCredentialFetch: typeof fetch = (input, init) =>
  globalThis.fetch(input, {
    ...init,
    credentials: init?.credentials ?? "include",
  });

export interface ControlPlaneProviderProps {
  readonly baseUrl?: string;
  readonly client?: HonuaControlPlaneClient;
  readonly children: ReactNode;
}

export function ControlPlaneProvider({
  baseUrl,
  client,
  children,
}: ControlPlaneProviderProps): JSX.Element {
  const value = useMemo<HonuaControlPlaneClient>(() => {
    if (client) return client;
    const honua = new HonuaClient({
      baseUrl: resolveHonuaBaseUrl(baseUrl),
      fetchFn: includeCredentialFetch,
    });
    return createHonuaControlPlane({ client: honua });
  }, [baseUrl, client]);
  return <ControlPlaneContext.Provider value={value}>{children}</ControlPlaneContext.Provider>;
}

export function useControlPlane(): HonuaControlPlaneClient {
  const ctx = useContext(ControlPlaneContext);
  if (!ctx) {
    throw new Error("useControlPlane must be used within a ControlPlaneProvider");
  }
  return ctx;
}
