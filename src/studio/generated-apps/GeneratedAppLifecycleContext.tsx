import { type ReactNode, createContext, useContext } from "react";

import type { GeneratedAppLifecycleClient } from "./client.js";

const GeneratedAppLifecycleClientContext = createContext<GeneratedAppLifecycleClient | null>(null);

export interface GeneratedAppLifecycleClientProviderProps {
  readonly client: GeneratedAppLifecycleClient;
  readonly children: ReactNode;
}

export function GeneratedAppLifecycleClientProvider({
  client,
  children,
}: GeneratedAppLifecycleClientProviderProps): JSX.Element {
  const existing = useContext(GeneratedAppLifecycleClientContext);
  return (
    <GeneratedAppLifecycleClientContext.Provider value={existing ?? client}>
      {children}
    </GeneratedAppLifecycleClientContext.Provider>
  );
}

export function useGeneratedAppLifecycleClient(): GeneratedAppLifecycleClient {
  const client = useContext(GeneratedAppLifecycleClientContext);
  if (!client) {
    throw new Error("useGeneratedAppLifecycleClient must be used inside <GeneratedAppLifecycleClientProvider>.");
  }
  return client;
}
