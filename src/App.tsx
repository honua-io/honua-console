import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMemo } from "react";

import { ConsoleShell } from "./shell/ConsoleShell";
import { StudioWorkflowEditor } from "./studio/workflows/StudioWorkflowEditor";
import { createStudioWorkflowFixtureClient } from "./studio/workflows/fixtureClient";

export function App(): JSX.Element {
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
  const workflowClient = useMemo(() => createStudioWorkflowFixtureClient(), []);

  return (
    <QueryClientProvider client={queryClient}>
      <ConsoleShell activeArea="studio">
        <StudioWorkflowEditor transport={workflowClient} />
      </ConsoleShell>
    </QueryClientProvider>
  );
}

export default App;
