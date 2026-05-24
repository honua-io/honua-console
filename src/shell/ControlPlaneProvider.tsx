import { createContext, useContext, useMemo, type ReactNode } from "react";

import {
  HonuaClient,
  HonuaControlPlaneClient,
  createHonuaControlPlane,
} from "../sdk/control-plane";

const ControlPlaneContext = createContext<HonuaControlPlaneClient | undefined>(undefined);

export interface ControlPlaneProviderProps {
  readonly baseUrl?: string;
  readonly client?: HonuaControlPlaneClient;
  readonly children: ReactNode;
}

function resolveBaseUrl(explicit: string | undefined): string {
  if (explicit) return explicit;
  const fromEnv = (import.meta.env as Record<string, string | undefined>).VITE_HONUA_BASE_URL;
  if (fromEnv) return fromEnv;
  if (typeof window !== "undefined") return window.location.origin;
  return "http://localhost";
}

export function ControlPlaneProvider({
  baseUrl,
  client,
  children,
}: ControlPlaneProviderProps): JSX.Element {
  const value = useMemo<HonuaControlPlaneClient>(() => {
    if (client) return client;
    const honua = new HonuaClient({ baseUrl: resolveBaseUrl(baseUrl) });
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
