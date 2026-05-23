import { type ReactNode, createContext, useContext } from "react";

import type { CatalogClient } from "./catalog-client.js";

const CatalogClientContext = createContext<CatalogClient | null>(null);

export interface CatalogClientProviderProps {
  readonly client: CatalogClient;
  readonly children: ReactNode;
}

export function CatalogClientProvider({ client, children }: CatalogClientProviderProps) {
  const existing = useContext(CatalogClientContext);
  return <CatalogClientContext.Provider value={existing ?? client}>{children}</CatalogClientContext.Provider>;
}

export function useCatalogClient(): CatalogClient {
  const client = useContext(CatalogClientContext);
  if (!client) throw new Error("useCatalogClient must be used inside <CatalogClientProvider>.");
  return client;
}
