import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMemo } from "react";
import { BrowserRouter } from "react-router-dom";

import { SessionProvider } from "./auth/SessionContext";
import { AppRoutes } from "./router";

import "./styles/global.css";

interface AppProps {
  /**
   * Optional router seam used by component tests so they can swap to a
   * MemoryRouter without re-mounting providers.
   */
  Router?: React.ComponentType<{ children: React.ReactNode }>;
  /**
   * Optional session driver override for tests.
   */
  sessionDriver?: import("./auth/types").SessionDriver;
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
