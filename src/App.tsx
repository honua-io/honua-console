import { BrowserRouter } from "react-router-dom";

import { AppRoutes } from "./router";

interface AppProps {
  // Test seam used by component tests to swap to a MemoryRouter without
  // re-mounting providers. The wrapper must accept `basename` so subpath
  // deployments can be exercised under MemoryRouter as well.
  Router?: React.ComponentType<{ basename?: string; children: React.ReactNode }>;
  basename?: string;
}

export function App({ Router = BrowserRouter, basename }: AppProps = {}): JSX.Element {
  // Vite stamps assets under HONUA_CONSOLE_BASE_PATH (BUILD_ARTIFACT.md).
  // React Router needs the matching basename so /console/studio resolves to
  // the /studio route on direct navigation under a subpath deployment.
  const effectiveBasename = basename ?? import.meta.env.BASE_URL;
  return (
    <Router basename={effectiveBasename}>
      <AppRoutes />
    </Router>
  );
}

export default App;
